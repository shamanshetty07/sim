using System;
using Sim.WorldGeneration.Terrain;
using UnityEngine;

namespace Sim.WorldGeneration
{
    /// <summary>
    /// Minimal runtime bounds representation for one generated world, consumed by Phase 12's
    /// Sim.Gameplay.DroneRecoveryController. Deliberately not the full TerrainGenerationResult
    /// itself — that also carries a GameObject/UnityEngine.Terrain reference the gameplay layer
    /// has no business touching directly; this exposes only the horizontal-bounds/ground-height
    /// queries recovery actually needs. Built once per generation from TerrainGenerationResult's
    /// own already-computed data (WorldGenerator constructs one right alongside the terrain) —
    /// no terrain math is duplicated here, and no new bounds-computation logic exists at all;
    /// this class is a narrow, read-only view over data WorldGenerator already produced.
    /// </summary>
    public sealed class WorldRuntimeBounds
    {
        private readonly TerrainGenerationResult _terrain;

        /// <summary>World-space minimum corner of the generated terrain's footprint (Y is typically 0 — terrain height varies above this, never below it as a bound).</summary>
        public Vector3 Origin => _terrain.Origin;

        public float Width => _terrain.Width;
        public float Depth => _terrain.Depth;

        /// <summary>The terrain heightmap's vertical size (TerrainData.size.y) — informational only; Phase 12 deliberately does not use this to impose a maximum-altitude recovery boundary (see docs/PHASE_12_RECOVERY.md "Why no max altitude").</summary>
        public float MaxHeight => _terrain.MaxHeight;

        public WorldRuntimeBounds(TerrainGenerationResult terrain)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        }

        /// <summary>True if worldX/worldZ falls within the generated terrain's actual horizontal footprint (no margin applied — callers wanting a recovery margin add it themselves, see DroneRecoveryConfig.RecoveryMargin).</summary>
        public bool IsWithinHorizontalBounds(float worldX, float worldZ) => _terrain.IsWithinBounds(worldX, worldZ);

        /// <summary>World-space ground height at the given world X/Z — the same sampling SpawnResolver/ObstacleGenerator already rely on.</summary>
        public float SampleGroundHeight(float worldX, float worldZ) => _terrain.SampleHeight(worldX, worldZ);
    }
}
