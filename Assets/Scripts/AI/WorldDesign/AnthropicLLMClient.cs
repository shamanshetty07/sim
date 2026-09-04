using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Anthropic (Claude) ILLMClient — a configuration-checked stub, not a real integration.
    /// No Anthropic API key exists in this project's environment as of Phase 7 (checked:
    /// ANTHROPIC_API_KEY, the standard name Anthropic's own SDKs use). Per this phase's
    /// explicit instruction, the real Messages API call is intentionally not written here yet:
    /// to complete it, POST https://api.anthropic.com/v1/messages with headers
    /// "x-api-key: &lt;key&gt;" and "anthropic-version: &lt;date&gt;", a JSON body of
    /// {"model": "...", "system": systemPrompt, "max_tokens": N, "messages": [{"role":"user",
    /// "content": userPrompt}]}, reading content[0].text from the response — via
    /// UnityWebRequest, matching OpenWorldReactorWorldGenerationService's existing pattern.
    /// </summary>
    public sealed class AnthropicLLMClient : ILLMClient
    {
        public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

        private readonly string _apiKeyOverride;

        /// <param name="apiKeyOverride">Bypasses the environment-variable lookup — for tests, so they never depend on whatever happens to be set on the machine running them. Leave null for normal use.</param>
        public AnthropicLLMClient(string apiKeyOverride = null)
        {
            _apiKeyOverride = apiKeyOverride;
        }

        public string ProviderName => "Anthropic";

        public Task<LLMCompletionResult> CompleteAsync(LLMCompletionRequest request, CancellationToken cancellationToken = default)
        {
            string apiKey = _apiKeyOverride ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            if (string.IsNullOrEmpty(apiKey))
                throw new LLMNotConfiguredException(ProviderName);

            return Task.FromResult(LLMCompletionResult.Failed(
                "AnthropicLLMClient has an API key configured but the real Messages API call is not yet implemented."));
        }
    }
}
