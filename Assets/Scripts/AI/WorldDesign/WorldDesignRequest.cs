using System;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// What the user asked for, headed to the AI World Designer. Mirrors
    /// WorldGenerationRequest's prompt-preservation guarantee (Phase 5) — <see cref="Prompt"/>
    /// is the complete, unmodified natural-language text; the constructor refuses to build a
    /// request without one, so there is no code path that silently drops or reduces it.
    /// </summary>
    public sealed class WorldDesignRequest
    {
        /// <summary>The user's complete, unmodified natural-language prompt. Never null or reduced.</summary>
        public string Prompt { get; }

        public Guid RequestId { get; }

        /// <summary>If set, overrides whatever seed the designer produces — a user-specified seed always wins, for reproducibility.</summary>
        public int? Seed { get; }

        public WorldDesignConstraints Constraints { get; }

        public DateTime RequestedAtUtc { get; }

        public WorldDesignRequest(
            string prompt,
            int? seed = null,
            WorldDesignConstraints constraints = null,
            Guid? requestId = null,
            DateTime? requestedAtUtc = null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("A WorldDesignRequest requires a non-empty prompt — the prompt is the primary input to world design, not an optional field.", nameof(prompt));

            Prompt = prompt;
            Seed = seed;
            Constraints = constraints;
            RequestId = requestId ?? Guid.NewGuid();
            RequestedAtUtc = requestedAtUtc ?? DateTime.UtcNow;
        }
    }
}
