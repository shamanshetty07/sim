using System.Collections.Generic;
using NUnit.Framework;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Environment;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Terrain;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Real EditMode tests against EnvironmentGenerator over a real generated
    /// UnityEngine.Terrain (via the actual TerrainGenerator — no reason to fake terrain
    /// sampling when Unity's own Terrain system runs fine in EditMode, same reasoning as
    /// WorldRuntimeBoundsTests/DroneRecoveryControllerTests).
    /// </summary>
    public class EnvironmentGeneratorTests
    {
        private GameObject _root;
        private EnvironmentGenerator _generator;
        private TerrainGenerationResult _terrain;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root");
            var environmentRoot = new GameObject("Environment");
            environmentRoot.transform.SetParent(_root.transform);

            var spec = new TerrainSpecification { TerrainType = "hills", Width = 2000f, Depth = 2000f, MaxHeight = 100f };
            _terrain = new TerrainGenerator().Generate(spec, _root.transform, new WorldSeedManager(1));

            _generator = new EnvironmentGenerator();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private Transform EnvironmentRoot => _root.transform.Find("Environment");

        private int TotalChildCount()
        {
            int total = 0;
            foreach (Transform group in EnvironmentRoot)
                total += group.childCount;
            return total;
        }

        [Test]
        public void Generate_RespectsRequestedCount_WhenWithinLimits()
        {
            var objects = new List<ObjectSpecification> { new ObjectSpecification { Category = "tree", Count = 10 } };

            _generator.Generate(objects, EnvironmentRoot, _terrain, new WorldSeedManager(1));

            Assert.AreEqual(10, TotalChildCount());
        }

        [Test]
        public void Generate_MultipleCategoriesWithinLimit_AllPlaced()
        {
            var objects = new List<ObjectSpecification>
            {
                new ObjectSpecification { Category = "tree", Count = 5 },
                new ObjectSpecification { Category = "rock", Count = 5 }
            };

            _generator.Generate(objects, EnvironmentRoot, _terrain, new WorldSeedManager(1));

            Assert.AreEqual(10, TotalChildCount());
        }

        [Test]
        public void Generate_TotalAcrossCategoriesExceedsLimit_ClampedToMaxTotal_LaterCategoryGetsOnlyRemainingBudget()
        {
            // Three categories, each individually well within
            // WorldGenerationLimits.MaxObjectCountPerCategory (20000), whose combined requested
            // count (12000) exceeds MaxTotalEnvironmentObjectCount (10000) — the combinatorial
            // case neither per-category limit alone prevents (see WorldGenerationLimits
            // remarks). Also confirms list-order allocation: earlier categories get their full
            // request first, a later one gets only whatever budget remains.
            const int perCategory = 4000; // 3 * 4000 = 12000 requested > 10000 total limit
            var objects = new List<ObjectSpecification>
            {
                new ObjectSpecification { Category = "tree", Count = perCategory },     // fits fully: 4000
                new ObjectSpecification { Category = "rock", Count = perCategory },     // fits fully: 4000 (8000 so far)
                new ObjectSpecification { Category = "building", Count = perCategory }  // only 2000 of budget remains
            };

            _generator.Generate(objects, EnvironmentRoot, _terrain, new WorldSeedManager(1));

            Transform trees = EnvironmentRoot.Find("Trees");
            Transform rocks = EnvironmentRoot.Find("Rocks");
            Transform buildings = EnvironmentRoot.Find("Buildings");

            Assert.AreEqual(perCategory, trees.childCount);
            Assert.AreEqual(perCategory, rocks.childCount);
            Assert.AreEqual(WorldGenerationLimits.MaxTotalEnvironmentObjectCount - 2 * perCategory, buildings.childCount);
            Assert.AreEqual(WorldGenerationLimits.MaxTotalEnvironmentObjectCount, TotalChildCount());
        }

        [Test]
        public void Generate_DeterministicCount_SameSpecificationAndSeed_SameTotal()
        {
            var objectsA = new List<ObjectSpecification> { new ObjectSpecification { Category = "tree", Count = 30 } };
            _generator.Generate(objectsA, EnvironmentRoot, _terrain, new WorldSeedManager(99));
            int countA = TotalChildCount();

            // Rebuild a fresh Environment root and repeat with an equivalent specification + seed.
            Object.DestroyImmediate(EnvironmentRoot.gameObject);
            var freshEnvironmentRoot = new GameObject("Environment");
            freshEnvironmentRoot.transform.SetParent(_root.transform);

            var objectsB = new List<ObjectSpecification> { new ObjectSpecification { Category = "tree", Count = 30 } };
            _generator.Generate(objectsB, freshEnvironmentRoot.transform, _terrain, new WorldSeedManager(99));
            int countB = 0;
            foreach (Transform group in freshEnvironmentRoot.transform)
                countB += group.childCount;

            Assert.AreEqual(countA, countB);
        }
    }
}
