using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Gameplay;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Real EditMode tests against WorldGenerator — Unity's Editor process has a live
    /// GameObject/Transform/Physics/Terrain system available outside Play mode, so these are
    /// genuine, runnable tests, not placeholders. What they cannot cover: anything requiring
    /// the Player loop actually ticking over time (FixedUpdate-driven physics response, the
    /// drone actually colliding with generated geometry while flying) — that needs a live
    /// Editor's Play mode, which is unavailable in the environment this was written in. See
    /// docs/WORLD_GENERATION.md "Testing" for exactly what's covered here vs. what still needs
    /// manual Play-mode verification.
    /// </summary>
    public class WorldGeneratorTests
    {
        private WorldGenerator _generator;

        [SetUp]
        public void SetUp() => _generator = new WorldGenerator();

        [TearDown]
        public void TearDown() => _generator.Clear();

        private static WorldSpecification MinimalValidSpecification()
        {
            return new WorldSpecification
            {
                OriginalPrompt = "Create a small test course.",
                WorldName = "Test World",
                Seed = 42,
                Terrain = new TerrainSpecification { TerrainType = "hills", Width = 200f, Depth = 200f, MaxHeight = 40f, HeightVariation01 = 0.3f },
                EnvironmentObjects = new List<ObjectSpecification>(),
                Obstacles = new List<ObstacleSpecification>(),
                Course = new CourseSpecification(),
                Spawn = new SpawnSpecification { Position = new Vector3(0f, 25f, 0f) }
            };
        }

        [Test]
        public void Generate_ValidSpecification_Succeeds()
        {
            GeneratedWorldResult result = _generator.Generate(MinimalValidSpecification());

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.Root);
        }

        [Test]
        public void Generate_NullSpecification_FailsCleanly_DoesNotThrow()
        {
            GeneratedWorldResult result = null;
            Assert.DoesNotThrow(() => result = _generator.Generate(null));

            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Generate_CreatesExpectedTopLevelHierarchy()
        {
            GeneratedWorldResult result = _generator.Generate(MinimalValidSpecification());

            Assert.IsNotNull(result.Root.transform.Find("Terrain"));
            Assert.IsNotNull(result.Root.transform.Find("Environment"));
            Assert.IsNotNull(result.Root.transform.Find("Obstacles"));
            Assert.IsNotNull(result.Root.transform.Find("Spawn"));
        }

        [Test]
        public void Generate_ObstacleSubgroups_AllPresent()
        {
            GeneratedWorldResult result = _generator.Generate(MinimalValidSpecification());
            Transform obstacles = result.Root.transform.Find("Obstacles");

            Assert.IsNotNull(obstacles.Find("Gates"));
            Assert.IsNotNull(obstacles.Find("Rings"));
            Assert.IsNotNull(obstacles.Find("Tunnels"));
            Assert.IsNotNull(obstacles.Find("Checkpoints"));
            Assert.IsNotNull(obstacles.Find("Other"));
        }

        [Test]
        public void Generate_EnvironmentSubgroups_AllPresent()
        {
            GeneratedWorldResult result = _generator.Generate(MinimalValidSpecification());
            Transform environment = result.Root.transform.Find("Environment");

            Assert.IsNotNull(environment.Find("Trees"));
            Assert.IsNotNull(environment.Find("Rocks"));
            Assert.IsNotNull(environment.Find("Buildings"));
            Assert.IsNotNull(environment.Find("Vegetation"));
            Assert.IsNotNull(environment.Find("Structures"));
        }

        [Test]
        public void Generate_TerrainDimensions_MatchSpecification()
        {
            WorldSpecification spec = MinimalValidSpecification();
            spec.Terrain.Width = 300f;
            spec.Terrain.Depth = 250f;
            spec.Terrain.MaxHeight = 60f;

            GeneratedWorldResult result = _generator.Generate(spec);
            var terrain = result.Root.transform.Find("Terrain").GetComponent<UnityEngine.Terrain>();

            Assert.AreEqual(300f, terrain.terrainData.size.x, 0.01f);
            Assert.AreEqual(250f, terrain.terrainData.size.z, 0.01f);
            Assert.AreEqual(60f, terrain.terrainData.size.y, 0.01f);
        }

        [Test]
        public void Generate_TerrainCollider_IsPresent()
        {
            GeneratedWorldResult result = _generator.Generate(MinimalValidSpecification());
            Transform terrain = result.Root.transform.Find("Terrain");

            Assert.IsNotNull(terrain.GetComponent<Collider>(), "Generated terrain must have real collision for the drone to fly over.");
        }

        [Test]
        public void Generate_EnvironmentObjectCount_RespectsRequestedCount()
        {
            WorldSpecification spec = MinimalValidSpecification();
            spec.EnvironmentObjects.Add(new ObjectSpecification { Category = "tree", Count = 5 });

            GeneratedWorldResult result = _generator.Generate(spec);
            Transform trees = result.Root.transform.Find("Environment/Trees");

            Assert.AreEqual(5, trees.childCount);
        }

        [Test]
        public void Generate_ExplicitObstaclePosition_IsPreserved()
        {
            WorldSpecification spec = MinimalValidSpecification();
            var explicitPosition = new Vector3(15f, 30f, 40f);
            spec.Obstacles.Add(new ObstacleSpecification { Id = "gate_00", Type = "gate", Position = explicitPosition, Scale = Vector3.one });

            GeneratedWorldResult result = _generator.Generate(spec);
            Transform gates = result.Root.transform.Find("Obstacles/Gates");

            Assert.AreEqual(1, gates.childCount);
            Assert.AreEqual(explicitPosition, gates.GetChild(0).position);
        }

        [Test]
        public void Generate_ObstacleCount_RespectsSpecification()
        {
            WorldSpecification spec = MinimalValidSpecification();
            for (int i = 0; i < 6; i++)
                spec.Obstacles.Add(new ObstacleSpecification { Id = $"gate_{i}", Type = "gate", Position = new Vector3(i * 10f, 25f, i * 10f), Scale = Vector3.one });

            GeneratedWorldResult result = _generator.Generate(spec);
            Transform gates = result.Root.transform.Find("Obstacles/Gates");

            Assert.AreEqual(6, gates.childCount);
        }

        [Test]
        public void Generate_CheckpointOrdering_IsSequentialWithNoGaps()
        {
            WorldSpecification spec = MinimalValidSpecification();
            spec.Obstacles.Add(new ObstacleSpecification { Id = "a", Type = "gate", Position = new Vector3(0f, 25f, 0f), Scale = Vector3.one, CheckpointIndex = 0 });
            spec.Obstacles.Add(new ObstacleSpecification { Id = "b", Type = "gate", Position = new Vector3(20f, 25f, 20f), Scale = Vector3.one, CheckpointIndex = 1 });
            spec.Obstacles.Add(new ObstacleSpecification { Id = "c", Type = "gate", Position = new Vector3(40f, 25f, 40f), Scale = Vector3.one, CheckpointIndex = 2 });

            GeneratedWorldResult result = _generator.Generate(spec);
            var indices = result.Root.GetComponentsInChildren<CheckpointTrigger>().Select(t => t.CheckpointIndex).OrderBy(i => i).ToList();

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, indices);
            Assert.AreEqual(3, result.CheckpointManager.TotalCheckpoints);
        }

        [Test]
        public void Generate_CourseGateCountBeyondExplicit_AutoGeneratesRemainder()
        {
            WorldSpecification spec = MinimalValidSpecification();
            spec.Obstacles.Add(new ObstacleSpecification { Id = "explicit_gate", Type = "gate", Position = new Vector3(0f, 25f, 0f), Scale = Vector3.one, CheckpointIndex = 0 });
            spec.Course.GateCount = 5; // 1 explicit + 4 auto-generated expected

            GeneratedWorldResult result = _generator.Generate(spec);
            Transform gates = result.Root.transform.Find("Obstacles/Gates");

            Assert.AreEqual(5, gates.childCount);
        }

        [Test]
        public void Generate_CourseStyleTechnicalVsHighSpeed_ProducesDifferentGateSpacing()
        {
            WorldSpecification technicalSpec = MinimalValidSpecification();
            technicalSpec.Terrain.Width = 2000f;
            technicalSpec.Terrain.Depth = 2000f;
            technicalSpec.Course.Style = "technical";
            technicalSpec.Course.GateCount = 5;

            WorldSpecification highSpeedSpec = MinimalValidSpecification();
            highSpeedSpec.Terrain.Width = 2000f;
            highSpeedSpec.Terrain.Depth = 2000f;
            highSpeedSpec.Course.Style = "high_speed";
            highSpeedSpec.Course.GateCount = 5;

            GeneratedWorldResult technicalResult = _generator.Generate(technicalSpec);
            float technicalSpacing = AverageConsecutiveGateSpacing(technicalResult);
            _generator.Clear();

            GeneratedWorldResult highSpeedResult = _generator.Generate(highSpeedSpec);
            float highSpeedSpacing = AverageConsecutiveGateSpacing(highSpeedResult);

            Assert.Less(technicalSpacing, highSpeedSpacing, "A technical course style should produce tighter gate spacing than a high-speed one.");
        }

        private static float AverageConsecutiveGateSpacing(GeneratedWorldResult result)
        {
            Transform gates = result.Root.transform.Find("Obstacles/Gates");
            float total = 0f;
            for (int i = 1; i < gates.childCount; i++)
                total += Vector3.Distance(gates.GetChild(i - 1).position, gates.GetChild(i).position);
            return total / (gates.childCount - 1);
        }

        [Test]
        public void Generate_SameSeed_ProducesSameTerrainHeightAtSamePoint()
        {
            WorldSpecification specA = MinimalValidSpecification();
            WorldSpecification specB = MinimalValidSpecification(); // same seed (42) by construction

            GeneratedWorldResult resultA = _generator.Generate(specA);
            var terrainA = resultA.Root.transform.Find("Terrain").GetComponent<UnityEngine.Terrain>();
            float heightA = terrainA.SampleHeight(new Vector3(10f, 0f, 10f));
            _generator.Clear();

            GeneratedWorldResult resultB = _generator.Generate(specB);
            var terrainB = resultB.Root.transform.Find("Terrain").GetComponent<UnityEngine.Terrain>();
            float heightB = terrainB.SampleHeight(new Vector3(10f, 0f, 10f));

            Assert.AreEqual(heightA, heightB, 0.0001f);
        }

        [Test]
        public void Generate_CalledTwice_ClearsPreviousWorld()
        {
            GeneratedWorldResult first = _generator.Generate(MinimalValidSpecification());
            GameObject firstRoot = first.Root;

            _generator.Generate(MinimalValidSpecification());

            Assert.IsTrue(firstRoot == null, "The first generated world's root should have been destroyed by the second Generate() call.");
        }

        [Test]
        public void Generate_SpawnPosition_IsAboveTerrainAtThatPoint()
        {
            GeneratedWorldResult result = _generator.Generate(MinimalValidSpecification());
            var terrain = result.Root.transform.Find("Terrain").GetComponent<UnityEngine.Terrain>();
            float groundHeight = terrain.SampleHeight(new Vector3(result.SpawnPosition.x, 0f, result.SpawnPosition.z));

            Assert.Greater(result.SpawnPosition.y, groundHeight);
        }

        [Test]
        public void Generate_SpawnDeepInsideTerrain_FailsCleanly_LeavesNoGeneratedWorld()
        {
            WorldSpecification spec = MinimalValidSpecification();
            // Terrain max height is 40; a spawn far below ground with no alternates should be
            // rejected rather than silently relocated.
            spec.Spawn = new SpawnSpecification { Position = new Vector3(0f, -500f, 0f), AlternateSpawnPoints = new List<Vector3>() };

            GeneratedWorldResult result = _generator.Generate(spec);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Root);
        }

        [Test]
        public void Generate_SpawnFailsButAlternateSucceeds_UsesAlternate()
        {
            WorldSpecification spec = MinimalValidSpecification();
            spec.Spawn = new SpawnSpecification
            {
                Position = new Vector3(0f, -500f, 0f), // invalid
                AlternateSpawnPoints = new List<Vector3> { new Vector3(5f, 25f, 5f) } // valid
            };

            GeneratedWorldResult result = _generator.Generate(spec);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(new Vector3(5f, 25f, 5f), result.SpawnPosition);
        }
    }
}
