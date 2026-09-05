using NUnit.Framework;
using Sim.Gameplay;
using Sim.Simulation;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Terrain;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Unit tests for DroneRecoveryController in isolation — a fake IGameplayClock (jumps to any
    /// value instantly, never sleeps for real seconds), a fake IDroneSpawnTarget/IDroneStateSource
    /// (same reasoning as WorldGenerationRuntimeServiceTests/CourseGameplayControllerTests: a real
    /// DroneController never gets its Rigidbody wired outside Play mode), and a real generated
    /// UnityEngine.Terrain (via the actual TerrainGenerator) wrapped in a real WorldRuntimeBounds
    /// — no reason to fake terrain sampling when Unity's own Terrain system works fine in
    /// EditMode. A real CourseGameplayController + CheckpointManager are used wherever race-state
    /// gating or checkpoint preservation is under test, exactly as CourseGameplayControllerTests
    /// already does.
    /// </summary>
    public class DroneRecoveryControllerTests
    {
        private sealed class FakeGameplayClock : IGameplayClock
        {
            public float NowSeconds { get; set; }
        }

        private sealed class FakeDroneSpawnTarget : IDroneSpawnTarget
        {
            public int PlaceCount { get; private set; }
            public Vector3 LastPosition { get; private set; }
            public Quaternion LastRotation { get; private set; }

            public void PlaceAt(Vector3 position, Quaternion rotation)
            {
                PlaceCount++;
                LastPosition = position;
                LastRotation = rotation;
            }
        }

        private sealed class FakeDroneStateSource : IDroneStateSource
        {
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.identity;
        }

        private const float TerrainWidth = 200f;
        private const float TerrainDepth = 200f;
        private static readonly Vector3 SpawnPosition = new Vector3(0f, 30f, 0f);
        private static readonly Quaternion SpawnRotation = Quaternion.Euler(0f, 45f, 0f);

        private GameObject _terrainParent;
        private GameObject _obstacleRoot;
        private WorldRuntimeBounds _bounds;
        private FakeGameplayClock _clock;
        private FakeDroneSpawnTarget _spawnTarget;
        private FakeDroneStateSource _stateSource;
        private DroneRecoveryConfig _config;

        [SetUp]
        public void SetUp()
        {
            _terrainParent = new GameObject("TerrainParent");
            var spec = new TerrainSpecification { TerrainType = "flat", Width = TerrainWidth, Depth = TerrainDepth, MaxHeight = 40f, HeightVariation01 = 0.1f };
            TerrainGenerationResult terrain = new TerrainGenerator().Generate(spec, _terrainParent.transform, new WorldSeedManager(1));
            _bounds = new WorldRuntimeBounds(terrain);

            _clock = new FakeGameplayClock();
            _spawnTarget = new FakeDroneSpawnTarget();
            _stateSource = new FakeDroneStateSource { Position = SpawnPosition };
            _config = new DroneRecoveryConfig
            {
                Enabled = true,
                RecoveryMargin = 25f,
                BelowWorldMargin = 15f,
                ConfirmationDurationSeconds = 0.5f,
                CooldownDurationSeconds = 1.5f
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_terrainParent != null) Object.DestroyImmediate(_terrainParent);
            if (_obstacleRoot != null) Object.DestroyImmediate(_obstacleRoot);
        }

        private DroneRecoveryController BuildRecovery(CourseGameplayController course = null) =>
            new DroneRecoveryController(_spawnTarget, _stateSource, course, _config, _clock);

        /// <summary>Builds a real CourseGameplayController bound to `checkpointCount` real checkpoints, in Racing state (Start + confirm the countdown) — the state DroneRecoveryController's margin-based detection is gated on. Returns the CheckpointManager too, so tests can drive checkpoint passes directly without needing any API CourseGameplayController doesn't already expose.</summary>
        private (CourseGameplayController course, CheckpointManager checkpoints) BuildRacingCourse(int checkpointCount)
        {
            _obstacleRoot = new GameObject("Obstacles");
            for (int i = checkpointCount - 1; i >= 0; i--)
            {
                var go = new GameObject($"gate_{i}");
                go.transform.SetParent(_obstacleRoot.transform);
                go.AddComponent<CheckpointTrigger>().Configure(i);
            }
            var checkpointManager = new CheckpointManager(_obstacleRoot);

            var course = new CourseGameplayController(_spawnTarget, _clock);
            course.BindToCourse(checkpointManager, SpawnPosition, SpawnRotation);
            course.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            course.Tick(); // -> Racing

            return (course, checkpointManager);
        }

        // ------------------------------------------------------------------
        // 1/2. Disabled / inside bounds -> no recovery
        // ------------------------------------------------------------------

        [Test]
        public void Disabled_OutOfBounds_NoRecovery()
        {
            _config.Enabled = false;
            (CourseGameplayController course, CheckpointManager _) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(10000f, 30f, 0f); // wildly out of bounds
            recovery.Tick();
            _clock.NowSeconds += 10f;
            recovery.Tick();

            Assert.AreEqual(DroneRecoveryState.Monitoring, recovery.State);
            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        [Test]
        public void InsideBounds_NoRecovery()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(5f, 30f, 5f); // well within terrain + margin
            recovery.Tick();

            Assert.AreEqual(DroneRecoveryState.Monitoring, recovery.State);
            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        // ------------------------------------------------------------------
        // 3/4/5. Horizontal / below-world boundary crossing, confirmed over time
        // ------------------------------------------------------------------

        [Test]
        public void CrossesHorizontalBoundary_EntersRecoveryPending()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(TerrainWidth / 2f + _config.RecoveryMargin + 5f, 30f, 0f);
            recovery.Tick();

            Assert.AreEqual(DroneRecoveryState.RecoveryPending, recovery.State);
            Assert.AreEqual(0, _spawnTarget.PlaceCount, "must not recover on the very first violating frame — confirmation duration must elapse first");
        }

        [Test]
        public void FallsBelowWorld_EntersRecoveryPending()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            float groundHeight = _bounds.SampleGroundHeight(0f, 0f);
            _stateSource.Position = new Vector3(0f, groundHeight - _config.BelowWorldMargin - 5f, 0f);
            recovery.Tick();

            Assert.AreEqual(DroneRecoveryState.RecoveryPending, recovery.State);
            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        [Test]
        public void RemainsOutsideBoundaryForConfirmationDuration_RecoveryTriggers()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(TerrainWidth / 2f + _config.RecoveryMargin + 5f, 30f, 0f);
            recovery.Tick(); // -> RecoveryPending
            _clock.NowSeconds += _config.ConfirmationDurationSeconds;
            recovery.Tick(); // still out of bounds, confirmation elapsed -> recover

            Assert.AreEqual(DroneRecoveryState.Cooldown, recovery.State);
            Assert.AreEqual(1, _spawnTarget.PlaceCount);
        }

        // ------------------------------------------------------------------
        // 6. Brief crossing + return -> no false recovery
        // ------------------------------------------------------------------

        [Test]
        public void BrieflyCrossesAndReturns_NoRecovery()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(TerrainWidth / 2f + _config.RecoveryMargin + 5f, 30f, 0f);
            recovery.Tick(); // -> RecoveryPending

            _stateSource.Position = SpawnPosition; // back inside before confirmation elapses
            _clock.NowSeconds += _config.ConfirmationDurationSeconds;
            recovery.Tick();

            Assert.AreEqual(DroneRecoveryState.Monitoring, recovery.State);
            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        // ------------------------------------------------------------------
        // 7/8. Non-finite position -> immediate recovery, regardless of confirmation duration
        // and regardless of course/race state.
        // ------------------------------------------------------------------

        [Test]
        public void NaNPosition_ImmediateRecovery_NoConfirmationNeeded()
        {
            var recovery = BuildRecovery(null); // no course at all — must still recover
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(float.NaN, 30f, 0f);
            recovery.Tick();

            Assert.AreEqual(DroneRecoveryState.Cooldown, recovery.State);
            Assert.AreEqual(1, _spawnTarget.PlaceCount);
        }

        [Test]
        public void InfinityPosition_ImmediateRecovery_NoConfirmationNeeded()
        {
            var recovery = BuildRecovery(null);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(0f, float.PositiveInfinity, 0f);
            recovery.Tick();

            Assert.AreEqual(DroneRecoveryState.Cooldown, recovery.State);
            Assert.AreEqual(1, _spawnTarget.PlaceCount);
        }

        [Test]
        public void NaNPosition_RecoversEvenWhileWaiting()
        {
            // Constructed but never started — stays Waiting. A non-finite position must still recover.
            var course = new CourseGameplayController(_spawnTarget, _clock);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick();

            Assert.AreEqual(1, _spawnTarget.PlaceCount);
        }

        // ------------------------------------------------------------------
        // 9/10/11. Recovery uses IDroneSpawnTarget, restores spawn position + rotation
        // ------------------------------------------------------------------

        [Test]
        public void Recovery_RestoresBoundSpawnPositionAndRotation()
        {
            var recovery = BuildRecovery(null);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick();

            Assert.AreEqual(1, _spawnTarget.PlaceCount);
            Assert.AreEqual(SpawnPosition, _spawnTarget.LastPosition);
            Assert.AreEqual(SpawnRotation, _spawnTarget.LastRotation);
        }

        // ------------------------------------------------------------------
        // 12/13. Checkpoint index preserved; recovery cannot register a checkpoint pass
        // ------------------------------------------------------------------

        [Test]
        public void Recovery_PreservesCurrentCheckpointIndex()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildRacingCourse(3);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            // Advance to checkpoint index 2 (passed gates 0 and 1) before crashing — driven
            // directly against the same CheckpointManager instance the course is bound to,
            // exactly like a real drone passing through those gates' triggers would.
            checkpoints.ReportCheckpointPassed(0);
            checkpoints.ReportCheckpointPassed(1);
            Assert.AreEqual(2, course.CurrentCheckpointIndex); // sanity

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick(); // immediate recovery

            Assert.AreEqual(2, course.CurrentCheckpointIndex, "checkpoint progress must be preserved across a recovery");
        }

        [Test]
        public void Recovery_SuppressesCheckpointProcessingDuringCooldown()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildRacingCourse(3);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick(); // -> Cooldown, checkpoint processing suppressed

            Assert.IsTrue(checkpoints.IsSuppressed);

            checkpoints.ReportCheckpointPassed(0); // must not advance while suppressed
            Assert.AreEqual(0, course.CurrentCheckpointIndex);

            _clock.NowSeconds += _config.CooldownDurationSeconds;
            recovery.Tick(); // cooldown elapses -> Monitoring, un-suppressed

            Assert.IsFalse(checkpoints.IsSuppressed);
        }

        // ------------------------------------------------------------------
        // 14. Recovery does not restart/act on a Finished race
        // ------------------------------------------------------------------

        [Test]
        public void FinishedRace_OutOfBounds_NoMarginBasedRecovery()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            checkpoints.ReportCheckpointPassed(0); // -> Finished (only 1 checkpoint)
            Assert.AreEqual(CourseState.Finished, course.State);

            _stateSource.Position = new Vector3(TerrainWidth / 2f + _config.RecoveryMargin + 5f, 30f, 0f);
            recovery.Tick();
            _clock.NowSeconds += _config.ConfirmationDurationSeconds + 1f;
            recovery.Tick();

            Assert.AreEqual(0, _spawnTarget.PlaceCount, "a Finished race must not be recovered/restarted just because the (now free-flying) drone wandered out of bounds");
        }

        // ------------------------------------------------------------------
        // 18. Cooldown prevents an immediate second recovery
        // ------------------------------------------------------------------

        [Test]
        public void Cooldown_PreventsImmediateSecondRecovery()
        {
            var recovery = BuildRecovery(null);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick(); // recovers once, -> Cooldown
            Assert.AreEqual(1, _spawnTarget.PlaceCount);

            // Still an invalid reading (e.g. the fake never actually "moves" the drone) —
            // Cooldown must suppress evaluating it again immediately.
            recovery.Tick();
            recovery.Tick();

            Assert.AreEqual(1, _spawnTarget.PlaceCount, "must not recover again until Cooldown has actually elapsed");
        }

        [Test]
        public void Cooldown_Elapsed_ResumesMonitoring_CanRecoverAgain()
        {
            var recovery = BuildRecovery(null);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick();
            Assert.AreEqual(1, _spawnTarget.PlaceCount);

            _clock.NowSeconds += _config.CooldownDurationSeconds;
            recovery.Tick(); // cooldown elapses -> Monitoring

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f); // invalid again
            recovery.Tick();

            Assert.AreEqual(2, _spawnTarget.PlaceCount);
        }

        // ------------------------------------------------------------------
        // 19. Events fire exactly once
        // ------------------------------------------------------------------

        [Test]
        public void RecoveryStartedAndCompleted_FireExactlyOnce()
        {
            var recovery = BuildRecovery(null);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            int startedCount = 0, completedCount = 0;
            recovery.RecoveryStarted += _ => startedCount++;
            recovery.RecoveryCompleted += () => completedCount++;

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick();
            recovery.Tick(); // Cooldown — must not re-fire
            recovery.Tick();

            Assert.AreEqual(1, startedCount);
            Assert.AreEqual(1, completedCount);
        }

        [Test]
        public void RecoveryFailed_FiresWhenNoSpawnTargetBound()
        {
            var recovery = new DroneRecoveryController(null, _stateSource, null, _config, _clock);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            int failedCount = 0, completedCount = 0;
            recovery.RecoveryFailed += _ => failedCount++;
            recovery.RecoveryCompleted += () => completedCount++;

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick();

            Assert.AreEqual(1, failedCount);
            Assert.AreEqual(0, completedCount);
        }

        // ------------------------------------------------------------------
        // 16/17. Unbind / rebind
        // ------------------------------------------------------------------

        [Test]
        public void Unbind_TickIsNoOp()
        {
            var recovery = BuildRecovery(null);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);
            recovery.Unbind();

            Assert.IsFalse(recovery.IsBound);

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick();

            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        [Test]
        public void Bind_SetsIsBoundTrue_ResetsToMonitoring()
        {
            var recovery = BuildRecovery(null);
            Assert.IsFalse(recovery.IsBound);

            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            Assert.IsTrue(recovery.IsBound);
            Assert.AreEqual(DroneRecoveryState.Monitoring, recovery.State);
        }

        [Test]
        public void Rebind_DiscardsPreviousPendingState()
        {
            var recovery = BuildRecovery(null);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _stateSource.Position = new Vector3(TerrainWidth / 2f + _config.RecoveryMargin + 5f, 30f, 0f);
            recovery.Tick(); // -> RecoveryPending

            recovery.Bind(_bounds, SpawnPosition, SpawnRotation); // simulates regeneration rebinding

            Assert.AreEqual(DroneRecoveryState.Monitoring, recovery.State);
        }

        // ------------------------------------------------------------------
        // 21. Timer keeps running through a recovery (never reset)
        // ------------------------------------------------------------------

        [Test]
        public void Timer_KeepsRunningThroughRecovery()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildRacingCourse(1);
            var recovery = BuildRecovery(course);
            recovery.Bind(_bounds, SpawnPosition, SpawnRotation);

            _clock.NowSeconds += 8f;
            float elapsedBefore = course.ElapsedSeconds;

            _stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick(); // recovery happens instantly at the current clock time

            Assert.AreEqual(elapsedBefore, course.ElapsedSeconds, 0.0001f, "the race timer must not reset/stop across a recovery");

            _clock.NowSeconds += 4f;
            Assert.AreEqual(elapsedBefore + 4f, course.ElapsedSeconds, 0.0001f, "the timer must keep advancing after a recovery, exactly as if nothing happened");
        }
    }
}
