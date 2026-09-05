namespace Sim.Gameplay
{
    /// <summary>
    /// An immutable snapshot of exactly one completed run — produced once, at the moment
    /// CourseGameplayController.RaceFinished fires (see CourseResultsController), and never
    /// mutated afterward. Every field here is a plain value copied out at that instant; nothing
    /// on this type reads a live clock, a live CheckpointManager, or anything else that could
    /// change after construction — that's the whole point of it being a *result*, not a live
    /// view of ongoing gameplay state.
    ///
    /// Deliberately NOT persisted anywhere (no PlayerPrefs/JSON/disk/database/cloud) — this is
    /// session-only runtime data. See docs/PHASE_13_COURSE_RESULTS.md "Persistence boundary".
    /// </summary>
    public sealed class CourseResult
    {
        /// <summary>The race timer's value at the exact instant the race finished — frozen, never recalculated from a moving clock afterward.</summary>
        public float ElapsedSeconds { get; }

        public int CompletedCheckpoints { get; }
        public int TotalCheckpoints { get; }

        /// <summary>Successful automatic recoveries during this run only — never manual Reset, initial spawn, or world regeneration. See DroneRecoveryController.RecoveryCountThisRun.</summary>
        public int RecoveryCount { get; }

        /// <summary>Always true for a CourseResult produced via a genuine finish (the only way one is ever constructed today) — kept as an explicit field rather than assumed, so a future producer of partial/abandoned-run data has somewhere to say otherwise without changing this type's shape.</summary>
        public bool IsCompleted { get; }

        /// <summary>The generated world's seed, if one was recorded — 0 if unknown. Available for a future persistence phase; deliberately not surfaced prominently in the results UI (see docs/PHASE_13_COURSE_RESULTS.md).</summary>
        public int WorldSeed { get; }

        public CourseResult(
            float elapsedSeconds,
            int completedCheckpoints,
            int totalCheckpoints,
            int recoveryCount,
            bool isCompleted,
            int worldSeed)
        {
            ElapsedSeconds = elapsedSeconds;
            CompletedCheckpoints = completedCheckpoints;
            TotalCheckpoints = totalCheckpoints;
            RecoveryCount = recoveryCount;
            IsCompleted = isCompleted;
            WorldSeed = worldSeed;
        }
    }
}
