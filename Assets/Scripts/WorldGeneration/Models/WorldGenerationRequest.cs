using System;

namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// What the user asked for, headed to the world-generation backend (OpenWorld Reactor, or
    /// Mock in the meantime). <see cref="Prompt"/> is the complete natural-language text the
    /// user typed — carried verbatim, never reduced to a fixed parameter set like
    /// "biome = mountain" before being sent. Everything else here is an optional hint
    /// alongside the prompt, not a replacement for it.
    ///
    /// This type is a plain data holder: no Unity object-creation types, no behaviour, safe
    /// to construct, serialize, and pass across the AI boundary.
    /// </summary>
    public sealed class WorldGenerationRequest
    {
        /// <summary>The user's complete, unmodified natural-language prompt. Never null or reduced.</summary>
        public string Prompt { get; }

        /// <summary>
        /// Uniquely identifies this request. Echoed back in the resulting
        /// <see cref="WorldGenerationMetadata"/> so a generated (and later saved/reloaded)
        /// world can always be traced back to the exact prompt that produced it.
        /// </summary>
        public Guid RequestId { get; }

        /// <summary>
        /// Deterministic seed. If null, the world-generation backend (or, failing that, the
        /// validator) assigns one — every generated world must have a concrete seed by the
        /// time it reaches Unity, but the request itself doesn't have to supply one.
        /// </summary>
        public int? Seed { get; }

        /// <summary>Coarse size hint alongside the prompt. Optional — the backend may also infer scale from the prompt text itself.</summary>
        public WorldScale? RequestedScale { get; }

        /// <summary>
        /// For "regenerate with a tweak" flows: the previously generated specification, if
        /// any, so the backend/adapter can treat this as a refinement rather than a fresh
        /// world. Null for a first-time generation.
        /// </summary>
        public WorldSpecification PreviousSpecification { get; }

        public DateTime RequestedAtUtc { get; }

        public WorldGenerationRequest(
            string prompt,
            int? seed = null,
            WorldScale? requestedScale = null,
            WorldSpecification previousSpecification = null,
            Guid? requestId = null,
            DateTime? requestedAtUtc = null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("A WorldGenerationRequest requires a non-empty prompt — the prompt is the primary input to world generation, not an optional field.", nameof(prompt));

            Prompt = prompt;
            Seed = seed;
            RequestedScale = requestedScale;
            PreviousSpecification = previousSpecification;
            RequestId = requestId ?? Guid.NewGuid();
            RequestedAtUtc = requestedAtUtc ?? DateTime.UtcNow;
        }
    }
}
