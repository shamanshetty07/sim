using System;

namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// Provenance for a generated world: which backend produced it, which request it answers,
    /// and when. Threaded through ReactorWorldResult -> WorldSpecification -> (later)
    /// WorldSaveData so a saved/reloaded world always carries a record of the prompt and
    /// backend that produced it, not just the resulting geometry description.
    /// </summary>
    public sealed class WorldGenerationMetadata
    {
        /// <summary>e.g. "OpenWorldReactor", "Mock". Not an enum — new providers shouldn't require a code change here.</summary>
        public string ProviderName { get; set; }

        /// <summary>Provider-reported version/model identifier, if any. Null if unknown/not applicable.</summary>
        public string ProviderVersion { get; set; }

        /// <summary>Echoes WorldGenerationRequest.RequestId — ties a result back to the exact prompt that produced it.</summary>
        public Guid RequestId { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>Wall-clock time the backend took to respond. Default (Zero) if not reported.</summary>
        public TimeSpan GenerationDuration { get; set; }

        /// <summary>
        /// Schema/format version of this metadata + the WorldSpecification it travels with —
        /// checked on load by Persistence (Phase 12/13) so an older/newer saved world is
        /// reported, not silently misapplied.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;
    }
}
