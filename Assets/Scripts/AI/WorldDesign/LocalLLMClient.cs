using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Local LLM ILLMClient — a configuration-checked stub, not a real integration. Unlike
    /// OpenAI/Anthropic, "local LLM" has no single standard API — Ollama, LM Studio, llama.cpp
    /// server, and text-generation-webui all differ, though several offer an OpenAI-compatible
    /// "/v1/chat/completions" endpoint by convention. LOCAL_LLM_ENDPOINT below is this
    /// project's OWN configuration name, not a standard set by any of those tools — flagged
    /// explicitly so it's never mistaken for an official variable name the way
    /// OPENAI_API_KEY/ANTHROPIC_API_KEY genuinely are. Confirm which local server (and its
    /// actual request/response shape) is intended before implementing the real call here.
    /// </summary>
    public sealed class LocalLLMClient : ILLMClient
    {
        /// <summary>This project's own convention (e.g. "http://localhost:11434"), not a standard set by any specific local LLM server — see class remarks.</summary>
        public const string EndpointEnvironmentVariable = "LOCAL_LLM_ENDPOINT";

        private readonly string _endpointOverride;

        /// <param name="endpointOverride">Bypasses the environment-variable lookup — for tests, so they never depend on whatever happens to be set on the machine running them. Leave null for normal use.</param>
        public LocalLLMClient(string endpointOverride = null)
        {
            _endpointOverride = endpointOverride;
        }

        public string ProviderName => "Local";

        public Task<LLMCompletionResult> CompleteAsync(LLMCompletionRequest request, CancellationToken cancellationToken = default)
        {
            string endpoint = _endpointOverride ?? Environment.GetEnvironmentVariable(EndpointEnvironmentVariable);
            if (string.IsNullOrEmpty(endpoint))
                throw new LLMNotConfiguredException(ProviderName);

            return Task.FromResult(LLMCompletionResult.Failed(
                "LocalLLMClient has an endpoint configured but the real call is not yet implemented — confirm which local server/API shape first."));
        }
    }
}
