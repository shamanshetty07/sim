using System;
using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.WorldGeneration.Terrain
{
    /// <summary>
    /// Builds a Unity <see cref="UnityEngine.Terrain"/> from a TerrainSpecification. Chose
    /// Unity's built-in Terrain system over a hand-rolled mesh specifically because it's the
    /// most practical option for this phase: a TerrainCollider comes for free (real collision,
    /// no extra work), Unity's own LOD/rendering optimizations apply automatically, and the
    /// heightmap API is a well-trodden path — no custom mesh-generation/collision code to get
    /// wrong. See docs/WORLD_GENERATION.md "Terrain implementation" for the full reasoning and
    /// what a mesh-based alternative would trade off.
    ///
    /// <see cref="HeightmapResolution"/> is kept deliberately small (129×129 — a valid Unity
    /// heightmap resolution, being 2^n+1) for a fast-generating prototype; a richer visual
    /// pass can raise this later without changing this class's structure.
    ///
    /// Terrain shape is procedural noise (Perlin-based fractal noise, deterministic — see
    /// class remarks on why not UnityEngine.Random), not a claim of geological realism. Several
    /// TerrainType values ("mountain"/"canyon"/"valley"/"island"/"flat") get distinct height
    /// profiles; anything unrecognized (including "desert"/"forest", which describe biome/
    /// vegetation, not terrain silhouette) falls back to gentle "hills" — vegetation/rock
    /// density for those biomes is EnvironmentGenerator's job, not this class's.
    /// </summary>
    public sealed class TerrainGenerator
    {
        private const int HeightmapResolution = 129;

        public TerrainGenerationResult Generate(TerrainSpecification specification, Transform parent, WorldSeedManager seedManager)
        {
            Random rng = seedManager.GetRandomForStage("terrain");

            float width = Mathf.Max(specification.Width, 10f);
            float depth = Mathf.Max(specification.Depth, 10f);
            float maxHeight = Mathf.Max(specification.MaxHeight, 1f);

            var terrainData = new TerrainData
            {
                heightmapResolution = HeightmapResolution,
                size = new Vector3(width, maxHeight, depth)
            };
            terrainData.SetHeights(0, 0, GenerateHeights(specification.TerrainType, specification.HeightVariation01, rng));

            GameObject terrainObject = UnityEngine.Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = "Terrain";
            terrainObject.transform.SetParent(parent, false);
            // Centered on the world origin, so obstacle/spawn coordinates in the specification
            // (which are not terrain-relative) land inside the generated bounds by default.
            Vector3 origin = new Vector3(-width / 2f, 0f, -depth / 2f);
            terrainObject.transform.position = origin;

            var terrain = terrainObject.GetComponent<UnityEngine.Terrain>();
            // TerrainCollider is added automatically by CreateTerrainGameObject — real collision
            // for the drone, no extra work.

            return new TerrainGenerationResult(terrainObject, terrain, origin, width, depth, maxHeight);
        }

        private static float[,] GenerateHeights(string terrainType, float heightVariation01, Random rng)
        {
            var heights = new float[HeightmapResolution, HeightmapResolution];

            // A random-but-seeded offset into noise space so different seeds produce visibly
            // different terrain even with an identical TerrainType/variation.
            float offsetX = (float)rng.NextDouble() * 1000f;
            float offsetZ = (float)rng.NextDouble() * 1000f;
            string type = (terrainType ?? "hills").Trim().ToLowerInvariant();

            for (int z = 0; z < HeightmapResolution; z++)
            {
                for (int x = 0; x < HeightmapResolution; x++)
                {
                    float nx = (float)x / HeightmapResolution;
                    float nz = (float)z / HeightmapResolution;
                    // TerrainData.SetHeights indexes [y, x] where the first axis maps to Z.
                    heights[z, x] = SampleHeight01(nx, nz, type, heightVariation01, offsetX, offsetZ);
                }
            }

            return heights;
        }

        private static float SampleHeight01(float nx, float nz, string type, float variation, float offsetX, float offsetZ)
        {
            float noise = FractalNoise(nx * 4f + offsetX, nz * 4f + offsetZ, octaves: 4, persistence: 0.5f, lacunarity: 2f);

            switch (type)
            {
                case "flat":
                    return noise * 0.03f;
                case "canyon":
                    return CanyonProfile(nx, noise, variation);
                case "valley":
                    return ValleyProfile(nx, noise, variation);
                case "island":
                    return noise * IslandFalloff(nx, nz) * Mathf.Lerp(0.4f, 1f, variation);
                case "mountain":
                    // Raising noise to a power > 1 biases the distribution toward sharper peaks
                    // and flatter troughs than raw Perlin noise gives on its own.
                    return Mathf.Pow(Mathf.Clamp01(noise), 1.4f) * Mathf.Lerp(0.5f, 1f, variation);
                default:
                    // "hills" and anything unrecognized (incl. "desert"/"forest"/"city"/"hybrid").
                    return noise * Mathf.Lerp(0.15f, 0.6f, variation);
            }
        }

        /// <summary>Sum of several Perlin octaves at decreasing amplitude/increasing frequency, normalized to ~0-1. The standard "fractal Brownian motion" technique for natural-looking terrain noise.</summary>
        private static float FractalNoise(float x, float z, int octaves, float persistence, float lacunarity)
        {
            float total = 0f, amplitude = 1f, frequency = 1f, maxPossible = 0f;
            for (int i = 0; i < octaves; i++)
            {
                total += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
                maxPossible += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total / maxPossible;
        }

        /// <summary>A trench along the Z axis at the terrain's X center, walls rising toward the X edges.</summary>
        private static float CanyonProfile(float nx, float noise, float variation)
        {
            float distanceFromCenter = Mathf.Abs(nx - 0.5f) * 2f;
            float baseHeight = Mathf.Lerp(0.05f, 0.7f, distanceFromCenter);
            return Mathf.Clamp01(baseHeight + (noise - 0.5f) * 0.15f * variation);
        }

        /// <summary>A wide, shallow dip along the Z axis — gentler than a canyon (quadratic falloff toward the center instead of linear).</summary>
        private static float ValleyProfile(float nx, float noise, float variation)
        {
            float distanceFromCenter = Mathf.Abs(nx - 0.5f) * 2f;
            float baseHeight = Mathf.Lerp(0.15f, 0.55f, distanceFromCenter * distanceFromCenter);
            return Mathf.Clamp01(baseHeight + (noise - 0.5f) * 0.2f * variation);
        }

        /// <summary>1 at the terrain center, falling to 0 toward the edges — combined with noise elsewhere to produce a landmass surrounded by low/flat terrain.</summary>
        private static float IslandFalloff(float nx, float nz)
        {
            float dx = nx - 0.5f, dz = nz - 0.5f;
            float distance = Mathf.Sqrt(dx * dx + dz * dz) * 2f;
            return Mathf.Clamp01(1f - distance);
        }
    }
}
