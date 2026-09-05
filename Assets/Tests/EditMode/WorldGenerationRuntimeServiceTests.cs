using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.Gameplay;
using Sim.Simulation;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Uses a fake IDroneSpawnTarget rather than a real DroneController — building a real,
    /// functioning drone rig outside Play mode is awkward (Awake() doesn't run for a
    /// component added via script in Edit mode, so DronePhysics never gets its Rigidbody
    /// reference — see DronePhysics's own Phase 3 remarks). IDroneSpawnTarget exists
    /// specifically so this class's actual logic (does Ready trigger placement? with the
    /// right position? not on failure? not after Dispose?) is testable without any of that.
    /// </summary>
    public class WorldGenerationRuntimeServiceTests
    {
        private sealed class FakeDroneSpawnTarget : IDroneSpawnTarget
        {
            public int PlaceCount { get; private set; }
            public Vector3 LastPosition { get; private set; }

            public void PlaceAt(Vector3 position, Quaternion rotation)
            {
                PlaceCount++;
                LastPosition = position;
            }
        }

        private WorldGenerator _worldGenerator;
        private WorldGenerationController _controller;
        private FakeDroneSpawnTarget _spawnTarget;
        private WorldGenerationRuntimeService _service;

        [SetUp]
        public void SetUp()
        {
            _worldGenerator = new WorldGenerator();
            _controller = new WorldGenerationController(new MockWorldDesigner(), new WorldSpecificationValidator(), _worldGenerator);
            _spawnTarget = new FakeDroneSpawnTarget();
            _service = new WorldGenerationRuntimeService(_controller, _spawnTarget);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
            _worldGenerator.Clear();
        }

        [Test]
        public void Controller_ExposesTheSameControllerInstance()
        {
            Assert.AreSame(_controller, _service.Controller);
        }

        [Test]
        public async Task GenerateWorldAsync_Success_PlacesDroneAtResolvedSpawn()
        {
            await _service.GenerateWorldAsync("Create a mountain course.");

            Assert.AreEqual(1, _spawnTarget.PlaceCount);
            Assert.AreEqual(_controller.LastGeneratedWorld.SpawnPosition, _spawnTarget.LastPosition);
        }

        [Test]
        public async Task GenerateWorldAsync_Failure_DoesNotPlaceDrone()
        {
            await _service.GenerateWorldAsync(""); // empty prompt -> Failed before ever reaching Ready

            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        [Test]
        public void Cancel_WithNothingInFlight_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.Cancel());
        }

        [Test]
        public async Task ClearWorld_ReturnsControllerToIdle()
        {
            await _service.GenerateWorldAsync("Create a mountain course.");
            _service.ClearWorld();

            Assert.AreEqual(WorldGenerationState.Idle, _controller.State);
        }

        [Test]
        public async Task NullSpawnTarget_ReachesReady_DoesNotThrow()
        {
            using var serviceWithoutDrone = new WorldGenerationRuntimeService(_controller, null);
            Assert.DoesNotThrowAsync(async () => await serviceWithoutDrone.GenerateWorldAsync("Create a mountain course."));
            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
        }

        [Test]
        public void Dispose_UnsubscribesFromController_NoLongerPlacesDroneAfterward()
        {
            _service.Dispose();

            // After Dispose, driving the controller directly (bypassing the disposed service)
            // must not still trigger drone placement — proves the subscription was really removed.
            Task task = _controller.GenerateWorldAsync("Create a mountain course.");
            task.GetAwaiter().GetResult();

            Assert.AreEqual(0, _spawnTarget.PlaceCount);
        }

        // --------------------------------------------------------------------------------
        // Phase 11 — course gameplay binding. MockWorldDesigner's standing example always
        // includes 15 gates (Course.GateCount = 15, all with CheckpointIndex set), so a
        // successful GenerateWorldAsync call always produces a real, generated CheckpointManager
        // to bind to here — no separate fake needed for these tests.
        // --------------------------------------------------------------------------------

        [Test]
        public async Task GenerateWorldAsync_Success_BindsCourseToGeneratedCheckpoints()
        {
            var course = new CourseGameplayController(_spawnTarget);
            using var serviceWithCourse = new WorldGenerationRuntimeService(_controller, _spawnTarget, course);

            await serviceWithCourse.GenerateWorldAsync("Create a mountain course.");

            Assert.AreEqual(CourseState.Waiting, course.State);
            Assert.AreEqual(15, course.TotalCheckpoints);
        }

        [Test]
        public async Task Regenerate_RebindsCourse_NotDuplicated_OldCheckpointManagerNoLongerAffectsCourse()
        {
            var course = new CourseGameplayController(_spawnTarget);
            using var serviceWithCourse = new WorldGenerationRuntimeService(_controller, _spawnTarget, course);

            await serviceWithCourse.GenerateWorldAsync("Create a mountain course.");
            CheckpointManager firstWorldCheckpoints = _controller.LastGeneratedWorld.CheckpointManager;

            await serviceWithCourse.GenerateWorldAsync("Create a different mountain course."); // regenerate — same course instance, rebound

            Assert.AreEqual(15, course.TotalCheckpoints, "the same CourseGameplayController instance must still report the newly generated course, not stay stuck on the old one or throw away the binding");

            // The old world's CheckpointManager is a stale plain-C# object at this point (its
            // GameObjects were destroyed by WorldGenerator.Generate()'s own Clear()) — proving
            // the course no longer reacts to it is what proves the old subscription was dropped.
            int currentIndexBefore = course.CurrentCheckpointIndex;
            firstWorldCheckpoints.ReportCheckpointPassed(0);
            Assert.AreEqual(currentIndexBefore, course.CurrentCheckpointIndex);
        }

        [Test]
        public async Task ClearWorld_UnbindsCourse_ReturnsToWaitingWithZeroCheckpoints()
        {
            var course = new CourseGameplayController(_spawnTarget);
            using var serviceWithCourse = new WorldGenerationRuntimeService(_controller, _spawnTarget, course);

            await serviceWithCourse.GenerateWorldAsync("Create a mountain course.");
            serviceWithCourse.ClearWorld();

            Assert.AreEqual(CourseState.Waiting, course.State);
            Assert.AreEqual(0, course.TotalCheckpoints);
        }

        [Test]
        public void NullCourseGameplayController_ReachesReady_DoesNotThrow()
        {
            using var serviceWithoutCourse = new WorldGenerationRuntimeService(_controller, _spawnTarget, null);
            Assert.DoesNotThrowAsync(async () => await serviceWithoutCourse.GenerateWorldAsync("Create a mountain course."));
            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
        }
    }
}
