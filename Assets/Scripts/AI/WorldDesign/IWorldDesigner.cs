using System.Threading;
using System.Threading.Tasks;
using Sim.WorldGeneration.Models;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// The authoritative source of world content, as of Phase 7. Interprets a user's complete
    /// natural-language prompt directly into a WorldSpecification — this is the "AI decides
    /// what the world should contain" half of the project's core principle
    /// (docs/ARCHITECTURE.md §1). Distinct namespace/folder from Sim.AI's existing Reactor-
    /// facing types (IWorldGenerationService et al.) deliberately: those remain isolated,
    /// optional, and non-authoritative (see docs/AI_WORLD_DESIGNER.md "Reactor's role") — this
    /// interface and everything behind it has no dependency on them.
    ///
    /// Implementations: MockWorldDesigner (deterministic, for testing/dev — does not interpret
    /// the prompt), LLMWorldDesigner (a real general-purpose LLM, provider-neutral via
    /// ILLMClient — see that interface for why OpenAI/Claude/local-LLM support is a client swap,
    /// not a rewrite of this layer).
    ///
    /// Returns a raw (not yet validated) WorldSpecification — validation is
    /// WorldSpecificationValidator's job, deliberately kept as a separate pipeline stage (see
    /// docs/ARCHITECTURE.md's layering rules) rather than folded into the designer.
    /// </summary>
    public interface IWorldDesigner
    {
        Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, CancellationToken cancellationToken = default);
    }
}
