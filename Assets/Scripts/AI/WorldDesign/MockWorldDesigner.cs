using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sim.Utilities;
using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Development/testing stand-in for a real IWorldDesigner. Same philosophy as
    /// MockWorldGenerationService (Sim.AI, Phase 5/6): deliberately does NOT interpret the
    /// prompt — it always returns the same rich, well-formed example specification, with the
    /// prompt only echoed into Description so tests can confirm it survived. A mock that
    /// keyword-matched the prompt into different outputs would be exactly the "hardcoded
    /// biome parser pretending to be AI" architecture this project keeps being told to avoid;
    /// keeping the mock honestly non-interpretive means its behaviour can never be mistaken
    /// for what real prompt interpretation will do.
    ///
    /// The example itself is intentionally rich — populated Terrain/EnvironmentObjects/
    /// Obstacles/Course/Weather/Lighting/Spawn/Flight — specifically so downstream code
    /// (validator, and eventually WorldGenerator) has a realistic, fully-populated
    /// specification to develop and test against, not a mostly-empty stub.
    /// </summary>
    public sealed class MockWorldDesigner : IWorldDesigner
    {
        /// <summary>0 by default (instant) — set higher only for manual/dev testing of loading states.</summary>
        public int SimulatedDelayMilliseconds { get; set; } = 0;

        public async Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                return WorldDesignOutcome.Failed(WorldDesignFailureReason.InvalidResponse, "Request was null.");

            if (SimulatedDelayMilliseconds > 0)
                await Task.Delay(SimulatedDelayMilliseconds, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            int seed = request.Seed ?? StableHash.Fnv1a(request.Prompt);

            var specification = new WorldSpecification
            {
                OriginalPrompt = request.Prompt,
                WorldName = "Mock Example World",
                Description = $"Placeholder result — does not reflect the prompt's actual content. Prompt received: \"{request.Prompt}\"",
                Seed = seed,
                Scale = request.Constraints?.PreferredScale ?? WorldScale.Large,
                Flight = new FlightCharacteristics
                {
                    PreferredStyle = FlightStyle.Technical,
                    TightnessScore01 = 0.6f,
                    ObstacleDensity01 = 0.5f,
                    VerticalityScore01 = 0.7f
                },
                Terrain = new TerrainSpecification
                {
                    TerrainType = "mountain",
                    Width = 2000f,
                    Depth = 2000f,
                    MaxHeight = 400f,
                    HeightVariation01 = 0.8f,
                    HasWater = true,
                    WaterFeatureHint = "waterfalls"
                },
                EnvironmentObjects = new List<ObjectSpecification>
                {
                    new ObjectSpecification { Category = "pine_tree", Count = 400, PlacementHint = "dense_cluster" },
                    new ObjectSpecification { Category = "rock", Count = 120, PlacementHint = "along_cliffs" },
                    new ObjectSpecification { Category = "abandoned_cabin", Count = 3, PlacementHint = "scattered" },
                    new ObjectSpecification { Category = "cliff", Count = 6, PlacementHint = "ridge_line" }
                },
                Obstacles = BuildExampleGates(),
                Course = new CourseSpecification
                {
                    Style = "technical_then_high_speed",
                    Difficulty = "hard",
                    GateCount = 15,
                    SectionDescriptions = new List<string>
                    {
                        "Technical and tight through the pine forest and cabins.",
                        "Opens into a high-speed valley run toward the waterfalls."
                    }
                },
                Weather = new WeatherSpecification { Type = "clear", FogDensity01 = 0.05f, WindStrength01 = 0.2f },
                Lighting = new LightingSpecification { TimeOfDayHours = 17f, SunIntensity = 1.1f },
                Spawn = new SpawnSpecification { Position = new Vector3(0f, 30f, 0f) },
                Metadata = new WorldGenerationMetadata
                {
                    ProviderName = "MockWorldDesigner",
                    ProviderVersion = "phase7-placeholder",
                    RequestId = request.RequestId,
                    GeneratedAtUtc = System.DateTime.UtcNow,
                    GenerationDuration = System.TimeSpan.FromMilliseconds(SimulatedDelayMilliseconds)
                }
            };

            return WorldDesignOutcome.Succeeded(specification);
        }

        private static List<ObstacleSpecification> BuildExampleGates()
        {
            var obstacles = new List<ObstacleSpecification>();
            for (int i = 0; i < 15; i++)
            {
                obstacles.Add(new ObstacleSpecification
                {
                    Id = $"gate_{i:D2}",
                    Type = "gate",
                    Position = new Vector3(i * 40f, 20f + (i % 3) * 5f, i * 60f),
                    RotationEuler = new Vector3(0f, (i * 12f) % 360f, 0f),
                    Scale = Vector3.one,
                    CheckpointIndex = i
                });
            }

            return obstacles;
        }
    }
}
