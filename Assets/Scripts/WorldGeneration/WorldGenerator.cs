using System;
using Sim.Gameplay;
using Sim.Utilities;
using Sim.WorldGeneration.Environment;
using Sim.WorldGeneration.Lighting;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Obstacles;
using Sim.WorldGeneration.Spawn;
using Sim.WorldGeneration.Terrain;
using Sim.WorldGeneration.Weather;
using UnityEngine;

namespace Sim.WorldGeneration
{
    /// <summary>
    /// Deterministically builds the playable Unity scene described by a validated
    /// WorldSpecification. This class is the orchestrator only — every real generation
    /// concern (terrain shape, object placement, obstacle/course layout, lighting, weather,
    /// spawn safety) lives in its own class under WorldGeneration/{Terrain,Environment,
    /// Obstacles,Lighting,Weather,Spawn}, injected here with sensible defaults.
    ///
    /// Contract: input must already be validated (WorldSpecificationValidator, Phase 6) — this
    /// class does not re-validate field values, only handles world-specific safety concerns a
    /// value-level validator cannot (spawn-vs-actual-geometry safety — see SpawnResolver).
    ///
    /// Deliberately has no reference to Sim.AI, Sim.AI.WorldDesign, or anything Reactor-shaped
    /// — per this phase's explicit instruction, the dependency direction stops at
    /// WorldSpecification. It also has no reference to Sim.Drone — the caller (Editor tooling
    /// for now; a future runtime UI) is responsible for placing the drone at
    /// GeneratedWorldResult.SpawnPosition/SpawnRotation via DroneController.SetSpawn +
    /// ResetToSpawn, keeping world construction and drone control cleanly separate.
    ///
    /// Regeneration safety: Generate() always clears any world it previously built (tracked via
    /// _currentRoot) before building a new one, and on any failure it clears the partial result
    /// too — the scene is never left with a half-built or stale GeneratedWorld root.
    /// </summary>
    public sealed class WorldGenerator
    {
        private readonly TerrainGenerator _terrainGenerator;
        private readonly EnvironmentGenerator _environmentGenerator;
        private readonly ObstacleGenerator _obstacleGenerator;
        private readonly LightingGenerator _lightingGenerator;
        private readonly WeatherGenerator _weatherGenerator;
        private readonly SpawnResolver _spawnResolver;

        private GameObject _currentRoot;

        public WorldGenerator(
            TerrainGenerator terrainGenerator = null,
            EnvironmentGenerator environmentGenerator = null,
            ObstacleGenerator obstacleGenerator = null,
            LightingGenerator lightingGenerator = null,
            WeatherGenerator weatherGenerator = null,
            SpawnResolver spawnResolver = null)
        {
            _terrainGenerator = terrainGenerator ?? new TerrainGenerator();
            _environmentGenerator = environmentGenerator ?? new EnvironmentGenerator();
            _obstacleGenerator = obstacleGenerator ?? new ObstacleGenerator();
            _lightingGenerator = lightingGenerator ?? new LightingGenerator();
            _weatherGenerator = weatherGenerator ?? new WeatherGenerator();
            _spawnResolver = spawnResolver ?? new SpawnResolver();
        }

        public GeneratedWorldResult Generate(WorldSpecification specification)
        {
            if (specification == null)
                return GeneratedWorldResult.Failed("WorldSpecification was null.");

            Clear();

            var root = new GameObject("GeneratedWorld");
            _currentRoot = root;

            try
            {
                var seedManager = new WorldSeedManager(specification.Seed);

                TerrainGenerationResult terrainResult = _terrainGenerator.Generate(specification.Terrain, root.transform, seedManager);

                var environmentRoot = new GameObject("Environment");
                environmentRoot.transform.SetParent(root.transform, false);
                _environmentGenerator.Generate(specification.EnvironmentObjects, environmentRoot.transform, terrainResult, seedManager);

                var obstacleRoot = new GameObject("Obstacles");
                obstacleRoot.transform.SetParent(root.transform, false);
                ObstacleGenerationResult obstacleResult = _obstacleGenerator.Generate(specification, obstacleRoot.transform, terrainResult, seedManager);

                _lightingGenerator.Configure(specification.Lighting, root.transform);
                _weatherGenerator.Configure(specification.Weather, root.transform, terrainResult.Width, terrainResult.Depth);

                SpawnResolutionResult spawnResult = _spawnResolver.Resolve(specification.Spawn, terrainResult);
                if (!spawnResult.Success)
                {
                    Debug.LogWarning($"[WorldGenerator] Spawn resolution failed: {spawnResult.ErrorMessage}");
                    Clear();
                    return GeneratedWorldResult.Failed(spawnResult.ErrorMessage);
                }

                var spawnMarker = new GameObject("Spawn");
                spawnMarker.transform.SetParent(root.transform, false);
                spawnMarker.transform.position = spawnResult.Position;
                spawnMarker.transform.rotation = spawnResult.Rotation;

                var checkpointManager = new CheckpointManager(obstacleRoot);
                var bounds = new WorldRuntimeBounds(terrainResult);

                Debug.Log($"[WorldGenerator] Generated '{specification.WorldName}' — " +
                          $"{obstacleResult.Checkpoints.Count} checkpoints, spawn at {spawnResult.Position}.");

                return GeneratedWorldResult.Succeeded(root, spawnResult.Position, spawnResult.Rotation, checkpointManager, bounds);
            }
            catch (Exception ex)
            {
                // A generator throwing mid-pipeline must not crash the caller or leave a
                // half-built world sitting in the scene.
                Debug.LogError($"[WorldGenerator] Generation threw an unexpected exception: {ex}");
                Clear();
                return GeneratedWorldResult.Failed("World generation failed unexpectedly.");
            }
        }

        /// <summary>Destroys the currently-generated world, if any. Safe to call repeatedly or when nothing has been generated yet.</summary>
        public void Clear()
        {
            if (_currentRoot == null) return;

            UnityLifecycleUtility.DestroySafely(_currentRoot);
            _currentRoot = null;
        }
    }
}
