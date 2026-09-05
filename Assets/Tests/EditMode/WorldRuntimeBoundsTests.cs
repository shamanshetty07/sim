using NUnit.Framework;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Terrain;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// WorldRuntimeBounds over a real, generated UnityEngine.Terrain (built via the actual
    /// TerrainGenerator, not hand-rolled TerrainData) — a genuine EditMode-runnable test since
    /// Unity's Terrain/heightmap system works outside Play mode.
    /// </summary>
    public class WorldRuntimeBoundsTests
    {
        private GameObject _parent;
        private TerrainGenerationResult _terrain;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("TerrainParent");
            var spec = new TerrainSpecification { TerrainType = "flat", Width = 200f, Depth = 300f, MaxHeight = 40f, HeightVariation01 = 0.3f };
            _terrain = new TerrainGenerator().Generate(spec, _parent.transform, new WorldSeedManager(1));
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null) Object.DestroyImmediate(_parent);
        }

        [Test]
        public void Constructor_NullTerrain_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new WorldRuntimeBounds(null));
        }

        [Test]
        public void Properties_MatchUnderlyingTerrainResult()
        {
            var bounds = new WorldRuntimeBounds(_terrain);

            Assert.AreEqual(_terrain.Origin, bounds.Origin);
            Assert.AreEqual(_terrain.Width, bounds.Width);
            Assert.AreEqual(_terrain.Depth, bounds.Depth);
            Assert.AreEqual(_terrain.MaxHeight, bounds.MaxHeight);
        }

        [Test]
        public void IsWithinHorizontalBounds_CenterPoint_True()
        {
            var bounds = new WorldRuntimeBounds(_terrain);
            Assert.IsTrue(bounds.IsWithinHorizontalBounds(0f, 0f));
        }

        [Test]
        public void IsWithinHorizontalBounds_FarOutside_False()
        {
            var bounds = new WorldRuntimeBounds(_terrain);
            Assert.IsFalse(bounds.IsWithinHorizontalBounds(10000f, 10000f));
        }

        [Test]
        public void SampleGroundHeight_MatchesUnderlyingTerrainSample()
        {
            var bounds = new WorldRuntimeBounds(_terrain);
            Assert.AreEqual(_terrain.SampleHeight(5f, 5f), bounds.SampleGroundHeight(5f, 5f), 0.0001f);
        }
    }
}
