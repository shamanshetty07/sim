using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.Gameplay;
using Sim.Simulation;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Persistence;
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

        private sealed class FakeDroneStateSource : IDroneStateSource
        {
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.identity;
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
            await serviceWithoutDrone.GenerateWorldAsync("Create a mountain course.");
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
        public async Task NullCourseGameplayController_ReachesReady_DoesNotThrow()
        {
            using var serviceWithoutCourse = new WorldGenerationRuntimeService(_controller, _spawnTarget, null);
            await serviceWithoutCourse.GenerateWorldAsync("Create a mountain course.");
            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
        }

        // --------------------------------------------------------------------------------
        // Phase 12 — drone recovery binding, over the same real Mock -> WorldGenerator
        // pipeline (real generated terrain/bounds, not a fake).
        // --------------------------------------------------------------------------------

        [Test]
        public async Task GenerateWorldAsync_Success_BindsRecoveryToGeneratedBounds()
        {
            var recovery = new DroneRecoveryController(_spawnTarget, new FakeDroneStateSource(), null);
            using var serviceWithRecovery = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, recovery);

            await serviceWithRecovery.GenerateWorldAsync("Create a mountain course.");

            Assert.IsTrue(recovery.IsBound);
        }

        [Test]
        public async Task Regenerate_RebindsRecovery_NotDuplicated()
        {
            var recovery = new DroneRecoveryController(_spawnTarget, new FakeDroneStateSource(), null);
            using var serviceWithRecovery = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, recovery);

            await serviceWithRecovery.GenerateWorldAsync("Create a mountain course.");
            Assert.IsTrue(recovery.IsBound);

            await serviceWithRecovery.GenerateWorldAsync("Create a different mountain course.");

            Assert.IsTrue(recovery.IsBound, "the same DroneRecoveryController instance must still be bound after regeneration, not left permanently unbound by the transient Designing/Validating/Generating unbind");
        }

        [Test]
        public async Task ClearWorld_UnbindsRecovery()
        {
            var recovery = new DroneRecoveryController(_spawnTarget, new FakeDroneStateSource(), null);
            using var serviceWithRecovery = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, recovery);

            await serviceWithRecovery.GenerateWorldAsync("Create a mountain course.");
            serviceWithRecovery.ClearWorld();

            Assert.IsFalse(recovery.IsBound);
        }

        [Test]
        public async Task NullDroneRecoveryController_ReachesReady_DoesNotThrow()
        {
            using var serviceWithoutRecovery = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, null);
            await serviceWithoutRecovery.GenerateWorldAsync("Create a mountain course.");
            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
        }

        // --------------------------------------------------------------------------------
        // Phase 13 — results snapshotting binding (the world seed), over the same real
        // Mock -> WorldGenerator pipeline.
        // --------------------------------------------------------------------------------

        [Test]
        public async Task GenerateWorldAsync_Success_CarriesGeneratedSeedIntoNextResult()
        {
            var course = new CourseGameplayController(_spawnTarget);
            var results = new CourseResultsController(course);
            using var serviceWithResults = new WorldGenerationRuntimeService(_controller, _spawnTarget, course, null, results);

            await serviceWithResults.GenerateWorldAsync("Create a mountain course.");

            int generatedSeed = _controller.LastValidSpecification.Seed;

            // Finish the real generated course (15 gates, MockWorldDesigner's standing example)
            // to actually produce a result and confirm the seed made it all the way through.
            // CheckpointManager.RaceFinished fires regardless of CourseGameplayController's own
            // Waiting/Countdown/Racing state, so reporting the passes directly is sufficient.
            for (int i = 0; i < 15; i++)
                _controller.LastGeneratedWorld.CheckpointManager.ReportCheckpointPassed(i);

            Assert.AreEqual(generatedSeed, results.LastResult.WorldSeed);
        }

        [Test]
        public async Task NullCourseResultsController_ReachesReady_DoesNotThrow()
        {
            using var serviceWithoutResults = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, null, null);
            await serviceWithoutResults.GenerateWorldAsync("Create a mountain course.");
            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
        }

        // --------------------------------------------------------------------------------
        // Phase 14 — save/load forwarding. A fake IWorldSaveService (in-memory, no real file
        // I/O) so these tests stay focused on "does the service forward correctly," not on
        // WorldSaveService's own file-handling (covered separately by WorldSaveServiceTests).
        // --------------------------------------------------------------------------------

        private sealed class FakeWorldSaveService : IWorldSaveService
        {
            public WorldSaveData Saved { get; private set; }
            public int SaveCount { get; private set; }
            public WorldLoadResult NextLoadResult { get; set; } = WorldLoadResult.Failed("No save file exists.");

            public WorldSaveOperationResult Save(WorldSaveData data, string slotName = null)
            {
                Saved = data;
                SaveCount++;
                return WorldSaveOperationResult.Succeeded();
            }

            public WorldLoadResult Load(string slotName = null) => NextLoadResult;

            public bool Delete(string slotName = null) => true;

            public bool Exists(string slotName = null) => Saved != null;
        }

        [Test]
        public void SaveWorld_NoSaveServiceConfigured_ReturnsMessage_DoesNotThrow()
        {
            string message = null;
            Assert.DoesNotThrow(() => message = _service.SaveWorld());
            Assert.IsNotNull(message);
        }

        [Test]
        public void SaveWorld_NoGeneratedWorldYet_ReturnsMessage_DoesNotCallSaveService()
        {
            var saveService = new FakeWorldSaveService();
            using var serviceWithSave = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, null, null, saveService);

            string message = serviceWithSave.SaveWorld();

            Assert.IsNotNull(message);
            Assert.AreEqual(0, saveService.SaveCount);
        }

        [Test]
        public async Task SaveWorld_AfterGeneration_ForwardsTheGeneratedSpecificationToTheSaveService()
        {
            var saveService = new FakeWorldSaveService();
            using var serviceWithSave = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, null, null, saveService);

            await serviceWithSave.GenerateWorldAsync("Create a mountain course.");
            string message = serviceWithSave.SaveWorld();

            Assert.AreEqual(1, saveService.SaveCount);
            Assert.AreSame(_controller.LastValidSpecification, saveService.Saved.Specification);
            Assert.IsNotNull(message);
        }

        [Test]
        public void LoadWorld_NoSaveServiceConfigured_ReturnsMessage_DoesNotThrow()
        {
            string message = null;
            Assert.DoesNotThrow(() => message = _service.LoadWorld());
            Assert.IsNotNull(message);
        }

        [Test]
        public void LoadWorld_SaveServiceLoadFails_ReturnsErrorMessage_ControllerStateUntouched()
        {
            var saveService = new FakeWorldSaveService { NextLoadResult = WorldLoadResult.Failed("No save file exists.") };
            using var serviceWithSave = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, null, null, saveService);

            string message = serviceWithSave.LoadWorld();

            Assert.IsNotNull(message);
            Assert.AreEqual(WorldGenerationState.Idle, _controller.State, "a failure before the controller is ever involved must not touch its state at all.");
        }

        [Test]
        public void LoadWorld_SaveServiceLoadSucceeds_ForwardsToControllerLoadWorld_ReachesReady()
        {
            var validSpecification = new WorldSpecification
            {
                OriginalPrompt = "Create a small test course.",
                Seed = 7,
                Terrain = new TerrainSpecification { TerrainType = "hills", Width = 200f, Depth = 200f, MaxHeight = 40f },
                Spawn = new SpawnSpecification { Position = new Vector3(0f, 25f, 0f) }
            };
            var saveService = new FakeWorldSaveService
            {
                NextLoadResult = WorldLoadResult.Succeeded(WorldSaveData.FromSpecification(validSpecification))
            };
            using var serviceWithSave = new WorldGenerationRuntimeService(_controller, _spawnTarget, null, null, null, saveService);

            string message = serviceWithSave.LoadWorld();

            Assert.IsNull(message, "success hands off to the controller — the existing StateChanged-driven status already reports the rest.");
            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
        }

        [Test]
        public void LoadWorld_Success_PlacesDroneAtLoadedSpawn_ViaTheSameHandlerAsGenerate()
        {
            var validSpecification = new WorldSpecification
            {
                OriginalPrompt = "Create a small test course.",
                Seed = 7,
                Terrain = new TerrainSpecification { TerrainType = "hills", Width = 200f, Depth = 200f, MaxHeight = 40f },
                Spawn = new SpawnSpecification { Position = new Vector3(0f, 25f, 0f) }
            };
            var saveService = new FakeWorldSaveService
            {
                NextLoadResult = WorldLoadResult.Succeeded(WorldSaveData.FromSpecification(validSpecification))
            };
            // A dedicated spawn target (not the shared _spawnTarget the base fixture's own
            // _service is also subscribed to) so this counts placements from this one service only.
            var dedicatedSpawnTarget = new FakeDroneSpawnTarget();
            using var serviceWithSave = new WorldGenerationRuntimeService(_controller, dedicatedSpawnTarget, null, null, null, saveService);

            serviceWithSave.LoadWorld();

            Assert.AreEqual(1, dedicatedSpawnTarget.PlaceCount);
            Assert.AreEqual(_controller.LastGeneratedWorld.SpawnPosition, dedicatedSpawnTarget.LastPosition);
        }
    }
}
