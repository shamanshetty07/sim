namespace Sim.Gameplay
{
    /// <summary>
    /// DroneRecoveryController's own state machine — deliberately separate from both
    /// Sim.Core.WorldGenerationState and CourseState. Nothing here shares a switch statement,
    /// an enum, or a state value with either of those.
    /// </summary>
    public enum DroneRecoveryState
    {
        /// <summary>Normal — watching the drone's position every Tick(). No violation currently detected.</summary>
        Monitoring,

        /// <summary>An out-of-bounds/below-world violation is being confirmed (debounced) before recovery actually triggers. Returning to a valid position cancels this and returns to Monitoring.</summary>
        RecoveryPending,

        /// <summary>Transient — set only for the duration of one BeginRecovery() call, while the drone is actually being repositioned.</summary>
        Recovering,

        /// <summary>Brief post-recovery pause: checkpoint processing stays suppressed and no new violation can trigger another recovery, giving the drone a moment to stabilize at its spawn.</summary>
        Cooldown
    }
}
