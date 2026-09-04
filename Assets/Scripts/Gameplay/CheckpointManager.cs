using System;
using UnityEngine;

namespace Sim.Gameplay
{
    /// <summary>
    /// Owns race state (current/completed checkpoints, finish state, timing) — deliberately
    /// separate from ObstacleGenerator, which only builds the visual/collision geometry and
    /// unwired CheckpointTrigger components. This class discovers those triggers under a given
    /// root and wires itself in; nothing about visual construction lives here, and nothing
    /// about GameObject creation lives here either — plain C# state plus event notification.
    ///
    /// Checkpoints must be passed in order: a trigger for an index other than
    /// <see cref="CurrentCheckpointIndex"/> is ignored, matching standard FPV racing
    /// convention (you can't skip ahead by flying through gate 5 before gate 1-4).
    /// </summary>
    public sealed class CheckpointManager
    {
        public int TotalCheckpoints { get; }
        public int CurrentCheckpointIndex { get; private set; }
        public int CompletedCheckpoints { get; private set; }
        public RaceState State { get; private set; } = RaceState.NotStarted;

        public event Action<int> CheckpointPassed;
        public event Action RaceFinished;

        private float _startTime;
        private float _finishTime;

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

        /// <summary>Elapsed race time — 0 before the first checkpoint, live while in progress, frozen at the finish time once done.</summary>
        public float ElapsedSeconds
        {
            get
            {
                return State switch
                {
                    RaceState.InProgress => Time.time - _startTime,
                    RaceState.Finished => _finishTime - _startTime,
                    _ => 0f
                };
            }
        }

        /// <summary>Called by a CheckpointTrigger when the drone passes through it. Out-of-order or post-finish reports are ignored.</summary>
        public void ReportCheckpointPassed(int index)
        {
            if (State == RaceState.Finished) return;
            if (index != CurrentCheckpointIndex) return;

            if (State == RaceState.NotStarted)
            {
                State = RaceState.InProgress;
                _startTime = Time.time;
            }

            CompletedCheckpoints++;
            CurrentCheckpointIndex++;
            CheckpointPassed?.Invoke(index);

            if (CurrentCheckpointIndex >= TotalCheckpoints)
            {
                State = RaceState.Finished;
                _finishTime = Time.time;
                RaceFinished?.Invoke();
            }
        }

        /// <summary>Returns race state to NotStarted without touching any GameObject — used when a world regenerates and a fresh CheckpointManager takes over.</summary>
        public void Reset()
        {
            State = RaceState.NotStarted;
            CurrentCheckpointIndex = 0;
            CompletedCheckpoints = 0;
        }
    }
}
