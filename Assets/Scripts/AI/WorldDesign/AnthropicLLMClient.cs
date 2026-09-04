using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Real Anthropic (Claude) ILLMClient — Phase 10's implemented provider (see
    /// docs/PHASE_10_REAL_LLM.md for why Anthropic was the one chosen when no provider was yet
    /// configured in this project). Calls the real Messages API
    /// (POST https://api.anthropic.com/v1/messages, headers x-api-key/anthropic-version/
    /// content-type — verified against platform.claude.com/docs/en/api/messages, Anthropic's
    /// current official documentation, not invented or guessed) via IHttpTransport (real
    /// transport: UnityWebRequestHttpTransport, matching
    /// OpenWorldReactorWorldGenerationService's established non-blocking polling pattern).
    ///
    /// Structured output: forces the model to call one tool
    /// (<see cref="ToolName"/>, "strict": true, input_schema = WorldSpecificationToolSchema —
    /// the same canonical schema any future structured-output-capable provider would reuse) via
    /// tool_choice {"type":"tool","name":"..."} — Anthropic's own official structured-output
    /// mechanism (platform.claude.com/docs/en/agents-and-tools/tool-use, .../strict-tool-use),
    /// not "please output JSON" free text. The tool_use block's "input" object IS already
    /// WorldSpecification-shaped JSON — its text is handed to
    /// IWorldSpecificationJsonParser.TryParse exactly as LLMWorldDesigner already does for any
    /// ILLMClient, so no new deserialization path is introduced and every existing
    /// TypeNameHandling.None / $type-injection protection applies unchanged. This class does
    /// nothing with the model's output except extract that one JSON object — the resulting
    /// WorldSpecification is still validated downstream (WorldSpecificationValidator) regardless
    /// of the schema-enforcement guarantee; structured output narrows the failure surface, it
    /// does not replace validation.
    ///
    /// temperature/top_p/top_k are deliberately never sent: verified (same documentation) that
    /// they are deprecated on current-generation Claude models and any value other than each
    /// parameter's own default is rejected with a 400 error — so LLMCompletionRequest.Temperature
    /// is intentionally not wired through here.
    ///
    /// Timeout vs. cancellation: a CancellationTokenSource with a bounded timeout is linked with
    /// the caller's token before the transport call; which one actually fired is checked after a
    /// cancellation, so a caller-initiated Cancel() still surfaces as OperationCanceledException
    /// (-&gt; WorldDesignFailureReason.Cancelled) while this class's own timeout surfaces as
    /// LLMRequestTimeoutException (-&gt; WorldDesignFailureReason.Timeout) — both already-existing
    /// enum values, not new ones.
    /// </summary>
    public sealed class AnthropicLLMClient : ILLMClient
    {
        /// <summary>Genuinely standard name used by Anthropic's own SDKs/CLI.</summary>
        public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

        /// <summary>This project's own configuration name (Anthropic has no single "default model" env var convention) — overrides <see cref="DefaultModel"/> when set.</summary>
        public const string ModelEnvironmentVariable = "ANTHROPIC_MODEL";

        /// <summary>This project's own configuration name — overrides <see cref="DefaultTimeoutSeconds"/> when set to a positive integer.</summary>
        public const string TimeoutSecondsEnvironmentVariable = "ANTHROPIC_TIMEOUT_SECONDS";

        public const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";

        /// <summary>Verified current value from platform.claude.com/docs/en/api/messages.</summary>
        public const string ApiVersion = "2023-06-01";

        /// <summary>A current, real, generally-available model name (verified in Anthropic's own API examples) — not the newest/most expensive tier, a reasonable balanced default for world design. Fully overridable via <see cref="ModelEnvironmentVariable"/>.</summary>
        public const string DefaultModel = "claude-sonnet-5";

        public const int DefaultTimeoutSeconds = 60;

        private const string ToolName = "emit_world_specification";

        private readonly string _apiKeyOverride;
        private readonly string _modelOverride;
        private readonly IHttpTransport _transport;
        private readonly int _timeoutSeconds;
        private readonly EnvironmentLlmCredentialsProvider _credentials;

        /// <param name="apiKeyOverride">Bypasses the environment-variable/.env.local lookup entirely (including an empty string, which means "explicitly no key") — for tests, so they never depend on whatever happens to be configured on the machine running them. Leave null for normal use.</param>
        /// <param name="modelOverride">Bypasses the model environment-variable lookup the same way. Leave null for normal use.</param>
        /// <param name="transport">Bypasses the real UnityWebRequest transport — for tests. Leave null for normal use.</param>
        /// <param name="timeoutSecondsOverride">Bypasses the timeout environment-variable lookup — for tests, so a timeout scenario can be exercised in milliseconds rather than real seconds. Leave null for normal use.</param>
        public AnthropicLLMClient(string apiKeyOverride = null, string modelOverride = null, IHttpTransport transport = null, int? timeoutSecondsOverride = null)
        {
            _apiKeyOverride = apiKeyOverride;
            _modelOverride = modelOverride;
            _transport = transport ?? new UnityWebRequestHttpTransport();
            _credentials = new EnvironmentLlmCredentialsProvider();
            _timeoutSeconds = timeoutSecondsOverride ?? ResolveTimeoutSeconds();
        }

        public string ProviderName => "Anthropic";

        public async Task<LLMCompletionResult> CompleteAsync(LLMCompletionRequest request, CancellationToken cancellationToken = default)
        {
            if (!TryResolveApiKey(out string apiKey))
                throw new LLMNotConfiguredException(ProviderName);

            string model = ResolveModel();
            string body = BuildRequestBody(request, model).ToString(Formatting.None);

            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                // Sent only as this one request header, directly to the real Anthropic
                // endpoint over HTTPS — never logged, never included in any exception message,
                // never returned to any caller. Same rule as Reactor's API key handling.
                ["x-api-key"] = apiKey,
                ["anthropic-version"] = ApiVersion
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            HttpTransportResponse response;
            try
            {
                response = await _transport.PostJsonAsync(MessagesEndpoint, headers, body, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                throw new LLMRequestTimeoutException(ProviderName, _timeoutSeconds);
            }

            if (response.IsConnectionError)
            {
                Debug.LogWarning($"[WorldDesign] Anthropic connection error: {response.ConnectionErrorMessage}");
                return LLMCompletionResult.Failed("Anthropic request failed: connection error.");
            }

            if (!response.IsSuccessStatusCode)
            {
                LogApiError(response);
                return LLMCompletionResult.Failed($"Anthropic request failed with HTTP {response.StatusCode}.");
            }

            return ParseToolInput(response.Body);
        }

        private bool TryResolveApiKey(out string apiKey)
        {
            if (_apiKeyOverride != null)
            {
                apiKey = _apiKeyOverride;
                return !string.IsNullOrEmpty(apiKey);
            }

            return _credentials.TryGetVariable(ApiKeyEnvironmentVariable, out apiKey);
        }

        private string ResolveModel()
        {
            if (_modelOverride != null) return _modelOverride;
            return _credentials.TryGetVariable(ModelEnvironmentVariable, out string model) ? model : DefaultModel;
        }

        private int ResolveTimeoutSeconds()
        {
            if (_credentials.TryGetVariable(TimeoutSecondsEnvironmentVariable, out string raw)
                && int.TryParse(raw, out int seconds) && seconds > 0)
                return seconds;

            return DefaultTimeoutSeconds;
        }

        private static JObject BuildRequestBody(LLMCompletionRequest request, string model) => new JObject
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxOutputTokens,
            ["system"] = request.SystemPrompt,
            ["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = request.UserPrompt } },
            ["tools"] = new JArray { BuildToolDefinition() },
            ["tool_choice"] = new JObject { ["type"] = "tool", ["name"] = ToolName }
        };

        private static JObject BuildToolDefinition() => new JObject
        {
            ["name"] = ToolName,
            ["description"] =
                "Emit the complete structured world specification for an FPV drone flight " +
                "simulator, matching the schema exactly. This is the only way to respond.",
            ["strict"] = true,
            ["input_schema"] = WorldSpecificationToolSchema.Build()
        };

        /// <summary>
        /// The tool_use block's "input" is already WorldSpecification-shaped JSON by
        /// construction (see class remarks) — this method's only job is finding that block and
        /// returning its input text unchanged; it never itself deserializes into a
        /// WorldSpecification or any other .NET type.
        /// </summary>
        private static LLMCompletionResult ParseToolInput(string responseBody)
        {
            JObject root;
            try
            {
                root = JObject.Parse(responseBody);
            }
            catch (JsonException ex)
            {
                Debug.LogWarning($"[WorldDesign] Anthropic response was not valid JSON: {ex.Message}");
                return LLMCompletionResult.Failed("Anthropic response was not valid JSON.");
            }

            JArray content = root["content"] as JArray;
            JToken toolUseBlock = content?.FirstOrDefault(block =>
                string.Equals((string)block["type"], "tool_use", StringComparison.Ordinal) &&
                string.Equals((string)block["name"], ToolName, StringComparison.Ordinal));

            if (toolUseBlock == null)
            {
                Debug.LogWarning("[WorldDesign] Anthropic response did not include the expected tool_use block.");
                return LLMCompletionResult.Failed("Anthropic response did not include the expected structured output.");
            }

            JToken input = toolUseBlock["input"];
            if (input == null)
                return LLMCompletionResult.Failed("Anthropic tool_use block had no input.");

            return LLMCompletionResult.Succeeded(input.ToString(Formatting.None));
        }

        /// <summary>Logs Anthropic's own safe error description (platform.claude.com/docs/en/api/errors — {"error":{"type","message"}}) when the body parses as one; never the raw request, never any header.</summary>
        private static void LogApiError(HttpTransportResponse response)
        {
            try
            {
                JObject parsed = JObject.Parse(response.Body);
                string type = parsed["error"]?["type"]?.ToString();
                string message = parsed["error"]?["message"]?.ToString();
                if (type != null)
                {
                    Debug.LogWarning($"[WorldDesign] Anthropic request failed: HTTP {response.StatusCode} ({type}) {message}");
                    return;
                }
            }
            catch (JsonException)
            {
                // Fall through — log just the status code below.
            }

            Debug.LogWarning($"[WorldDesign] Anthropic request failed: HTTP {response.StatusCode}.");
        }
    }
}
