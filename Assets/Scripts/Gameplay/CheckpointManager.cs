using System;
using UnityEngine;

namespace Sim.Gameplay
{
    /// <summary>
    /// Owns checkpoint progression (current/completed checkpoint, in-order enforcement) —
    /// deliberately separate from ObstacleGenerator, which only builds the visual/collision
    /// geometry and unwired CheckpointTrigger components. This class discovers those triggers
    /// under a given root and wires itself in; nothing about visual construction lives here,
    /// and nothing about GameObject creation lives here either — plain C# state plus event
    /// notification.
    ///
    /// Checkpoints must be passed in order: a trigger for an index other than
    /// <see cref="CurrentCheckpointIndex"/> is ignored (reported via
    /// <see cref="WrongCheckpointAttempted"/>, not advanced), matching standard FPV racing
    /// convention (you can't skip ahead by flying through gate 5 before gates 1-4).
    ///
    /// Phase 11: race *flow* state (Waiting/Countdown/Racing/Finished/Resetting) and the race
    /// timer used to live here (as <c>RaceState</c> + <c>ElapsedSeconds</c> read straight off
    /// <c>Time.time</c>) but were pulled out into Sim.Gameplay.CourseGameplayController +
    /// RaceTimer, which is the one place that now owns "when did the race start/stop" — this
    /// class only tracks *which* checkpoint is next and reports when the sequence completes.
    /// Keeping both concerns in one class would have meant two overlapping state machines
    /// (this one's old NotStarted/InProgress/Finished vs. CourseGameplayController's
    /// Waiting/Countdown/Racing/Finished) disagreeing about when "the race" begins — this one
    /// used to start timing lazily on the first checkpoint pass, while the course should start
    /// timing at the end of the start countdown, before any checkpoint is reached.
    /// </summary>
    public sealed class CheckpointManager
    {
        public int TotalCheckpoints { get; }
        public int CurrentCheckpointIndex { get; private set; }
        public int CompletedCheckpoints { get; private set; }

        /// <summary>True once every checkpoint has been passed in order. TotalCheckpoints == 0 is never "finished" — there is nothing to finish.</summary>
        public bool IsFinished => TotalCheckpoints > 0 && CurrentCheckpointIndex >= TotalCheckpoints;

        /// <summary>
        /// Phase 12: while true, ReportCheckpointPassed is a complete no-op (no state change, no
        /// events) — used by DroneRecoveryController to guarantee a mid-race respawn teleport
        /// can never accidentally register as passing (or wrongly attempting) a checkpoint,
        /// without touching progression at all. Distinct from Reset() (Phase 11), which zeroes
        /// progress — this only pauses reporting; CurrentCheckpointIndex/CompletedCheckpoints
        /// are completely untouched while suppressed.
        /// </summary>
        public bool IsSuppressed { get; private set; }

        public void SetSuppressed(bool suppressed) => IsSuppressed = suppressed;

        /// <summary>Raised with the index that was just passed, in order.</summary>
        public event Action<int> CheckpointPassed;

        /// <summary>Raised exactly once, when the final checkpoint is passed in order.</summary>
        public event Action RaceFinished;

        /// <summary>Raised when a trigger reports an index other than CurrentCheckpointIndex — the attempted index, then the index actually required. Progression does not advance.</summary>
        public event Action<int, int> WrongCheckpointAttempted;

        public CheckpointManager(GameObject obstacleRoot)
        {
            if (obstacleRoot == null)
            {
                TotalCheckpoints = 0;
                return;
            }

            var triggers = obstacleRoot.GetComponentsInChildren<CheckpointTrigger>(includeInactive: true);
            TotalCheckpoints = triggers.Length;
            foreach (CheckpointTrigger trigger in triggers)
                trigger.SetManager(this);
        }

        /// <summary>Called by a CheckpointTrigger when the drone passes through it. Out-of-order or post-finish reports never advance progression.</summary>
        public void ReportCheckpointPassed(int index)
        {
            if (IsSuppressed) return;
            if (IsFinished) return;

            if (index != CurrentCheckpointIndex)
            {
                WrongCheckpointAttempted?.Invoke(index, CurrentCheckpointIndex);
                return;
            }

            CompletedCheckpoints++;
            CurrentCheckpointIndex++;
            CheckpointPassed?.Invoke(index);

            if (CurrentCheckpointIndex >= TotalCheckpoints)
                RaceFinished?.Invoke();
        }

        /// <summary>Returns progression to the start without touching any GameObject — used on course reset/restart, and safe to call even when TotalCheckpoints is 0.</summary>
        public void Reset()
        {
            CurrentCheckpointIndex = 0;
            CompletedCheckpoints = 0;
        }
    }
}
