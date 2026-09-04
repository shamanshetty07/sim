namespace Sim.WorldGeneration.Validation
{
    /// <summary>
    /// Hard limits WorldSpecificationValidator enforces. Centralized here (not inline in the
    /// validator) so they're easy to find/tune, and so other code (a future debug overlay,
    /// generation-progress UI) can reference the same numbers rather than duplicating them.
    /// Deliberately generous but finite — the goal is preventing a malicious/malformed
    /// generation from creating millions of objects or degenerate geometry, not constraining
    /// legitimate large worlds ("a huge mountain FPV course").
    /// </summary>
    public static class WorldGenerationLimits
    {
        public const float MaxTerrainDimensionMeters = 20000f;
        public const float MinTerrainDimensionMeters = 10f;
        public const float MaxTerrainHeightMeters = 5000f;

        public const int MaxObjectCountPerCategory = 20000;
        public const int MaxEnvironmentObjectCategories = 64;

        public const int MaxObstacleCount = 2000;

        /// <summary>Obstacle scale components below this are treated as degenerate (would produce a broken/invisible collider or mesh).</summary>
        public const float MinObstacleScaleComponent = 0.01f;
        public const float MaxObstacleScaleComponent = 1000f;
    }
}
