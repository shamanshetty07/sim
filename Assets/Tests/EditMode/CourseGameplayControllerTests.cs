using System.Collections.Generic;
using NUnit.Framework;
using Sim.Gameplay;
using Sim.Simulation;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Unit tests for CourseGameplayController in isolation — a fake IGameplayClock (jumps to
    /// any value instantly, never sleeps for real seconds) and a fake IDroneSpawnTarget (same
    /// pattern WorldGenerationRuntimeServiceTests already uses for the same reason: a real
    /// DroneController never gets its Rigidbody/config wired outside Play mode). Real
    /// CheckpointManager instances are used throughout (built over a hand-built hierarchy of
    /// real CheckpointTrigger components) rather than a mock, since CheckpointManager itself is
    /// plain, dependency-free C# — no reason to fake it.
    /// </summary>
    public class CourseGameplayControllerTests
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

        private FakeGameplayClock _clock;
        private FakeDroneSpawnTarget _spawnTarget;
        private CourseGameplayController _controller;
        private readonly List<GameObject> _obstacleRoots = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeGameplayClock();
            _spawnTarget = new FakeDroneSpawnTarget();
            _controller = new CourseGameplayController(_spawnTarget, _clock);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _obstacleRoots)
                if (root != null) Object.DestroyImmediate(root);
            _obstacleRoots.Clear();
        }

        /// <summary>Builds a real obstacle root with `count` real CheckpointTrigger components, indices 0..count-1, added in *reverse* order and named so alphabetical/creation/sibling order would disagree with checkpoint order if anything here relied on it — nothing should.</summary>
        private GameObject BuildCheckpointRoot(int count)
        {
            var root = new GameObject("Obstacles");
            _obstacleRoots.Add(root);
            for (int i = count - 1; i >= 0; i--)
            {
                var go = new GameObject($"zzz_last_gate_{i}");
                go.transform.SetParent(root.transform);
                go.AddComponent<CheckpointTrigger>().Configure(i);
            }
            return root;
        }

        private CheckpointManager BindCourse(int checkpointCount)
        {
            GameObject root = BuildCheckpointRoot(checkpointCount);
            var manager = new CheckpointManager(root);
            _controller.BindToCourse(manager, new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
            return manager;
        }

        // ------------------------------------------------------------------
        // 1. Initial state
        // ------------------------------------------------------------------

        [Test]
        public void InitialState_IsWaiting()
        {
            Assert.AreEqual(CourseState.Waiting, _controller.State);
        }

        // ------------------------------------------------------------------
        // 2. Cannot start with no checkpoints
        // ------------------------------------------------------------------

        [Test]
        public void StartRace_NothingBound_DoesNotTransition()
        {
            _controller.StartRace();
            Assert.AreEqual(CourseState.Waiting, _controller.State);
        }

        [Test]
        public void BindToCourse_ZeroCheckpoints_TransitionsToFailed()
        {
            var emptyRoot = new GameObject("EmptyObstacles"); // no CheckpointTrigger children
            _obstacleRoots.Add(emptyRoot);
            var manager = new CheckpointManager(emptyRoot);

            _controller.BindToCourse(manager, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(CourseState.Failed, _controller.State);
            Assert.IsNotEmpty(_controller.LastFailureReason);
        }

        [Test]
        public void BindToCourse_NullCheckpointManager_TransitionsToFailed_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.BindToCourse(null, Vector3.zero, Quaternion.identity));
            Assert.AreEqual(CourseState.Failed, _controller.State);
        }

        [Test]
        public void Failed_StartRace_DoesNotTransition()
        {
            _controller.BindToCourse(null, Vector3.zero, Quaternion.identity);
            _controller.StartRace();
            Assert.AreEqual(CourseState.Failed, _controller.State);
        }

        // ------------------------------------------------------------------
        // 3. Start transitions Waiting -> Countdown -> Racing
        // ------------------------------------------------------------------

        [Test]
        public void StartRace_ValidCourse_TransitionsToCountdown()
        {
            BindCourse(3);
            _controller.StartRace();
            Assert.AreEqual(CourseState.Countdown, _controller.State);
        }

        [Test]
        public void Tick_BeforeCountdownElapses_StaysInCountdown()
        {
            BindCourse(3);
            _controller.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds - 0.1f;
            _controller.Tick();
            Assert.AreEqual(CourseState.Countdown, _controller.State);
        }

        [Test]
        public void Tick_AfterCountdownElapses_TransitionsToRacing()
        {
            BindCourse(3);
            _controller.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            _controller.Tick();
            Assert.AreEqual(CourseState.Racing, _controller.State);
        }

        // ------------------------------------------------------------------
        // 4/5. Timer starts when Racing begins, stops when Finished
        // ------------------------------------------------------------------

        [Test]
        public void Racing_ElapsedSecondsIncreasesOverTime()
        {
            BindCourse(1);
            _controller.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            _controller.Tick(); // -> Racing, timer starts at _clock.NowSeconds

            _clock.NowSeconds += 5f;
            Assert.AreEqual(5f, _controller.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Finished_ElapsedSecondsStopsIncreasing()
        {
            var manager = BindCourse(1);
            _controller.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            _controller.Tick();

            _clock.NowSeconds += 5f;
            manager.ReportCheckpointPassed(0); // final (only) checkpoint -> Finished

            float finishedElapsed = _controller.ElapsedSeconds;
            Assert.AreEqual(5f, finishedElapsed, 0.0001f);

            _clock.NowSeconds += 10f; // time keeps moving; the race timer must not
            Assert.AreEqual(finishedElapsed, _controller.ElapsedSeconds, 0.0001f);
        }

        // ------------------------------------------------------------------
        // 7/8. Checkpoint order enforcement
        // ------------------------------------------------------------------

        [Test]
        public void CorrectCheckpoint_AdvancesCurrentIndex()
        {
            var manager = BindCourse(3);
            manager.ReportCheckpointPassed(0);
            Assert.AreEqual(1, _controller.CurrentCheckpointIndex);
        }

        [Test]
        public void WrongCheckpoint_OutOfOrder_DoesNotAdvance()
        {
            var manager = BindCourse(3);
            manager.ReportCheckpointPassed(2); // gate 3 attempted while gate 1 (index 0) is current
            Assert.AreEqual(0, _controller.CurrentCheckpointIndex);
        }

        [Test]
        public void WrongCheckpoint_RaisesWrongCheckpointAttempted_WithRequiredIndex()
        {
            var manager = BindCourse(3);
            int attempted = -1, required = -1, callCount = 0;
            _controller.WrongCheckpointAttempted += (a, r) => { attempted = a; required = r; callCount++; };

            manager.ReportCheckpointPassed(2);

            Assert.AreEqual(1, callCount);
            Assert.AreEqual(2, attempted);
            Assert.AreEqual(0, required);
        }

        // ------------------------------------------------------------------
        // 9/10. Final checkpoint finishes the race and stops the timer
        // ------------------------------------------------------------------

        [Test]
        public void FinalCheckpoint_InOrder_TransitionsToFinished()
        {
            var manager = BindCourse(2);
            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);
            Assert.AreEqual(CourseState.Finished, _controller.State);
        }

        [Test]
        public void NonFinalCheckpoint_DoesNotFinish()
        {
            var manager = BindCourse(2);
            manager.ReportCheckpointPassed(0);
            Assert.AreNotEqual(CourseState.Finished, _controller.State);
        }

        [Test]
        public void Tick_TransitionToRacing_DiscardsAnyCheckpointPassedDuringCountdown()
        {
            var manager = BindCourse(2);
            _controller.StartRace(); // -> Countdown

            // A drone drifting through gate 1's trigger before GO must not count — reaching
            // Racing has to guarantee checkpoint 0 is still required.
            manager.ReportCheckpointPassed(0);
            Assert.AreEqual(1, _controller.CurrentCheckpointIndex, "sanity: the manager itself has no concept of countdown and does advance immediately");

            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            _controller.Tick(); // -> Racing

            Assert.AreEqual(0, _controller.CurrentCheckpointIndex, "Racing must start with checkpoint 0 still required, regardless of a stray pre-GO trigger.");
        }

        // ------------------------------------------------------------------
        // 11/12/18. Reset/Restart: checkpoint index, timer, drone all reset
        // ------------------------------------------------------------------

        [Test]
        public void Reset_CheckspointIndexReturnsToZero()
        {
            var manager = BindCourse(3);
            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);

            _controller.Reset();

            Assert.AreEqual(0, _controller.CurrentCheckpointIndex);
        }

        [Test]
        public void Reset_TimerReturnsToZero()
        {
            BindCourse(1);
            _controller.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            _controller.Tick();
            _clock.NowSeconds += 12f;

            _controller.Reset();

            Assert.AreEqual(0f, _controller.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Reset_ReturnsStateToWaiting()
        {
            var manager = BindCourse(1);
            manager.ReportCheckpointPassed(0); // -> Finished

            _controller.Reset();

            Assert.AreEqual(CourseState.Waiting, _controller.State);
        }

        [Test]
        public void Reset_InvokesDroneSpawnTargetAtBoundStartSpawn()
        {
            BindCourse(1);
            _controller.Reset();

            Assert.AreEqual(1, _spawnTarget.PlaceCount);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), _spawnTarget.LastPosition);
        }

        [Test]
        public void Reset_NothingBound_DoesNotThrow_DoesNotInvokeDroneSpawnTarget()
        {
            Assert.DoesNotThrow(() => _controller.Reset());
            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        // ------------------------------------------------------------------
        // 13/14/15/16. Unbind (Clear World) and rebind (regeneration)
        // ------------------------------------------------------------------

        [Test]
        public void Unbind_ReturnsToWaitingWithZeroCheckpoints()
        {
            BindCourse(5);
            _controller.Unbind();

            Assert.AreEqual(CourseState.Waiting, _controller.State);
            Assert.AreEqual(0, _controller.TotalCheckpoints);
        }

        [Test]
        public void BindToCourse_Rebind_UpdatesTotalCheckpoints()
        {
            BindCourse(5);
            Assert.AreEqual(5, _controller.TotalCheckpoints);

            BindCourse(9);
            Assert.AreEqual(9, _controller.TotalCheckpoints);
        }

        [Test]
        public void BindToCourse_Rebind_OldCheckpointManagerNoLongerAffectsController()
        {
            CheckpointManager oldManager = BindCourse(3);
            BindCourse(4); // regenerate — a new CheckpointManager takes over

            oldManager.ReportCheckpointPassed(0); // stale manager from the "destroyed" world

            Assert.AreEqual(0, _controller.CurrentCheckpointIndex, "The new course's progression must not be affected by an old, unbound CheckpointManager still firing events.");
        }

        [Test]
        public void BindToCourse_AfterFailure_ValidCourse_RecoversToWaiting()
        {
            _controller.BindToCourse(null, Vector3.zero, Quaternion.identity);
            Assert.AreEqual(CourseState.Failed, _controller.State);

            BindCourse(2);
            Assert.AreEqual(CourseState.Waiting, _controller.State);
        }

        // ------------------------------------------------------------------
        // 20. Events fire exactly once
        // ------------------------------------------------------------------

        [Test]
        public void RaceStarted_FiresExactlyOnce()
        {
            BindCourse(1);
            int count = 0;
            _controller.RaceStarted += () => count++;

            _controller.StartRace();
            _clock.NowSeconds += CourseGameplayController.CountdownDurationSeconds;
            _controller.Tick();
            _controller.Tick(); // further ticks in Racing must not re-fire

            Assert.AreEqual(1, count);
        }

        [Test]
        public void CheckpointPassed_FiresExactlyOnceForCorrectCheckpoint()
        {
            var manager = BindCourse(3);
            int count = 0;
            _controller.CheckpointPassed += _ => count++;

            manager.ReportCheckpointPassed(2); // wrong order — must not count
            manager.ReportCheckpointPassed(0); // correct — counts once

            Assert.AreEqual(1, count);
        }

        [Test]
        public void RaceFinished_FiresExactlyOnce()
        {
            var manager = BindCourse(1);
            int count = 0;
            _controller.RaceFinished += () => count++;

            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(0); // already finished — must not re-fire

            Assert.AreEqual(1, count);
        }

        [Test]
        public void CourseReset_FiresExactlyOnce()
        {
            BindCourse(1);
            int count = 0;
            _controller.CourseReset += () => count++;

            _controller.Reset();

            Assert.AreEqual(1, count);
        }

        [Test]
        public void CourseFailed_FiresExactlyOnceForInvalidBind()
        {
            int count = 0;
            _controller.CourseFailed += _ => count++;

            _controller.BindToCourse(null, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(1, count);
        }

        // ------------------------------------------------------------------
        // 21/22. Order comes from CheckpointDefinition/CheckpointTrigger index,
        // never from GameObject name or hierarchy/sibling order.
        // ------------------------------------------------------------------

        [Test]
        public void CheckpointOrder_IgnoresGameObjectNameAndHierarchyOrder()
        {
            // BuildCheckpointRoot deliberately adds triggers in reverse index order and names
            // them so that neither creation order, sibling index, nor alphabetical name order
            // matches the real checkpoint order — only CheckpointTrigger.Configure(index) does.
            var manager = BindCourse(4);

            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);
            manager.ReportCheckpointPassed(2);
            manager.ReportCheckpointPassed(3);

            Assert.AreEqual(CourseState.Finished, _controller.State);
        }
    }
}
