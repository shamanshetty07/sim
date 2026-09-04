namespace Sim.Core
{
    /// <summary>
    /// Observable lifecycle state of one full prompt-to-playable-world attempt. A UI drives
    /// its Generate/Cancel/Clear buttons and status text off this alone.
    ///
    /// Renamed/extended Phase 9 from the Phase 6/8 version (Requesting -&gt; Designing,
    /// Completed -&gt; Ready, added Generating) to match this phase's explicit state vocabulary
    /// and to reflect that WorldGenerationController now drives the pipeline all the way
    /// through Unity world construction, not just design+validation — the same enum, extended
    /// in place, not a second competing state system.
    /// </summary>
    public enum WorldGenerationState
    {
        Idle,

        /// <summary>Waiting on IWorldDesigner (Mock or a real LLM provider).</summary>
        Designing,

        /// <summary>Running WorldSpecificationValidator against the designed specification.</summary>
        Validating,

        /// <summary>Running WorldGenerator against the validated specification — synchronous, main-thread Unity object construction.</summary>
        Generating,

        /// <summary>A playable world exists; GeneratedWorldResult/LastValidSpecification are populated.</summary>
        Ready,

        Failed,
        Cancelled
    }
}
