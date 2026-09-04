using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI.WorldDesign;

namespace Sim.Tests.EditMode
{
    public class LLMWorldDesignerTests
    {
        /// <summary>A fully in-memory ILLMClient — no network, no real provider — so these tests exercise LLMWorldDesigner's orchestration logic deterministically.</summary>
        private sealed class FakeLLMClient : ILLMClient
        {
            public string ProviderName => "Fake";
            public string ResponseText { get; set; } = "{\"WorldName\":\"Fake World\"}";
            public bool ShouldFail { get; set; }
            public bool ShouldThrowCancelled { get; set; }

            public Task<LLMCompletionResult> CompleteAsync(LLMCompletionRequest request, CancellationToken cancellationToken = default)
            {
                if (ShouldThrowCancelled) throw new OperationCanceledException(cancellationToken);
                if (ShouldFail) return Task.FromResult(LLMCompletionResult.Failed("simulated provider failure"));
                return Task.FromResult(LLMCompletionResult.Succeeded(ResponseText));
            }
        }

        [Test]
        public void Constructor_NullClient_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new LLMWorldDesigner(null));
        }

        [Test]
        public async Task DesignWorldAsync_ValidResponse_Succeeds()
        {
            var designer = new LLMWorldDesigner(new FakeLLMClient());
            var request = new WorldDesignRequest("Create a mountain course.");

            WorldDesignOutcome outcome = await designer.DesignWorldAsync(request);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("Fake World", outcome.Specification.WorldName);
        }

        [Test]
        public async Task DesignWorldAsync_PreservesOriginalPrompt()
        {
            const string prompt = "Create a desert canyon FPV racing course with tunnels, large rocks and 12 gates.";
            var designer = new LLMWorldDesigner(new FakeLLMClient());
            var request = new WorldDesignRequest(prompt);

            WorldDesignOutcome outcome = await designer.DesignWorldAsync(request);

            Assert.AreEqual(prompt, outcome.Specification.OriginalPrompt);
        }

        [Test]
        public async Task DesignWorldAsync_ProviderFailure_ReturnsUnavailable_DoesNotThrow()
        {
            var client = new FakeLLMClient { ShouldFail = true };
            var designer = new LLMWorldDesigner(client);
            var request = new WorldDesignRequest("prompt");

            WorldDesignOutcome outcome = null;
            Assert.DoesNotThrowAsync(async () => outcome = await designer.DesignWorldAsync(request));

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldDesignFailureReason.Unavailable, outcome.FailureReason);
        }

        [Test]
        public async Task DesignWorldAsync_MalformedResponse_ReturnsInvalidResponse()
        {
            var client = new FakeLLMClient { ResponseText = "not json at all {" };
            var designer = new LLMWorldDesigner(client);
            var request = new WorldDesignRequest("prompt");

            WorldDesignOutcome outcome = await designer.DesignWorldAsync(request);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldDesignFailureReason.InvalidResponse, outcome.FailureReason);
        }

        [Test]
        public async Task DesignWorldAsync_Cancellation_ReturnsCancelled_DoesNotThrow()
        {
            var client = new FakeLLMClient { ShouldThrowCancelled = true };
            var designer = new LLMWorldDesigner(client);
            var request = new WorldDesignRequest("prompt");

            WorldDesignOutcome outcome = null;
            Assert.DoesNotThrowAsync(async () => outcome = await designer.DesignWorldAsync(request));

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldDesignFailureReason.Cancelled, outcome.FailureReason);
        }

        [Test]
        public async Task DesignWorldAsync_NullRequest_ReturnsInvalidResponse_DoesNotThrow()
        {
            var designer = new LLMWorldDesigner(new FakeLLMClient());

            WorldDesignOutcome outcome = null;
            Assert.DoesNotThrowAsync(async () => outcome = await designer.DesignWorldAsync(null));

            Assert.IsFalse(outcome.Success);
        }

        // --- Provider stub configuration behaviour (no real network in any of these) ---

        [Test]
        public void OpenAiLLMClient_NoKey_ThrowsNotConfigured()
        {
            var client = new OpenAiLLMClient(apiKeyOverride: "");
            Assert.ThrowsAsync<LLMNotConfiguredException>(async () =>
                await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }));
        }

        [Test]
        public async Task OpenAiLLMClient_KeyPresent_FailsCleanly_NotYetImplemented()
        {
            var client = new OpenAiLLMClient(apiKeyOverride: "fake-key-for-test");
            LLMCompletionResult result = await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" });

            Assert.IsFalse(result.Success, "Real call is intentionally not implemented yet — must fail cleanly, not silently succeed.");
        }

        [Test]
        public void AnthropicLLMClient_NoKey_ThrowsNotConfigured()
        {
            var client = new AnthropicLLMClient(apiKeyOverride: "");
            Assert.ThrowsAsync<LLMNotConfiguredException>(async () =>
                await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }));
        }

        [Test]
        public void LocalLLMClient_NoEndpoint_ThrowsNotConfigured()
        {
            var client = new LocalLLMClient(endpointOverride: "");
            Assert.ThrowsAsync<LLMNotConfiguredException>(async () =>
                await client.CompleteAsync(new LLMCompletionRequest { UserPrompt = "x" }));
        }
    }
}
