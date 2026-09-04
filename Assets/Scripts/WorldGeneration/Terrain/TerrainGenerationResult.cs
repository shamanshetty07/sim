using UnityEngine;

namespace Sim.WorldGeneration.Terrain
{
    /// <summary>Handle to a generated Terrain, with the height-sampling helper every downstream generator (environment placement, obstacle placement, spawn safety) needs.</summary>
    public sealed class TerrainGenerationResult
    {
        public GameObject TerrainObject { get; }
        public UnityEngine.Terrain Terrain { get; }
        public Vector3 Origin { get; }
        public float Width { get; }
        public float Depth { get; }
        public float MaxHeight { get; }

        public TerrainGenerationResult(GameObject terrainObject, UnityEngine.Terrain terrain, Vector3 origin, float width, float depth, float maxHeight)
        {
            TerrainObject = terrainObject;
            Terrain = terrain;
            Origin = origin;
            Width = width;
            Depth = depth;
            MaxHeight = maxHeight;
        }

        /// <summary>World-space ground height at the given world X/Z.</summary>
        public float SampleHeight(float worldX, float worldZ) =>
            Terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + Origin.y;

        public bool IsWithinBounds(float worldX, float worldZ) =>
            worldX >= Origin.x && worldX <= Origin.x + Width &&
            worldZ >= Origin.z && worldZ <= Origin.z + Depth;
    }
}
