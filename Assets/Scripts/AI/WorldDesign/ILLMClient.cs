using System.Threading;
using System.Threading.Tasks;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Provider-neutral "send text, get text back" abstraction — the actual provider
    /// abstraction requested for Phase 7. LLMWorldDesigner contains ALL of the prompt-
    /// engineering, JSON-parsing, and validation-readiness logic exactly once; swapping
    /// OpenAI/Claude/a local model for another is swapping which ILLMClient it's constructed
    /// with, not rewriting that logic per provider. This is deliberately a single generic
    /// LLMWorldDesigner + swappable ILLMClient rather than parallel
    /// OpenAIWorldDesigner/ClaudeWorldDesigner/LocalLLMWorldDesigner classes that would each
    /// duplicate the same prompt-building and JSON-handling code — see
    /// docs/AI_WORLD_DESIGNER.md for the reasoning. OpenAiLLMClient/AnthropicLLMClient/
    /// LocalLLMClient are the concrete ILLMClient implementations satisfying that part of the
    /// brief; each is an honest "not configured" stub until real credentials exist for it,
    /// exactly like OpenWorldReactorWorldGenerationService was before Phase 6.
    /// </summary>
    public interface ILLMClient
    {
        /// <summary>e.g. "OpenAI", "Anthropic", "Local" — for logging/metadata, never for branching logic elsewhere.</summary>
        string ProviderName { get; }

        Task<LLMCompletionResult> CompleteAsync(LLMCompletionRequest request, CancellationToken cancellationToken = default);
    }
}
