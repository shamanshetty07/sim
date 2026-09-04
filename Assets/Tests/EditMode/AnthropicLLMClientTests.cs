using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Sim.AI.WorldDesign;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Exercises AnthropicLLMClient's request-building and response-parsing logic against a
    /// fully in-memory FakeHttpTransport — no real network call anywhere in this file, per this
    /// phase's explicit "do not make automated tests depend on a real API key" instruction. See
    /// docs/PHASE_10_REAL_LLM.md "Real-provider smoke testing" for the one manual, credential-
    /// gated real-network check this class intentionally does not attempt to automate.
    /// </summary>
    public class AnthropicLLMClientTests
    {
        private sealed class FakeHttpTransport : IHttpTransport
        {
            public int CallCount { get; private set; }
            public string LastUrl { get; private set; }
            public IReadOnlyDictionary<string, string> LastHeaders { get; private set; }
            public string LastBody { get; private set; }

            /// <summary>Defaults to an immediate 200 with an empty tool_use input — overridden per test.</summary>
            public System.Func<CancellationToken, Task<HttpTransportResponse>> Handler { get; set; } =
                _ => Task.FromResult(HttpTransportResponse.Completed(200, SuccessBody("{}")));

            public Task<HttpTransportResponse> PostJsonAsync(string url, IReadOnlyDictionary<string, string> headers, string jsonBody, CancellationToken cancellationToken = default)
            {
                CallCount++;
                LastUrl = url;
                LastHeaders = headers;
                LastBody = jsonBody;
                return Handler(cancellationToken);
            }
        }

        /// <summary>A minimal, realistic Anthropic Messages API response envelope carrying one forced tool_use block, matching platform.claude.com/docs/en/api/messages' documented response shape.</summary>
        private static string SuccessBody(string inputJson) =>
            "{\"id\":\"msg_test\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-sonnet-5\"," +
            "\"stop_reason\":\"tool_use\",\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_1\"," +
            "\"name\":\"emit_world_specification\",\"input\":" + inputJson + "}]}";

        private static string ErrorBody(string type, string message) =>
            "{\"type\":\"error\",\"error\":{\"type\":\"" + type + "\",\"message\":\"" + message + "\"},\"request_id\":\"req_test\"}";

        /// <summary>
        /// Never completes on its own — mirrors the real UnityWebRequestHttpTransport contract
        /// (reacts only to the token it's given). Awaiting Task.Delay directly, rather than
        /// wrapping it in ContinueWith, matters here: ContinueWith runs its continuation even
        /// when the antecedent was cancelled unless told otherwise, which would swallow the
        /// OperationCanceledException this test relies on propagating out of the returned Task.
        /// </summary>
        private static async Task<HttpTransportResponse> HangUntilCancelled(CancellationToken ct)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return null;
        }

        [Test]
        public void NoApiKey_DoesNotSendAnyRequest()
        {
            var transport = new FakeHttpTransport();
            var client = new AnthropicLLMClient(apiKeyOverride: "", transport: transport);

            Assert.ThrowsAsync<LLMNotConfiguredException>(async () =>
                await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }));

            Assert.AreEqual(0, transport.CallCount, "An unconfigured client must never reach the transport at all.");
        }

        [Test]
        public async Task UserPrompt_IsSentIntact_NeverKeywordReduced()
        {
            const string prompt = "Create a cinematic Himalayan FPV racing course with 15 gates.";
            var transport = new FakeHttpTransport();
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            await client.CompleteAsync(new LLMCompletionRequest { SystemPrompt = "system", UserPrompt = prompt });

            var body = JObject.Parse(transport.LastBody);
            Assert.AreEqual(prompt, (string)body["messages"][0]["content"]);
        }

        [Test]
        public async Task Request_UsesConfiguredModel()
        {
            var transport = new FakeHttpTransport();
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", modelOverride: "claude-haiku-4-5", transport: transport);

            await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            var body = JObject.Parse(transport.LastBody);
            Assert.AreEqual("claude-haiku-4-5", (string)body["model"]);
        }

        [Test]
        public async Task Request_UsesDefaultModel_WhenNoneConfigured()
        {
            var transport = new FakeHttpTransport();
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            var body = JObject.Parse(transport.LastBody);
            Assert.AreEqual(AnthropicLLMClient.DefaultModel, (string)body["model"]);
        }

        [Test]
        public async Task Request_AuthenticationHeaders_AreCorrectlyConstructed()
        {
            var transport = new FakeHttpTransport();
            var client = new AnthropicLLMClient(apiKeyOverride: "my-secret-key", transport: transport);

            await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            Assert.AreEqual("my-secret-key", transport.LastHeaders["x-api-key"]);
            Assert.AreEqual(AnthropicLLMClient.ApiVersion, transport.LastHeaders["anthropic-version"]);
            Assert.AreEqual(AnthropicLLMClient.MessagesEndpoint, transport.LastUrl);
        }

        [Test]
        public async Task Request_ForcesTheStructuredOutputTool_NeverFreeform()
        {
            var transport = new FakeHttpTransport();
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            var body = JObject.Parse(transport.LastBody);
            Assert.AreEqual("tool", (string)body["tool_choice"]["type"]);
            Assert.AreEqual("emit_world_specification", (string)body["tool_choice"]["name"]);
            Assert.AreEqual(1, ((JArray)body["tools"]).Count);
            Assert.AreEqual(true, (bool)body["tools"][0]["strict"]);
            Assert.IsFalse(body.ContainsKey("temperature"), "temperature must never be sent — deprecated/restricted on current models.");
        }

        [Test]
        public async Task SuccessfulResponse_ExtractsToolInputAsText()
        {
            var transport = new FakeHttpTransport
            {
                Handler = _ => Task.FromResult(HttpTransportResponse.Completed(200, SuccessBody("{\"WorldName\":\"Canyon Run\"}")))
            };
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            LLMCompletionResult result = await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            Assert.IsTrue(result.Success);
            var parsedInput = JObject.Parse(result.Text);
            Assert.AreEqual("Canyon Run", (string)parsedInput["WorldName"]);
        }

        [Test]
        public async Task SuccessfulResponse_FlowsThroughExistingParser_IntoAWorldSpecification()
        {
            var transport = new FakeHttpTransport
            {
                Handler = _ => Task.FromResult(HttpTransportResponse.Completed(200,
                    SuccessBody("{\"WorldName\":\"Canyon Run\",\"Terrain\":{\"TerrainType\":\"canyon\"}}")))
            };
            var designer = new LLMWorldDesigner(new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport));

            WorldDesignOutcome outcome = await designer.DesignWorldAsync(new WorldDesignRequest("Create a canyon course."));

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("Canyon Run", outcome.Specification.WorldName);
            Assert.AreEqual("canyon", outcome.Specification.Terrain.TerrainType);
            Assert.AreEqual("Create a canyon course.", outcome.Specification.OriginalPrompt);
        }

        [Test]
        public async Task MaliciousTypeInjection_InToolInput_NeverExecutesOrThrows_StaysInertData()
        {
            var transport = new FakeHttpTransport
            {
                Handler = _ => Task.FromResult(HttpTransportResponse.Completed(200,
                    SuccessBody("{\"WorldName\":\"$type attempt\",\"$type\":\"System.Object, mscorlib\"}")))
            };
            var designer = new LLMWorldDesigner(new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport));

            WorldDesignOutcome outcome = await designer.DesignWorldAsync(new WorldDesignRequest("prompt"));

            Assert.IsTrue(outcome.Success, "A $type field must be safely ignored, not treated as a parse failure.");
            Assert.AreEqual("$type attempt", outcome.Specification.WorldName, "The rest of the object must still parse normally as inert data.");
            Assert.IsInstanceOf<Sim.WorldGeneration.Models.WorldSpecification>(outcome.Specification, "Must still be the real, closed WorldSpecification type — never an arbitrary type resolved from the payload.");
        }

        [Test]
        public async Task MalformedResponseJson_FailsCleanly()
        {
            var transport = new FakeHttpTransport
            {
                Handler = _ => Task.FromResult(HttpTransportResponse.Completed(200, "not json at all {"))
            };
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            LLMCompletionResult result = await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            Assert.IsFalse(result.Success);
        }

        [Test]
        public async Task ResponseMissingToolUseBlock_FailsCleanly()
        {
            var transport = new FakeHttpTransport
            {
                Handler = _ => Task.FromResult(HttpTransportResponse.Completed(200,
                    "{\"content\":[{\"type\":\"text\",\"text\":\"I refuse to use tools.\"}]}"))
            };
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            LLMCompletionResult result = await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            Assert.IsFalse(result.Success);
        }

        [TestCase(401, "authentication_error", "invalid x-api-key")]
        [TestCase(429, "rate_limit_error", "rate limited")]
        [TestCase(500, "api_error", "internal error")]
        public async Task HttpErrorStatus_FailsCleanly_NeverThrows(int status, string errorType, string message)
        {
            var transport = new FakeHttpTransport
            {
                Handler = _ => Task.FromResult(HttpTransportResponse.Completed(status, ErrorBody(errorType, message)))
            };
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            LLMCompletionResult result = null;
            Assert.DoesNotThrowAsync(async () => result = await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }));

            Assert.IsFalse(result.Success);
        }

        [Test]
        public async Task ConnectionError_FailsCleanly_NeverThrows()
        {
            var transport = new FakeHttpTransport
            {
                Handler = _ => Task.FromResult(HttpTransportResponse.ConnectionError("Cannot connect to destination host"))
            };
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport);

            LLMCompletionResult result = null;
            Assert.DoesNotThrowAsync(async () => result = await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }));

            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Timeout_ThrowsLLMRequestTimeoutException_NotGenericFailure()
        {
            var transport = new FakeHttpTransport
            {
                // Never completes on its own — only reacts to the token it's given, exactly
                // like the real UnityWebRequestHttpTransport contract.
                Handler = HangUntilCancelled
            };
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport, timeoutSecondsOverride: 0);

            Assert.ThrowsAsync<LLMRequestTimeoutException>(async () =>
                await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }));
        }

        [Test]
        public void CallerCancellation_ThrowsOperationCanceledException_NotTimeout()
        {
            var transport = new FakeHttpTransport
            {
                Handler = HangUntilCancelled
            };
            // A generous timeout — cancellation must win the race, not the timeout.
            var client = new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport, timeoutSecondsOverride: 300);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<System.OperationCanceledException>(async () =>
                await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }, cts.Token));
        }

        [Test]
        public async Task LLMWorldDesigner_Timeout_ReportsTimeoutFailureReason()
        {
            var transport = new FakeHttpTransport
            {
                Handler = HangUntilCancelled
            };
            var designer = new LLMWorldDesigner(new AnthropicLLMClient(apiKeyOverride: "test-key", transport: transport, timeoutSecondsOverride: 0));

            WorldDesignOutcome outcome = await designer.DesignWorldAsync(new WorldDesignRequest("prompt"));

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldDesignFailureReason.Timeout, outcome.FailureReason);
        }

        [Test]
        public async Task LLMWorldDesigner_NotConfigured_ReportsNotConfiguredFailureReason()
        {
            var designer = new LLMWorldDesigner(new AnthropicLLMClient(apiKeyOverride: ""));

            WorldDesignOutcome outcome = await designer.DesignWorldAsync(new WorldDesignRequest("prompt"));

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldDesignFailureReason.NotConfigured, outcome.FailureReason);
        }
    }
}
