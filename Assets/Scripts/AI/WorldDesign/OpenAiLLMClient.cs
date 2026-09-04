using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// OpenAI ILLMClient — a configuration-checked stub, not a real integration. No OpenAI API
    /// key exists in this project's environment as of Phase 7 (checked: the
    /// OPENAI_API_KEY environment variable, the standard name OpenAI's own SDKs/CLI use).
    /// Per this phase's explicit instruction ("only implement the real provider once its API
    /// is configured"), the actual Chat Completions HTTP call is intentionally not written
    /// here yet, even though that API is well-documented — to complete it: POST
    /// https://api.openai.com/v1/chat/completions with header
    /// "Authorization: Bearer &lt;key&gt;", a JSON body of {"model": "...", "messages": [
    /// {"role":"system","content": systemPrompt}, {"role":"user","content": userPrompt}]},
    /// reading choices[0].message.content from the response — via UnityWebRequest, the same
    /// pattern OpenWorldReactorWorldGenerationService.MintSessionTokenAsync already
    /// establishes.
    /// </summary>
    public sealed class OpenAiLLMClient : ILLMClient
    {
        public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";

        private readonly string _apiKeyOverride;

        /// <param name="apiKeyOverride">Bypasses the environment-variable lookup — for tests, so they never depend on whatever happens to be set on the machine running them. Leave null for normal use.</param>
        public OpenAiLLMClient(string apiKeyOverride = null)
        {
            _apiKeyOverride = apiKeyOverride;
        }

        public string ProviderName => "OpenAI";

        public Task<LLMCompletionResult> CompleteAsync(LLMCompletionRequest request, CancellationToken cancellationToken = default)
        {
            string apiKey = _apiKeyOverride ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            if (string.IsNullOrEmpty(apiKey))
                throw new LLMNotConfiguredException(ProviderName);

            // Reaching here means a key is configured but the real call still isn't
            // implemented — see class remarks. Fails clearly rather than fabricating a result.
            return Task.FromResult(LLMCompletionResult.Failed(
                "OpenAiLLMClient has an API key configured but the real Chat Completions call is not yet implemented."));
        }
    }
}
