using System;
using Sim.Simulation;
using Sim.WorldGeneration;
using UnityEngine;

namespace Sim.Gameplay
{
    /// <summary>
    /// Automatic crash/fall recovery for a generated FPV course. Plain C# class — same pattern
    /// as CourseGameplayController and Sim.Core.WorldGenerationController — constructed once by
    /// the runtime composition root and re-bound to a new world's bounds/spawn every
    /// regeneration, never recreated (no duplicate recovery managers can accumulate).
    ///
    /// Deliberately does NOT infer crashes from orientation, angular velocity, or linear
    /// velocity: Acro/Horizon mode both permit aggressive rotation, the drone can legitimately
    /// fly inverted, and a high rotation/speed reading is completely ordinary FPV flight, not a
    /// crash signal. The only two things that count as "unrecoverable" here are a position the
    /// generated world itself defines as out of bounds (horizontally, or fallen below the
    /// terrain by more than a margin) and a position that has become physically meaningless
    /// (NaN/Infinity) — see docs/PHASE_12_RECOVERY.md "Detection strategy" for the full
    /// reasoning, including why no maximum-altitude check exists.
    ///
    /// Owns none of: flight physics (DronePhysics/DroneFlightModel/FlightModeController are
    /// never touched), world generation, checkpoint ordering (CheckpointManager still owns
    /// that; this only ever calls its narrow SetSuppressed passthrough via
    /// CourseGameplayController), or race-flow state (CourseGameplayController's State is only
    /// ever read, never written, by this class) — the one exception being
    /// <see cref="RecoveryCountThisRun"/> (Phase 13), which this class resets purely by
    /// *reacting* to CourseGameplayController.RaceStarted, never by writing course state itself.
    /// </summary>
    public sealed class DroneRecoveryController
    {
        private readonly IDroneSpawnTarget _spawnTarget;
        private readonly IDroneStateSource _stateSource;
        private readonly CourseGameplayController _course;
        private readonly DroneRecoveryConfig _config;
        private readonly IGameplayClock _clock;

        private WorldRuntimeBounds _bounds;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private float _pendingSinceSeconds;
        private float _cooldownStartedAtSeconds;

        public DroneRecoveryState State { get; private set; } = DroneRecoveryState.Monitoring;

        /// <summary>True once a world's bounds/spawn have been bound (and not since Unbind()) — exposed for tests/observability, not consumed by any decision inside this class.</summary>
        public bool IsBound => _bounds != null;

        /// <summary>
        /// Phase 13: count of successful automatic recoveries during the *current* run only —
        /// manual Reset, initial spawn placement, and world regeneration never touch this (none
        /// of those go through BeginRecovery at all). Reset to 0 whenever CourseGameplayController
        /// raises RaceStarted (a fresh race is beginning) and defensively on Bind()/Unbind() too,
        /// so a stale count from an abandoned run can never leak into a later one.
        /// </summary>
        public int RecoveryCountThisRun { get; private set; }

        /// <summary>Raised the instant a recovery begins (before the drone is actually moved) — carries a human-readable reason ("non-finite position", "crossed recovery boundary", etc.).</summary>
        public event Action<string> RecoveryStarted;

        /// <summary>Raised once the drone has actually been placed back at the bound spawn.</summary>
        public event Action RecoveryCompleted;

        /// <summary>Raised instead of RecoveryCompleted if a recovery was attempted but could not actually be carried out (e.g. no drone spawn target bound) — a clean failure, never a crash or a silently-pretended success.</summary>
        public event Action<string> RecoveryFailed;

        public DroneRecoveryController(
            IDroneSpawnTarget spawnTarget,
            IDroneStateSource stateSource,
            CourseGameplayController course,
            DroneRecoveryConfig config = null,
            IGameplayClock clock = null)
        {
            _spawnTarget = spawnTarget;
            _stateSource = stateSource;
            _course = course;
            _config = config ?? new DroneRecoveryConfig();
            _clock = clock ?? new UnityGameplayClock();

            // The one event subscription this class holds — resetting its own internal counter
            // reactively is not "writing course state" (see class remarks); _course is a
            // permanent, session-lifetime instance (never rebound), so no unsubscription is
            // needed, matching how this class already treats _course elsewhere.
            if (_course != null)
                _course.RaceStarted += HandleRaceStarted;
        }

        private void HandleRaceStarted() => RecoveryCountThisRun = 0;

        /// <summary>
        /// Binds to a freshly generated world's bounds and start spawn. Always safe to call
        /// again for a regenerated world — always resets to Monitoring, discarding whatever
        /// pending/cooldown state the previous world's bounds were in.
        /// </summary>
        public void Bind(WorldRuntimeBounds bounds, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            _bounds = bounds;
            _spawnPosition = spawnPosition;
            _spawnRotation = spawnRotation;
            State = DroneRecoveryState.Monitoring;
            RecoveryCountThisRun = 0; // defensive — RaceStarted already resets this at the start of every real race, but a new world must never inherit a stale count from an abandoned one
        }

        /// <summary>
        /// Discards the currently bound world's bounds — used both when a new generation attempt
        /// starts (invalidating the old bounds before the new ones exist) and when the world is
        /// cleared entirely. Tick() no-ops with nothing bound. Safe to call repeatedly.
        /// </summary>
        public void Unbind()
        {
            _bounds = null;
            State = DroneRecoveryState.Monitoring;
            RecoveryCountThisRun = 0;
        }

        /// <summary>
        /// Call once per frame. Lightweight: no allocations, no scene search, no repeated
        /// GetComponent — every dependency here was cached at construction/Bind time. A no-op
        /// whenever automatic recovery is disabled, no world is bound, or no drone state source
        /// is available.
        /// </summary>
        public void Tick()
        {
            if (!_config.Enabled) return;
            if (_bounds == null) return;
            if (_stateSource == null) return;

            if (State == DroneRecoveryState.Cooldown)
            {
                if (_clock.NowSeconds - _cooldownStartedAtSeconds < _config.CooldownDurationSeconds) return;

                _course?.SetCheckpointProcessingSuppressed(false);
                State = DroneRecoveryState.Monitoring;
                return;
            }

            if (State == DroneRecoveryState.Recovering) return; // BeginRecovery runs synchronously; this state should never actually be observed across a Tick, but never re-enter it if it somehow were

            EvaluatePosition();
        }

        private void EvaluatePosition()
        {
            Vector3 position = _stateSource.Position;

            if (!IsFinite(position))
            {
                // A raw safety net, independent of race state: an invalid transform must never
                // be allowed to keep propagating through the scene, regardless of whether the
                // course is Waiting, Racing, or Finished.
                BeginRecovery("Drone position became non-finite (NaN/Infinity).");
                return;
            }

            if (!IsOutOfBounds(position))
            {
                State = DroneRecoveryState.Monitoring;
                return;
            }

            // Out-of-bounds/below-world recovery is tied to actually Racing — a drone left
            // sitting somewhere odd while Waiting/mid-Countdown/after Finished should not be
            // yanked back; only a clearly invalid (non-finite) position does that regardless of
            // state (handled above). If no course is bound at all, there is no race state to
            // gate on, so recovery proceeds unconditionally.
            if (_course != null && _course.State != CourseState.Racing)
            {
                State = DroneRecoveryState.Monitoring;
                return;
            }

            if (State != DroneRecoveryState.RecoveryPending)
            {
                State = DroneRecoveryState.RecoveryPending;
                _pendingSinceSeconds = _clock.NowSeconds;
                return;
            }

            if (_clock.NowSeconds - _pendingSinceSeconds >= _config.ConfirmationDurationSeconds)
                BeginRecovery("Drone crossed the course recovery boundary.");
        }

        private bool IsOutOfBounds(Vector3 position)
        {
            float minX = _bounds.Origin.x - _config.RecoveryMargin;
            float maxX = _bounds.Origin.x + _bounds.Width + _config.RecoveryMargin;
            float minZ = _bounds.Origin.z - _config.RecoveryMargin;
            float maxZ = _bounds.Origin.z + _bounds.Depth + _config.RecoveryMargin;

            if (position.x < minX || position.x > maxX || position.z < minZ || position.z > maxZ)
                return true;

            // Only sampled once horizontally within bounds+margin — Terrain.SampleHeight is not
            // guaranteed meaningful far outside the terrain's own footprint, and it doesn't need
            // to be: a horizontal violation already returned true above in that case.
            float groundHeight = _bounds.SampleGroundHeight(position.x, position.z);
            return position.y < groundHeight - _config.BelowWorldMargin;
        }

        private void BeginRecovery(string reason)
        {
            State = DroneRecoveryState.Recovering;
            RecoveryStarted?.Invoke(reason);

            if (_spawnTarget == null)
            {
                RecoveryFailed?.Invoke("No drone spawn target is bound — cannot recover.");
                State = DroneRecoveryState.Cooldown;
                _cooldownStartedAtSeconds = _clock.NowSeconds;
                return;
            }

            // Suppressed for the whole Cooldown window, not just this instant — SpawnResolver's
            // own overlap check ignores triggers (QueryTriggerInteraction.Ignore), so a spawn
            // point is not guaranteed clear of a checkpoint trigger volume; staying suppressed
            // until the drone has had a moment to settle is what actually guarantees the
            // teleport can never register as passing (or wrongly attempting) a checkpoint.
            _course?.SetCheckpointProcessingSuppressed(true);

            // Same spawn transform Phase 8/9 established and Phase 11's manual Reset already
            // uses — SetSpawn + ResetToSpawn under the hood, zeroing velocity/angular velocity
            // and returning to the spawn orientation exactly like any other reset. Checkpoint
            // progress (CurrentCheckpointIndex) is never touched here — only the drone moves.
            _spawnTarget.PlaceAt(_spawnPosition, _spawnRotation);
            RecoveryCountThisRun++;

            State = DroneRecoveryState.Cooldown;
            _cooldownStartedAtSeconds = _clock.NowSeconds;
            RecoveryCompleted?.Invoke();
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
