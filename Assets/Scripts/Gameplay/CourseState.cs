namespace Sim.Gameplay
{
    /// <summary>
    /// CourseGameplayController's own state machine — deliberately separate from
    /// Sim.Core.WorldGenerationState (prompt -&gt; design -&gt; validate -&gt; generate; knows
    /// nothing about racing). A generated world reaching WorldGenerationState.Ready is what
    /// lets CourseGameplayController leave Waiting at all, but the two never share a state
    /// value, a switch statement, or an enum.
    /// </summary>
    public enum CourseState
    {
        /// <summary>A valid course is bound (or none has ever been bound) and idle — Start is available once TotalCheckpoints &gt; 0.</summary>
        Waiting,

        /// <summary>3-2-1-GO countdown running. Checkpoints cannot yet be passed and the timer has not started.</summary>
        Countdown,

        /// <summary>Timer running; checkpoints advance CurrentCheckpointIndex in order.</summary>
        Racing,

        /// <summary>Final checkpoint was passed in order. Timer stopped; ElapsedSeconds is now the final time.</summary>
        Finished,

        /// <summary>The bound generated world has no usable checkpoint sequence — the course cannot run. Not a crash, not a fake success.</summary>
        Failed,

        /// <summary>Transient — set for the duration of one Reset() call, before landing back on Waiting.</summary>
        Resetting
    }
}
