using Sim.WorldGeneration.Models;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Optional bounds/hints alongside the prompt — not a replacement for it. A caller (a
    /// future UI) can nudge scale or cap obstacle count without dictating content; the prompt
    /// remains the sole source of *what* the world contains. All fields optional/nullable —
    /// an absent constraint means "let the designer/prompt decide."
    /// </summary>
    public sealed class WorldDesignConstraints
    {
        public WorldScale? PreferredScale { get; set; }

        /// <summary>Soft hint to the designer; WorldSpecificationValidator enforces the hard limit (WorldGenerationLimits.MaxObstacleCount) regardless.</summary>
        public int? MaxObstacles { get; set; }
    }
}
