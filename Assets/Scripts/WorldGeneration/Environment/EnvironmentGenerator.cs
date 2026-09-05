using System;
using System.Collections.Generic;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Terrain;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.WorldGeneration.Environment
{
    /// <summary>
    /// Places every ObjectSpecification entry from WorldSpecification.EnvironmentObjects,
    /// terrain-snapped, under the required Environment/{Trees,Rocks,Buildings,Vegetation,
    /// Structures} hierarchy. Object *shape* comes from IWorldPrefabRegistry (primitive
    /// fallbacks by default); this class owns count resolution, placement, and grouping only.
    ///
    /// Collision: every primitive IWorldPrefabRegistry builds via GameObject.CreatePrimitive
    /// already carries Unity's default collider for that shape (BoxCollider on cubes,
    /// SphereCollider on spheres, CapsuleCollider on cylinders) — nothing extra is needed here
    /// for solid objects to block the drone. The one deliberate exception is decorative-only
    /// water features, which the registry itself strips colliders from.
    ///
    /// Phase 15: Generate() enforces WorldGenerationLimits.MaxTotalEnvironmentObjectCount as a
    /// running total across every category — see its own remarks on why the per-category/
    /// per-category-count limits alone don't prevent a pathologically large combined count.
    /// </summary>
    public sealed class EnvironmentGenerator
    {
        private const float PlacementMarginMeters = 5f;
        private const float DensityAreaPerObjectSqMeters = 80f;

        private readonly IWorldPrefabRegistry _registry;

        public EnvironmentGenerator(IWorldPrefabRegistry registry = null)
        {
            _registry = registry ?? new PrimitiveWorldPrefabRegistry();
        }

        public void Generate(List<ObjectSpecification> objects, Transform environmentRoot, TerrainGenerationResult terrain, WorldSeedManager seedManager)
        {
            System.Random rng = seedManager.GetRandomForStage("environment");

            Transform trees = CreateGroup(environmentRoot, "Trees");
            Transform rocks = CreateGroup(environmentRoot, "Rocks");
            Transform buildings = CreateGroup(environmentRoot, "Buildings");
            Transform vegetation = CreateGroup(environmentRoot, "Vegetation");
            Transform structures = CreateGroup(environmentRoot, "Structures");

            if (objects == null) return;

            // Phase 15: MaxObjectCountPerCategory (per category) and MaxEnvironmentObjectCategories
            // (category count) are each already bounded, but their product is not — a
            // pathological specification could otherwise request up to 64 * 20000 objects. This
            // running total is the one place that actually sees the resolved count for *either*
            // an explicit Count or a Density01-derived one (ResolveCount only bounds each
            // category individually), so it's the correct place to cap the combined total — see
            // WorldGenerationLimits.MaxTotalEnvironmentObjectCount remarks.
            int totalPlaced = 0;

            foreach (ObjectSpecification spec in objects)
            {
                if (spec == null) continue;
                if (totalPlaced >= WorldGenerationLimits.MaxTotalEnvironmentObjectCount) break;

                Transform group = ResolveGroup(spec.Category, trees, rocks, buildings, vegetation, structures);
                int count = Mathf.Min(ResolveCount(spec, terrain), WorldGenerationLimits.MaxTotalEnvironmentObjectCount - totalPlaced);
                totalPlaced += count;

                // Cluster placement hints pick from a handful of seeded centers rather than a
                // fresh random center per object, so "dense_cluster" actually looks clustered.
                Vector3[] clusterCenters = BuildClusterCenters(spec.PlacementHint, terrain, rng);

                for (int i = 0; i < count; i++)
                {
                    Vector3 position = PickPosition(spec.PlacementHint, terrain, rng, clusterCenters);
                    GameObject instance = _registry.CreateInstance(spec.Category, group);
                    instance.transform.position = position;
                    instance.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    float scaleJitter = 0.85f + (float)rng.NextDouble() * 0.3f;
                    instance.transform.localScale *= scaleJitter;
                }
            }
        }

        private static Transform CreateGroup(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static Transform ResolveGroup(string category, Transform trees, Transform rocks, Transform buildings, Transform vegetation, Transform structures)
        {
            string normalized = (category ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("tree")) return trees;
            if (normalized.Contains("rock") || normalized.Contains("cliff") || normalized.Contains("boulder")) return rocks;
            if (normalized.Contains("building") || normalized.Contains("cabin") || normalized.Contains("house") || normalized.Contains("ruin") || normalized.Contains("tower")) return buildings;
            if (normalized.Contains("bush") || normalized.Contains("shrub") || normalized.Contains("vegetation") || normalized.Contains("grass")) return vegetation;
            return structures; // bridges, tunnels, water features, and anything else — the general-purpose bucket
        }

        private static int ResolveCount(ObjectSpecification spec, TerrainGenerationResult terrain)
        {
            int count = spec.Count;

            if (count <= 0 && spec.Density01 > 0f)
            {
                float area = terrain.Width * terrain.Depth;
                count = Mathf.RoundToInt(Mathf.Clamp01(spec.Density01) * area / DensityAreaPerObjectSqMeters);
            }

            // Defense in depth — WorldGenerator's contract is "validated input only", but a
            // one-line clamp here costs nothing and this is exactly the kind of unbounded-
            // object-count mistake the project's performance rules call out explicitly.
            return Mathf.Clamp(count, 0, WorldGenerationLimits.MaxObjectCountPerCategory);
        }

        private static Vector3[] BuildClusterCenters(string placementHint, TerrainGenerationResult terrain, System.Random rng)
        {
            if (!IsHint(placementHint, "cluster")) return null;

            const int clusterCount = 4;
            var centers = new Vector3[clusterCount];
            for (int i = 0; i < clusterCount; i++)
                centers[i] = RandomGroundPosition(terrain, rng);
            return centers;
        }

        private static Vector3 PickPosition(string placementHint, TerrainGenerationResult terrain, System.Random rng, Vector3[] clusterCenters)
        {
            if (clusterCenters != null)
            {
                Vector3 center = clusterCenters[rng.Next(clusterCenters.Length)];
                float jitterX = ((float)rng.NextDouble() - 0.5f) * terrain.Width * 0.08f;
                float jitterZ = ((float)rng.NextDouble() - 0.5f) * terrain.Depth * 0.08f;
                return SnapToTerrain(center.x + jitterX, center.z + jitterZ, terrain);
            }

            if (IsHint(placementHint, "cliff") || IsHint(placementHint, "ridge"))
                return PickHighestOfCandidates(terrain, rng, sampleCount: 5);

            if (IsHint(placementHint, "riverbank") || IsHint(placementHint, "lowland"))
                return PickLowestOfCandidates(terrain, rng, sampleCount: 5);

            return RandomGroundPosition(terrain, rng);
        }

        private static bool IsHint(string placementHint, string keyword) =>
            !string.IsNullOrEmpty(placementHint) && placementHint.ToLowerInvariant().Contains(keyword);

        private static Vector3 RandomGroundPosition(TerrainGenerationResult terrain, System.Random rng)
        {
            float x = terrain.Origin.x + PlacementMarginMeters + (float)rng.NextDouble() * (terrain.Width - 2f * PlacementMarginMeters);
            float z = terrain.Origin.z + PlacementMarginMeters + (float)rng.NextDouble() * (terrain.Depth - 2f * PlacementMarginMeters);
            return SnapToTerrain(x, z, terrain);
        }

        private static Vector3 PickHighestOfCandidates(TerrainGenerationResult terrain, System.Random rng, int sampleCount)
        {
            Vector3 best = RandomGroundPosition(terrain, rng);
            for (int i = 1; i < sampleCount; i++)
            {
                Vector3 candidate = RandomGroundPosition(terrain, rng);
                if (candidate.y > best.y) best = candidate;
            }

            return best;
        }

        private static Vector3 PickLowestOfCandidates(TerrainGenerationResult terrain, System.Random rng, int sampleCount)
        {
            Vector3 best = RandomGroundPosition(terrain, rng);
            for (int i = 1; i < sampleCount; i++)
            {
                Vector3 candidate = RandomGroundPosition(terrain, rng);
                if (candidate.y < best.y) best = candidate;
            }

            return best;
        }

        private static Vector3 SnapToTerrain(float x, float z, TerrainGenerationResult terrain) =>
            new Vector3(x, terrain.SampleHeight(x, z), z);
    }
}
