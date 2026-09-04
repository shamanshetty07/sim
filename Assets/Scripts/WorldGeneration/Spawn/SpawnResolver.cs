using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Terrain;
using UnityEngine;

namespace Sim.WorldGeneration.Spawn
{
    /// <summary>
    /// Validates a spawn position against the actually-generated world (terrain height,
    /// obstacle/environment colliders) — a check the Validator (Phase 6) cannot do, since it
    /// only ever sees numeric field values, never a built scene. Tries the specified position,
    /// then each AlternateSpawnPoint in order; if none are safe, fails cleanly rather than
    /// picking an arbitrary fallback — per this phase's explicit instruction, this
    /// deliberately overrides the "always produce a fallback spawn" behaviour
    /// docs/ARCHITECTURE.md originally sketched in Phase 2, before this constraint was made
    /// explicit. See docs/WORLD_GENERATION.md "Spawn resolution" for the full reasoning.
    /// </summary>
    public sealed class SpawnResolver
    {
        /// <summary>Rough safety radius for the overlap check — matches the drone's own SphereCollider radius (Phase 3, DroneRigBuilder: 0.18) plus a margin.</summary>
        private const float DroneSafetyRadius = 0.35f;

        private const float ClearanceAboveTerrain = 0.5f;

        public SpawnResolutionResult Resolve(SpawnSpecification specification, TerrainGenerationResult terrain)
        {
            if (TryPosition(specification.Position, specification.RotationEuler, terrain, out SpawnResolutionResult primaryResult))
                return primaryResult;

            if (specification.AlternateSpawnPoints != null)
            {
                foreach (Vector3 alternate in specification.AlternateSpawnPoints)
                {
                    if (TryPosition(alternate, specification.RotationEuler, terrain, out SpawnResolutionResult alternateResult))
                        return alternateResult;
                }
            }

            return SpawnResolutionResult.Failed(
                "No safe spawn position found among the specified position and its alternates — " +
                "all were outside terrain bounds, below ground clearance, or overlapping an obstacle/environment object.");
        }

        private static bool TryPosition(Vector3 position, Vector3 rotationEuler, TerrainGenerationResult terrain, out SpawnResolutionResult result)
        {
            result = null;

            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
                float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                return false;

            if (!terrain.IsWithinBounds(position.x, position.z))
                return false;

            float groundHeight = terrain.SampleHeight(position.x, position.z);
            if (position.y < groundHeight + ClearanceAboveTerrain)
                return false; // inside or too close to terrain

            if (OverlapsAnythingOtherThanTerrain(position, terrain))
                return false; // inside an obstacle or environment object

            result = SpawnResolutionResult.Succeeded(position, Quaternion.Euler(rotationEuler));
            return true;
        }

        private static bool OverlapsAnythingOtherThanTerrain(Vector3 position, TerrainGenerationResult terrain)
        {
            Collider terrainCollider = terrain.TerrainObject != null ? terrain.TerrainObject.GetComponent<Collider>() : null;
            Collider[] overlaps = Physics.OverlapSphere(position, DroneSafetyRadius, ~0, QueryTriggerInteraction.Ignore);

            foreach (Collider collider in overlaps)
            {
                if (collider == terrainCollider) continue;
                return true;
            }

            return false;
        }
    }
}
