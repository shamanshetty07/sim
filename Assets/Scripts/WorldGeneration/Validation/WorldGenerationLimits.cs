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

        /// <summary>
        /// Phase 15: MaxObjectCountPerCategory and MaxEnvironmentObjectCategories bound each
        /// dimension individually, but their *product* (up to 64 * 20000 = 1,280,000
        /// GameObjects) is not itself a reasonable object count for any legitimate world — that
        /// combinatorial case is a real pathological-generation risk neither limit alone
        /// prevents. EnvironmentGenerator enforces this one as a running total across every
        /// category (explicit Count and Density01-derived counts alike), since that is the one
        /// place the actually-resolved per-category count is known for both paths — see its
        /// own remarks. Still generous relative to any real prompt ("a huge mountain FPV
        /// course" realistically asks for at most a few thousand objects total) — chosen well
        /// below MaxObjectCountPerCategory * MaxEnvironmentObjectCategories specifically so it
        /// also keeps the worst-case generated scene (and the EditMode test that exercises this
        /// limit — real primitive GameObjects, not mocked) reasonably sized.
        /// </summary>
        public const int MaxTotalEnvironmentObjectCount = 10000;

        public const int MaxObstacleCount = 2000;

        /// <summary>Obstacle scale components below this are treated as degenerate (would produce a broken/invisible collider or mesh).</summary>
        public const float MinObstacleScaleComponent = 0.01f;
        public const float MaxObstacleScaleComponent = 1000f;

        /// <summary>
        /// Phase 15: SpawnResolver tries the specified spawn position, then every alternate, in
        /// order, doing one real Physics.OverlapSphere query per attempt — a genuine (if
        /// generation-time-only, not per-frame) physics cost. Nothing previously bounded
        /// AlternateSpawnPoints' list length at all; an unusually large list (from the LLM, or a
        /// hand-edited/corrupted save file — see docs/PHASE_14_SAVE_LOAD.md) could otherwise
        /// drive an unbounded number of physics queries during one generation. This is a
        /// generous ceiling for a list whose entire purpose is "a small number of reasonable
        /// fallback positions."
        /// </summary>
        public const int MaxAlternateSpawnPoints = 32;
    }
}
