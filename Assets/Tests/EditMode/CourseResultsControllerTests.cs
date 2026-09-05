using System.Collections.Generic;
using NUnit.Framework;
using Sim.Gameplay;
using Sim.Simulation;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Unit tests for CourseResultsController — a real CourseGameplayController/CheckpointManager
    /// (built the same way CourseGameplayControllerTests already does: a hand-built hierarchy of
    /// real CheckpointTrigger components, no MonoBehaviour/DroneController involved) plus a real
    /// DroneRecoveryController where recovery-count behaviour is under test, and a fake
    /// IGameplayClock/IDroneSpawnTarget throughout — no test here sleeps for real seconds or
    /// needs a live drone.
    /// </summary>
    public class CourseResultsControllerTests
    {
        private sealed class FakeGameplayClock : IGameplayClock
        {
            public float NowSeconds { get; set; }
        }

        private sealed class FakeDroneSpawnTarget : IDroneSpawnTarget
        {
            public void PlaceAt(Vector3 position, Quaternion rotation) { }
        }

        private sealed class MutablePositionStateSource : IDroneStateSource
        {
            public Vector3 Position { get; set; } = new Vector3(1f, 2f, 3f);
            public Quaternion Rotation { get; set; } = Quaternion.identity;
        }

        private FakeGameplayClock _clock;
        private FakeDroneSpawnTarget _spawnTarget;
        private readonly List<GameObject> _obstacleRoots = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeGameplayClock();
            _spawnTarget = new FakeDroneSpawnTarget();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _obstacleRoots)
                if (root != null) Object.DestroyImmediate(root);
            _obstacleRoots.Clear();
        }

        private CheckpointManager BuildCheckpoints(int count)
        {
            var root = new GameObject("Obstacles");
            _obstacleRoots.Add(root);
            for (int i = count - 1; i >= 0; i--)
            {
                var go = new GameObject($"gate_{i}");
                go.transform.SetParent(root.transform);
                go.AddComponent<CheckpointTrigger>().Configure(i);
            }
            return new CheckpointManager(root);
        }

        private (CourseGameplayController course, CheckpointManager checkpoints) BuildBoundCourse(int checkpointCount)
        {
            CheckpointManager checkpoints = BuildCheckpoints(checkpointCount);
            var course = new CourseGameplayController(_spawnTarget, _clock);
            course.BindToCourse(checkpoints, new Vector3(1f, 2f, 3f), Quaternion.identity);
            return (course, checkpoints);
        }

        private void FinishRace(CourseGameplayController course, CheckpointManager checkpoints, int checkpointCount)
        {
            course.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            course.Tick(); // -> Racing
            for (int i = 0; i < checkpointCount; i++)
                checkpoints.ReportCheckpointPassed(i);
        }

        // ------------------------------------------------------------------
        // 1/2. Final elapsed time captured and immutable
        // ------------------------------------------------------------------

        [Test]
        public void RaceFinished_CapturesElapsedTimeAtFinishInstant()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            course.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            course.Tick();
            _clock.NowSeconds += 42.5f;
            checkpoints.ReportCheckpointPassed(0);

            Assert.AreEqual(42.5f, results.LastResult.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Result_RemainsUnchanged_AfterClockKeepsMoving()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            FinishRace(course, checkpoints, 1);
            float capturedTime = results.LastResult.ElapsedSeconds;

            _clock.NowSeconds += 1000f; // time keeps moving in the world; the stored result must not

            Assert.AreEqual(capturedTime, results.LastResult.ElapsedSeconds, 0.0001f);
        }

        // ------------------------------------------------------------------
        // 3/4. Checkpoint counts
        // ------------------------------------------------------------------

        [Test]
        public void Result_CapturesCompletedAndTotalCheckpoints()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(5);
            var results = new CourseResultsController(course);

            FinishRace(course, checkpoints, 5);

            Assert.AreEqual(5, results.LastResult.CompletedCheckpoints);
            Assert.AreEqual(5, results.LastResult.TotalCheckpoints);
        }

        // ------------------------------------------------------------------
        // 5/6/7/8/9/10. Recovery count
        // ------------------------------------------------------------------

        [Test]
        public void Result_CapturesRecoveryCount()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var stateSource = new MutablePositionStateSource();
            var config = new DroneRecoveryConfig { CooldownDurationSeconds = 1f };
            var recovery = new DroneRecoveryController(_spawnTarget, stateSource, course, config, _clock);
            var results = new CourseResultsController(course, recovery);

            course.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            course.Tick(); // -> Racing, recovery count reset to 0

            stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick(); // one automatic recovery -> Cooldown (checkpoint processing suppressed)

            stateSource.Position = new Vector3(1f, 2f, 3f); // back to a valid position
            _clock.NowSeconds += config.CooldownDurationSeconds;
            recovery.Tick(); // cooldown elapses -> Monitoring, checkpoint processing resumes

            checkpoints.ReportCheckpointPassed(0); // finish

            Assert.AreEqual(1, results.LastResult.RecoveryCount);
        }

        [Test]
        public void RecoveryCount_StartsAtZeroForNewRace()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildBoundCourse(1);
            var stateSource = new MutablePositionStateSource();
            var recovery = new DroneRecoveryController(_spawnTarget, stateSource, course, new DroneRecoveryConfig(), _clock);

            Assert.AreEqual(0, recovery.RecoveryCountThisRun);
        }

        [Test]
        public void ManualReset_DoesNotIncrementRecoveryCount()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildBoundCourse(1);
            var stateSource = new MutablePositionStateSource();
            var recovery = new DroneRecoveryController(_spawnTarget, stateSource, course, new DroneRecoveryConfig(), _clock);

            course.Reset(); // manual reset — goes through IDroneSpawnTarget directly, never BeginRecovery

            Assert.AreEqual(0, recovery.RecoveryCountThisRun);
        }

        [Test]
        public void InitialSpawnPlacement_DoesNotIncrementRecoveryCount()
        {
            // "Initial spawn" in the real pipeline is WorldGenerationRuntimeService calling
            // IDroneSpawnTarget.PlaceAt directly on Ready — a code path that never touches
            // DroneRecoveryController at all. Modelled here simply as: constructing/binding
            // recovery never itself increments the counter.
            (CourseGameplayController course, CheckpointManager _) = BuildBoundCourse(1);
            var stateSource = new MutablePositionStateSource { Position = new Vector3(1f, 2f, 3f) };
            var recovery = new DroneRecoveryController(_spawnTarget, stateSource, course, new DroneRecoveryConfig(), _clock);
            recovery.Bind(null, Vector3.zero, Quaternion.identity); // no bounds — Tick() is a no-op regardless

            Assert.AreEqual(0, recovery.RecoveryCountThisRun);
        }

        [Test]
        public void NewRace_ResetsRecoveryCount()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildBoundCourse(2);
            var stateSource = new MutablePositionStateSource();
            var recovery = new DroneRecoveryController(_spawnTarget, stateSource, course, new DroneRecoveryConfig(), _clock);

            course.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            course.Tick();
            stateSource.Position = new Vector3(float.NaN, 0f, 0f);
            recovery.Tick(); // 1 recovery this run
            Assert.AreEqual(1, recovery.RecoveryCountThisRun);

            course.Reset(); // -> Waiting
            stateSource.Position = new Vector3(1f, 2f, 3f); // valid again

            course.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            course.Tick(); // -> Racing again, RaceStarted resets the counter

            Assert.AreEqual(0, recovery.RecoveryCountThisRun);
        }

        // ------------------------------------------------------------------
        // 11/12. Exactly one result per finished race; duplicate finish protection
        // ------------------------------------------------------------------

        [Test]
        public void RaceFinished_FiresResultsReadyExactlyOnce()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);
            int readyCount = 0;
            results.ResultsReady += _ => readyCount++;

            FinishRace(course, checkpoints, 1);

            Assert.AreEqual(1, readyCount);
        }

        [Test]
        public void DuplicateFinishReport_DoesNotProduceASecondResult()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);
            int readyCount = 0;
            results.ResultsReady += _ => readyCount++;

            FinishRace(course, checkpoints, 1);
            checkpoints.ReportCheckpointPassed(0); // already finished — CheckpointManager's own guard makes this a no-op

            Assert.AreEqual(1, readyCount);
        }

        // ------------------------------------------------------------------
        // Result clearing lifecycle
        // ------------------------------------------------------------------

        [Test]
        public void LastResult_NullBeforeAnyFinish()
        {
            (CourseGameplayController course, CheckpointManager _) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            Assert.IsNull(results.LastResult);
        }

        [Test]
        public void Restart_ClearsLastResult()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            FinishRace(course, checkpoints, 1);
            Assert.IsNotNull(results.LastResult);

            course.Reset();

            Assert.IsNull(results.LastResult);
        }

        [Test]
        public void Unbind_ClearsLastResult()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            FinishRace(course, checkpoints, 1);
            Assert.IsNotNull(results.LastResult);

            course.Unbind(); // models Clear World / a fresh regeneration invalidating the old course

            Assert.IsNull(results.LastResult);
        }

        [Test]
        public void Rebind_ClearsPreviousResult()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            FinishRace(course, checkpoints, 1);
            Assert.IsNotNull(results.LastResult);

            course.BindToCourse(BuildCheckpoints(3), new Vector3(9f, 9f, 9f), Quaternion.identity); // models a new generation

            Assert.IsNull(results.LastResult);
        }

        [Test]
        public void SecondFinish_ProducesAFreshResult()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            FinishRace(course, checkpoints, 1);
            CourseResult firstResult = results.LastResult;

            course.Reset();
            _clock.NowSeconds += 5f;
            FinishRace(course, checkpoints, 1);

            Assert.IsNotNull(results.LastResult);
            Assert.AreNotSame(firstResult, results.LastResult);
        }

        // ------------------------------------------------------------------
        // World seed
        // ------------------------------------------------------------------

        [Test]
        public void SetWorldSeed_IsCarriedIntoTheNextResult()
        {
            (CourseGameplayController course, CheckpointManager checkpoints) = BuildBoundCourse(1);
            var results = new CourseResultsController(course);

            results.SetWorldSeed(42);
            FinishRace(course, checkpoints, 1);

            Assert.AreEqual(42, results.LastResult.WorldSeed);
        }
    }
}
