using System;
using Sim.Simulation;
using UnityEngine;

namespace Sim.Gameplay
{
    /// <summary>
    /// The single authoritative owner of race *flow* state: Waiting -&gt; Countdown -&gt; Racing
    /// -&gt; Finished, plus Failed (course cannot run) and Resetting (transient, during one
    /// Reset() call). This is a plain C# class, not a MonoBehaviour — same pattern as
    /// Sim.Core.WorldGenerationController — constructed once by the runtime composition root
    /// (RuntimeSimulationBootstrap) and re-bound to a new CheckpointManager every time a world
    /// regenerates, rather than a new instance being created per generation. That is what
    /// guarantees no duplicate gameplay managers ever accumulate.
    ///
    /// Deliberately does NOT own the checkpoint *progression* itself (which checkpoint is next,
    /// in-order enforcement) — that stays in CheckpointManager, generated fresh by WorldGenerator
    /// alongside the checkpoint triggers themselves. This class only reacts to
    /// CheckpointManager's events and layers race-flow state + timing on top.
    ///
    /// Countdown is driven by IGameplayClock, not a Unity Coroutine — Tick() must be called
    /// periodically (once per frame is fine; Sim.Simulation.RuntimeSimulationBootstrap.Update()
    /// is the production driver) to notice when the countdown has elapsed and transition to
    /// Racing. This keeps the whole state machine testable with a fake clock that jumps to any
    /// value instantly, with no need to actually wait 3 real seconds in a test.
    /// </summary>
    public sealed class CourseGameplayController
    {
        public const float CountdownDurationSeconds = 3f;

        private readonly IDroneSpawnTarget _droneSpawnTarget;
        private readonly IGameplayClock _clock;
        private readonly RaceTimer _timer;

        private CheckpointManager _checkpointManager;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private float _countdownStartedAtSeconds;

        public CourseState State { get; private set; } = CourseState.Waiting;
        public string LastFailureReason { get; private set; }

        public int TotalCheckpoints => _checkpointManager?.TotalCheckpoints ?? 0;
        public int CurrentCheckpointIndex => _checkpointManager?.CurrentCheckpointIndex ?? 0;
        public float ElapsedSeconds => _timer.ElapsedSeconds;

        /// <summary>0 outside Countdown. Counts down from CountdownDurationSeconds to 0 while Countdown is active.</summary>
        public float CountdownRemainingSeconds => State == CourseState.Countdown
            ? Mathf.Max(0f, CountdownDurationSeconds - (_clock.NowSeconds - _countdownStartedAtSeconds))
            : 0f;

        public event Action<CourseState> StateChanged;

        /// <summary>A new course was bound and is ready for Start (TotalCheckpoints &gt; 0).</summary>
        public event Action CourseReady;

        /// <summary>Countdown reached zero; the timer just started.</summary>
        public event Action RaceStarted;

        /// <summary>The checkpoint index that was just passed, in order.</summary>
        public event Action<int> CheckpointPassed;

        /// <summary>An out-of-order checkpoint was attempted — attempted index, then the index actually required. Progression did not advance.</summary>
        public event Action<int, int> WrongCheckpointAttempted;

        public event Action RaceFinished;

        /// <summary>Fired at the end of a Reset()/Restart() call, once state is back to Waiting.</summary>
        public event Action CourseReset;

        /// <summary>The bound world has no usable checkpoint sequence — carries the human-readable reason.</summary>
        public event Action<string> CourseFailed;

        public CourseGameplayController(IDroneSpawnTarget droneSpawnTarget, IGameplayClock clock = null)
        {
            _droneSpawnTarget = droneSpawnTarget;
            _clock = clock ?? new UnityGameplayClock();
            _timer = new RaceTimer(_clock);
        }

        /// <summary>
        /// Binds to a freshly generated world's checkpoint sequence and start spawn. Always
        /// detaches from whatever course was previously bound first (see Unbind) — never leaves
        /// a subscription on a CheckpointManager belonging to a world that has since been
        /// destroyed. If the bound checkpoint sequence is empty or missing, the course goes to
        /// Failed rather than pretending to be playable; call sites must not crash on this.
        /// </summary>
        public void BindToCourse(CheckpointManager checkpointManager, Vector3 startPosition, Quaternion startRotation)
        {
            Unbind();

            if (!CourseValidator.IsValid(checkpointManager, out string failureReason))
            {
                LastFailureReason = failureReason;
                SetState(CourseState.Failed);
                CourseFailed?.Invoke(failureReason);
                return;
            }

            _checkpointManager = checkpointManager;
            _checkpointManager.CheckpointPassed += HandleCheckpointPassed;
            _checkpointManager.RaceFinished += HandleRaceFinished;
            _checkpointManager.WrongCheckpointAttempted += HandleWrongCheckpointAttempted;

            _startPosition = startPosition;
            _startRotation = startRotation;

            _timer.Reset();
            LastFailureReason = null;
            SetState(CourseState.Waiting);
            CourseReady?.Invoke();
        }

        /// <summary>
        /// Detaches from whatever course is currently bound (if any) and returns to an inactive
        /// Waiting state with zero checkpoints — used both when a new generation attempt starts
        /// (invalidating the old course before the new one exists) and when the world is
        /// cleared entirely. Safe to call repeatedly, or when nothing is bound.
        /// </summary>
        public void Unbind()
        {
            if (_checkpointManager != null)
            {
                _checkpointManager.CheckpointPassed -= HandleCheckpointPassed;
                _checkpointManager.RaceFinished -= HandleRaceFinished;
                _checkpointManager.WrongCheckpointAttempted -= HandleWrongCheckpointAttempted;
            }

            _checkpointManager = null;
            _timer.Reset();
            LastFailureReason = null;
            SetState(CourseState.Waiting);
        }

        /// <summary>Waiting -&gt; Countdown. No-op if not currently Waiting, or if no valid course (TotalCheckpoints == 0) is bound.</summary>
        public void StartRace()
        {
            if (State != CourseState.Waiting) return;
            if (_checkpointManager == null || _checkpointManager.TotalCheckpoints == 0) return;

            _countdownStartedAtSeconds = _clock.NowSeconds;
            SetState(CourseState.Countdown);
        }

        /// <summary>
        /// Call once per frame (or at least often enough that CountdownDurationSeconds passing
        /// unnoticed isn't visible) while a countdown might be running. A no-op in every other
        /// state — cheap to call unconditionally.
        /// </summary>
        public void Tick()
        {
            if (State != CourseState.Countdown) return;
            if (_clock.NowSeconds - _countdownStartedAtSeconds < CountdownDurationSeconds) return;

            // A checkpoint trigger is always "live" once bound, including during Waiting/
            // Countdown — a drone drifting through a gate's opening before GO (e.g. sitting
            // near gate 1 at spawn) would otherwise silently consume that checkpoint before the
            // race has actually begun. Resetting progression at the exact moment Racing starts
            // guarantees checkpoint 0 is genuinely required *during* the race, regardless of
            // anything that happened to the trigger beforehand.
            _checkpointManager.Reset();
            SetState(CourseState.Racing);
            _timer.Reset();
            _timer.Start();
            RaceStarted?.Invoke();
        }

        /// <summary>
        /// Stops the timer, resets checkpoint progress, places the drone back at the course's
        /// start spawn (via IDroneSpawnTarget — the same abstraction WorldGenerationRuntimeService
        /// uses, never a second drone-reset implementation), and returns to Waiting. Serves both
        /// "Reset" (e.g. after a crash) and "Restart" (after Finished) — both are exactly this
        /// same operation; the UI decides when to offer it. No-op if no course is bound.
        /// </summary>
        public void Reset()
        {
            if (_checkpointManager == null) return;

            SetState(CourseState.Resetting);

            _checkpointManager.Reset();
            _timer.Reset();
            _droneSpawnTarget?.PlaceAt(_startPosition, _startRotation);
            LastFailureReason = null;

            SetState(CourseState.Waiting);
            CourseReset?.Invoke();
        }

        private void HandleCheckpointPassed(int index) => CheckpointPassed?.Invoke(index);

        private void HandleWrongCheckpointAttempted(int attemptedIndex, int requiredIndex) =>
            WrongCheckpointAttempted?.Invoke(attemptedIndex, requiredIndex);

        private void HandleRaceFinished()
        {
            _timer.Stop();
            SetState(CourseState.Finished);
            RaceFinished?.Invoke();
        }

        private void SetState(CourseState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
