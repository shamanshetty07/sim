using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Extended Phase 9: WorldGenerationController now drives the pipeline all the way through
    /// WorldGenerator, so these tests construct real GameObjects (Terrain, obstacles, etc.) —
    /// real EditMode tests, not placeholders, per the same reasoning as WorldGeneratorTests
    /// (Phase 8). TearDown clears the shared WorldGenerator so no test leaks a GeneratedWorld
    /// into the next one.
    /// </summary>
    public class WorldGenerationControllerTests
    {
        private MockWorldDesigner _mockDesigner;
        private WorldGenerator _worldGenerator;
        private WorldGenerationController _controller;

        [SetUp]
        public void SetUp()
        {
            _mockDesigner = new MockWorldDesigner();
            _worldGenerator = new WorldGenerator();
            _controller = new WorldGenerationController(_mockDesigner, new WorldSpecificationValidator(), _worldGenerator);
        }

        [TearDown]
        public void TearDown() => _worldGenerator.Clear();

        [Test]
        public void Constructor_NullDesigner_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new WorldGenerationController(null, new WorldSpecificationValidator(), new WorldGenerator()));
        }

        [Test]
        public void Constructor_NullValidator_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new WorldGenerationController(new MockWorldDesigner(), null, new WorldGenerator()));
        }

        [Test]
        public void Constructor_NullWorldGenerator_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new WorldGenerationController(new MockWorldDesigner(), new WorldSpecificationValidator(), null));
        }

        [Test]
        public void InitialState_IsIdle()
        {
            Assert.AreEqual(WorldGenerationState.Idle, _controller.State);
        }

        [Test]
        public async Task GenerateWorldAsync_Success_EndsInReady_WithSpecificationAndGeneratedWorld()
        {
            await _controller.GenerateWorldAsync("Create a mountain FPV course with cliffs and rocks.");

            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
            Assert.IsNotNull(_controller.LastValidSpecification);
            Assert.IsNotNull(_controller.LastGeneratedWorld);
            Assert.IsTrue(_controller.LastGeneratedWorld.Success);
            Assert.IsNotNull(_controller.LastGeneratedWorld.Root);
        }

        [Test]
        public async Task GenerateWorldAsync_PreservesOriginalPromptThroughFullPipeline()
        {
            const string prompt = "Create a desert canyon FPV racing course with tunnels, large rocks and 12 gates.";
            await _controller.GenerateWorldAsync(prompt);

            Assert.AreEqual(prompt, _controller.LastValidSpecification.OriginalPrompt);
        }

        [Test]
        public async Task GenerateWorldAsync_EmptyPrompt_EndsInFailed_DoesNotThrow()
        {
            await _controller.GenerateWorldAsync("");

            Assert.AreEqual(WorldGenerationState.Failed, _controller.State);
            Assert.IsNotNull(_controller.LastErrorMessage);
        }

        [Test]
        public async Task GenerateWorldAsync_StateTransitions_FireInOrder()
        {
            var seen = new List<WorldGenerationState>();
            _controller.StateChanged += s => seen.Add(s);

            await _controller.GenerateWorldAsync("Create a forest obstacle course.");

            CollectionAssert.AreEqual(
                new[] { WorldGenerationState.Designing, WorldGenerationState.Validating, WorldGenerationState.Generating, WorldGenerationState.Ready },
                seen);
        }

        [Test]
        public async Task GenerateWorldAsync_ResolvesAValidDroneSpawn()
        {
            await _controller.GenerateWorldAsync("Create a mountain course.");

            Vector3 spawn = _controller.LastGeneratedWorld.SpawnPosition;
            Assert.IsFalse(float.IsNaN(spawn.x) || float.IsNaN(spawn.y) || float.IsNaN(spawn.z));
        }

        [Test]
        public async Task GenerateWorldAsync_DesignerFailure_EndsInFailed_NoGeneratedWorld()
        {
            var failingDesigner = new AlwaysFailsDesigner();
            var controller = new WorldGenerationController(failingDesigner, new WorldSpecificationValidator(), _worldGenerator);

            await controller.GenerateWorldAsync("prompt");

            Assert.AreEqual(WorldGenerationState.Failed, controller.State);
            Assert.IsNull(controller.LastGeneratedWorld);
        }

        [Test]
        public async Task GenerateWorldAsync_InvalidSpecification_EndsInFailed_WithValidationFailedReason()
        {
            var invalidDesigner = new ReturnsNullPromptDesigner();
            var controller = new WorldGenerationController(invalidDesigner, new WorldSpecificationValidator(), _worldGenerator);

            await controller.GenerateWorldAsync("prompt");

            Assert.AreEqual(WorldGenerationState.Failed, controller.State);
            Assert.AreEqual(WorldDesignFailureReason.ValidationFailed, controller.LastFailureReason);
        }

        [Test]
        public async Task GenerateWorldAsync_WorldGeneratorFailure_EndsInFailed_NoStaleGeneratedWorld()
        {
            var unsafeSpawnDesigner = new UnsafeSpawnDesigner();
            var controller = new WorldGenerationController(unsafeSpawnDesigner, new WorldSpecificationValidator(), _worldGenerator);

            await controller.GenerateWorldAsync("prompt");

            Assert.AreEqual(WorldGenerationState.Failed, controller.State);
            Assert.IsNull(GameObject.Find("GeneratedWorld"), "A failed generation must not leave a stale GeneratedWorld in the scene.");
        }

        [Test]
        public void ClearGeneratedWorld_WithNothingGenerated_DoesNotThrow_ReturnsToIdle()
        {
            Assert.DoesNotThrow(() => _controller.ClearGeneratedWorld());
            Assert.AreEqual(WorldGenerationState.Idle, _controller.State);
        }

        [Test]
        public async Task ClearGeneratedWorld_AfterSuccess_RemovesTheGeneratedWorld_AndReturnsToIdle()
        {
            await _controller.GenerateWorldAsync("Create a mountain course.");
            GameObject root = _controller.LastGeneratedWorld.Root;

            _controller.ClearGeneratedWorld();

            Assert.IsTrue(root == null, "The generated world's root should have been destroyed.");
            Assert.AreEqual(WorldGenerationState.Idle, _controller.State);
            Assert.IsNull(_controller.LastGeneratedWorld);
            Assert.IsNull(_controller.LastValidSpecification);
        }

        [Test]
        public async Task GenerateWorldAsync_CalledTwice_DoesNotDuplicateGeneratedWorldRoots()
        {
            await _controller.GenerateWorldAsync("First prompt.");
            await _controller.GenerateWorldAsync("Second prompt.");

            int count = 0;
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
                if (go.name == "GeneratedWorld") count++;

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Cancel_WithNothingInFlight_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.Cancel());
        }

        [Test]
        public async Task GenerateWorldAsync_CancelledBeforeCompletion_EndsInCancelled()
        {
            _mockDesigner.SimulatedDelayMilliseconds = 2000;

            Task generation = _controller.GenerateWorldAsync("Create a mountain course.");
            _controller.Cancel();
            await generation;

            Assert.AreEqual(WorldGenerationState.Cancelled, _controller.State);
        }

        [Test]
        public async Task GenerateWorldAsync_CalledAgain_SupersedesPreviousInFlightAttempt()
        {
            _mockDesigner.SimulatedDelayMilliseconds = 500;
            Task first = _controller.GenerateWorldAsync("First prompt.");

            _mockDesigner.SimulatedDelayMilliseconds = 0;
            await _controller.GenerateWorldAsync("Second prompt.");

            await first; // let the now-superseded, now-cancelled first attempt finish quietly

            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
            Assert.AreEqual("Second prompt.", _controller.LastValidSpecification.OriginalPrompt);
        }

        // --------------------------------------------------------------------------------
        // Phase 14 — LoadWorld: the same Validating -> Generating -> Ready/Failed tail as
        // GenerateWorldAsync, given an already-known specification, skipping Designing entirely.
        // --------------------------------------------------------------------------------

        private static WorldSpecification ValidSpecification(int seed = 42) => new WorldSpecification
        {
            OriginalPrompt = "Create a small test course.",
            Seed = seed,
            Terrain = new TerrainSpecification { TerrainType = "hills", Width = 200f, Depth = 200f, MaxHeight = 40f },
            Spawn = new SpawnSpecification { Position = new Vector3(0f, 25f, 0f) }
        };

        [Test]
        public void LoadWorld_NullSpecification_EndsInFailed_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.LoadWorld(null));
            Assert.AreEqual(WorldGenerationState.Failed, _controller.State);
        }

        [Test]
        public void LoadWorld_ValidSpecification_EndsInReady_WithGeneratedWorld()
        {
            _controller.LoadWorld(ValidSpecification());

            Assert.AreEqual(WorldGenerationState.Ready, _controller.State);
            Assert.IsNotNull(_controller.LastGeneratedWorld);
            Assert.IsTrue(_controller.LastGeneratedWorld.Success);
        }

        [Test]
        public void LoadWorld_NeverReachesDesigningState_AndNeverCallsTheDesigner()
        {
            var spyDesigner = new SpyDesigner();
            var controller = new WorldGenerationController(spyDesigner, new WorldSpecificationValidator(), _worldGenerator);
            var seen = new List<WorldGenerationState>();
            controller.StateChanged += s => seen.Add(s);

            controller.LoadWorld(ValidSpecification());

            Assert.IsFalse(spyDesigner.WasCalled, "LoadWorld must never call IWorldDesigner — that is what structurally guarantees no LLM call happens on load.");
            CollectionAssert.DoesNotContain(seen, WorldGenerationState.Designing);
            CollectionAssert.AreEqual(new[] { WorldGenerationState.Validating, WorldGenerationState.Generating, WorldGenerationState.Ready }, seen);
        }

        [Test]
        public void LoadWorld_InvalidSpecification_EndsInFailed_WithValidationFailedReason()
        {
            var invalid = ValidSpecification();
            invalid.OriginalPrompt = ""; // the one genuinely unrecoverable WorldSpecificationValidator error

            _controller.LoadWorld(invalid);

            Assert.AreEqual(WorldGenerationState.Failed, _controller.State);
            Assert.AreEqual(WorldDesignFailureReason.ValidationFailed, _controller.LastFailureReason);
        }

        [Test]
        public void LoadWorld_UnresolvableSpawn_EndsInFailed_NoStaleGeneratedWorld()
        {
            var unsafeSpawn = ValidSpecification();
            unsafeSpawn.Spawn = new SpawnSpecification { Position = new Vector3(0f, -5000f, 0f), AlternateSpawnPoints = new List<Vector3>() };

            _controller.LoadWorld(unsafeSpawn);

            Assert.AreEqual(WorldGenerationState.Failed, _controller.State);
            Assert.IsNull(GameObject.Find("GeneratedWorld"));
        }

        [Test]
        public void LoadWorld_SameSpecificationAndSeed_ProducesDeterministicTerrain()
        {
            _controller.LoadWorld(ValidSpecification(seed: 7));
            var terrainA = _controller.LastGeneratedWorld.Root.transform.Find("Terrain").GetComponent<UnityEngine.Terrain>();
            float heightA = terrainA.SampleHeight(new Vector3(10f, 0f, 10f));

            _worldGenerator.Clear();
            _controller.LoadWorld(ValidSpecification(seed: 7)); // same specification content + same seed

            var terrainB = _controller.LastGeneratedWorld.Root.transform.Find("Terrain").GetComponent<UnityEngine.Terrain>();
            float heightB = terrainB.SampleHeight(new Vector3(10f, 0f, 10f));

            Assert.AreEqual(heightA, heightB, 0.0001f, "loading never generates a new seed — the same specification+seed must reproduce the same terrain.");
        }

        [Test]
        public void LoadWorld_DoesNotDuplicateGeneratedWorldRoots_WhenCalledTwice()
        {
            _controller.LoadWorld(ValidSpecification());
            _controller.LoadWorld(ValidSpecification());

            int count = 0;
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
                if (go.name == "GeneratedWorld") count++;

            Assert.AreEqual(1, count);
        }

        private sealed class SpyDesigner : IWorldDesigner
        {
            public bool WasCalled { get; private set; }

            public Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, System.Threading.CancellationToken cancellationToken = default)
            {
                WasCalled = true;
                return Task.FromResult(WorldDesignOutcome.Succeeded(new WorldSpecification { OriginalPrompt = request.Prompt }));
            }
        }

        // --- Fakes for the failure-path tests above ---

        private sealed class AlwaysFailsDesigner : IWorldDesigner
        {
            public Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, System.Threading.CancellationToken cancellationToken = default) =>
                Task.FromResult(WorldDesignOutcome.Failed(WorldDesignFailureReason.Unavailable, "simulated failure"));
        }

        /// <summary>Returns a specification whose OriginalPrompt validation will always reject (empty), regardless of the request — proves the controller reaches Failed via the validator, not the designer.</summary>
        private sealed class ReturnsNullPromptDesigner : IWorldDesigner
        {
            public Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, System.Threading.CancellationToken cancellationToken = default)
            {
                var spec = new WorldSpecification { OriginalPrompt = "" };
                return Task.FromResult(WorldDesignOutcome.Succeeded(spec));
            }
        }

        /// <summary>A specification whose spawn is unreachable (no alternates) — proves the controller reaches Failed via WorldGenerator, not the validator.</summary>
        private sealed class UnsafeSpawnDesigner : IWorldDesigner
        {
            public Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, System.Threading.CancellationToken cancellationToken = default)
            {
                var spec = new WorldSpecification
                {
                    OriginalPrompt = request.Prompt,
                    Seed = 1,
                    Spawn = new SpawnSpecification
                    {
                        Position = new Vector3(0f, -5000f, 0f),
                        AlternateSpawnPoints = new List<Vector3>()
                    }
                };
                return Task.FromResult(WorldDesignOutcome.Succeeded(spec));
            }
        }
    }
}
