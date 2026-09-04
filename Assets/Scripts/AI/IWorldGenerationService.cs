using System.Threading;
using System.Threading.Tasks;
using Sim.WorldGeneration.Models;

namespace Sim.AI
{
    /// <summary>
    /// Provider-agnostic contract for turning a WorldGenerationRequest into a
    /// WorldGenerationOutcome. Every backend (Mock, OpenWorld Reactor, any future provider)
    /// implements exactly this — nothing elsewhere in the codebase is allowed to depend on a
    /// concrete implementation. Async because a real backend call is inherently I/O-bound
    /// (network, or at minimum a local generation process) and must not block the main thread
    /// (see docs/ARCHITECTURE.md §7 / the UI-responsiveness requirement).
    /// </summary>
    public interface IWorldGenerationService
    {
        Task<WorldGenerationOutcome> GenerateWorldAsync(WorldGenerationRequest request, CancellationToken cancellationToken = default);
    }
}
