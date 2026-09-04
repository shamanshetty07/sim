using System.Collections.Generic;
using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.WorldGeneration.Validation
{
    /// <summary>
    /// First real validation pass for a WorldSpecification (Phase 5 shipped only the data
    /// contracts; this is the actual logic). Policy, applied consistently throughout:
    ///
    ///  - A null required nested object (Terrain/Weather/Lighting/Spawn/Flight) is repaired by
    ///    substituting a fresh default instance (all of them have sensible parameterless
    ///    defaults) — Warning, not Error.
    ///  - NaN/Infinity in a numeric field is repaired by substituting that field's own
    ///    documented default — Warning.
    ///  - An out-of-range numeric value (negative count, a size outside WorldGenerationLimits)
    ///    is clamped into range — Warning.
    ///  - An unrecognized free-form string (obstacle Type, object Category) is left as-is and
    ///    reported as a Warning, not rejected — Phase 9/10's primitive-fallback system handles
    ///    an unrecognized value at generation time; the validator's job is only to flag it.
    ///  - Errors are reserved for genuinely unrecoverable cases: the specification itself
    ///    being null, or OriginalPrompt being missing (the core product principle — prompt
    ///    preservation — has already been violated somewhere upstream if this happens; papering
    ///    over it with a fabricated prompt would hide a real bug, not fix one).
    ///
    /// This mirrors the repair-vs-reject philosophy already documented in
    /// docs/ARCHITECTURE.md §7 ("Spec has recoverable issues... Validator repairs in place...
    /// vs. unrecoverable errors... stop before any Unity object is created").
    /// </summary>
    public sealed class WorldSpecificationValidator : IWorldSpecificationValidator
    {
        private static readonly HashSet<string> KnownObstacleTypes = new HashSet<string>
        {
            "gate", "ring", "wall", "pole", "tunnel", "checkpoint", "landing_pad"
        };

        public ValidationResult Validate(WorldSpecification specification)
        {
            var errors = new List<ValidationError>();

            if (specification == null)
            {
                errors.Add(new ValidationError("(root)", "WorldSpecification is null.", ValidationSeverity.Error));
                return new ValidationResult { Errors = errors, RepairedSpecification = null };
            }

            if (string.IsNullOrWhiteSpace(specification.OriginalPrompt))
            {
                errors.Add(new ValidationError(
                    nameof(WorldSpecification.OriginalPrompt),
                    "OriginalPrompt is missing — the prompt must survive the entire pipeline; this indicates an upstream bug, not something safe to paper over.",
                    ValidationSeverity.Error));
            }

            ValidateTerrain(specification, errors);
            ValidateEnvironmentObjects(specification, errors);
            ValidateObstacles(specification, errors);
            ValidateWeather(specification, errors);
            ValidateLighting(specification, errors);
            ValidateSpawn(specification, errors);
            ValidateFlight(specification, errors);

            bool hasBlockingError = errors.Exists(e => e.Severity == ValidationSeverity.Error);
            return new ValidationResult
            {
                Errors = errors,
                RepairedSpecification = hasBlockingError ? null : specification
            };
        }

        private static void ValidateTerrain(WorldSpecification spec, List<ValidationError> errors)
        {
            if (spec.Terrain == null)
            {
                spec.Terrain = new TerrainSpecification();
                errors.Add(Warning("Terrain", "Terrain was null; substituted defaults."));
                return;
            }

            spec.Terrain.Width = RepairDimension(spec.Terrain.Width, 1000f, "Terrain.Width", errors);
            spec.Terrain.Depth = RepairDimension(spec.Terrain.Depth, 1000f, "Terrain.Depth", errors);
            spec.Terrain.MaxHeight = RepairDimension(spec.Terrain.MaxHeight, 100f, "Terrain.MaxHeight", errors, allowSmallerMin: 0f);

            float clampedVariation = Mathf.Clamp01(SafeOrDefault(spec.Terrain.HeightVariation01, 0.4f));
            if (!Mathf.Approximately(clampedVariation, spec.Terrain.HeightVariation01))
            {
                errors.Add(Warning("Terrain.HeightVariation01", $"Clamped to [0,1] (was {spec.Terrain.HeightVariation01})."));
                spec.Terrain.HeightVariation01 = clampedVariation;
            }
        }

        private static float RepairDimension(float value, float fallback, string field, List<ValidationError> errors, float allowSmallerMin = WorldGenerationLimits.MinTerrainDimensionMeters)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                errors.Add(Warning(field, $"Was NaN/Infinity; substituted default {fallback}."));
                return fallback;
            }

            // Height uses its own, smaller ceiling than width/depth.
            float ceiling = field.EndsWith("MaxHeight")
                ? WorldGenerationLimits.MaxTerrainHeightMeters
                : WorldGenerationLimits.MaxTerrainDimensionMeters;
            float clamped = Mathf.Clamp(value, allowSmallerMin, ceiling);

            if (!Mathf.Approximately(clamped, value))
            {
                errors.Add(Warning(field, $"Clamped to valid range (was {value}, now {clamped})."));
            }

            return clamped;
        }

        private static void ValidateEnvironmentObjects(WorldSpecification spec, List<ValidationError> errors)
        {
            if (spec.EnvironmentObjects == null)
            {
                spec.EnvironmentObjects = new List<ObjectSpecification>();
                errors.Add(Warning("EnvironmentObjects", "List was null; substituted an empty list."));
                return;
            }

            if (spec.EnvironmentObjects.Count > WorldGenerationLimits.MaxEnvironmentObjectCategories)
            {
                int removed = spec.EnvironmentObjects.Count - WorldGenerationLimits.MaxEnvironmentObjectCategories;
                spec.EnvironmentObjects.RemoveRange(WorldGenerationLimits.MaxEnvironmentObjectCategories, removed);
                errors.Add(Warning("EnvironmentObjects", $"Trimmed {removed} categories over the {WorldGenerationLimits.MaxEnvironmentObjectCategories}-category limit."));
            }

            for (int i = 0; i < spec.EnvironmentObjects.Count; i++)
            {
                ObjectSpecification obj = spec.EnvironmentObjects[i];
                if (obj == null)
                {
                    spec.EnvironmentObjects[i] = new ObjectSpecification { Category = "misc" };
                    errors.Add(Warning($"EnvironmentObjects[{i}]", "Entry was null; substituted a default 'misc' entry."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(obj.Category))
                {
                    obj.Category = "misc";
                    errors.Add(Warning($"EnvironmentObjects[{i}].Category", "Empty category defaulted to 'misc'."));
                }

                if (obj.Count < 0)
                {
                    errors.Add(Warning($"EnvironmentObjects[{i}].Count", $"Negative count ({obj.Count}) clamped to 0."));
                    obj.Count = 0;
                }
                else if (obj.Count > WorldGenerationLimits.MaxObjectCountPerCategory)
                {
                    errors.Add(Warning($"EnvironmentObjects[{i}].Count", $"Count {obj.Count} exceeds the per-category limit; clamped to {WorldGenerationLimits.MaxObjectCountPerCategory}."));
                    obj.Count = WorldGenerationLimits.MaxObjectCountPerCategory;
                }

                float clampedDensity = Mathf.Clamp01(SafeOrDefault(obj.Density01, 0f));
                if (!Mathf.Approximately(clampedDensity, obj.Density01))
                {
                    errors.Add(Warning($"EnvironmentObjects[{i}].Density01", "Clamped to [0,1]."));
                    obj.Density01 = clampedDensity;
                }
            }
        }

        private static void ValidateObstacles(WorldSpecification spec, List<ValidationError> errors)
        {
            if (spec.Obstacles == null)
            {
                spec.Obstacles = new List<ObstacleSpecification>();
                errors.Add(Warning("Obstacles", "List was null; substituted an empty list."));
                return;
            }

            if (spec.Obstacles.Count > WorldGenerationLimits.MaxObstacleCount)
            {
                int removed = spec.Obstacles.Count - WorldGenerationLimits.MaxObstacleCount;
                spec.Obstacles.RemoveRange(WorldGenerationLimits.MaxObstacleCount, removed);
                errors.Add(Warning("Obstacles", $"Trimmed {removed} obstacles over the {WorldGenerationLimits.MaxObstacleCount} limit."));
            }

            for (int i = 0; i < spec.Obstacles.Count; i++)
            {
                ObstacleSpecification obstacle = spec.Obstacles[i];
                if (obstacle == null)
                {
                    spec.Obstacles[i] = new ObstacleSpecification { Id = $"obstacle_{i}", Type = "gate" };
                    errors.Add(Warning($"Obstacles[{i}]", "Entry was null; substituted a default gate."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(obstacle.Id))
                {
                    obstacle.Id = $"obstacle_{i}";
                    errors.Add(Warning($"Obstacles[{i}].Id", "Missing Id; assigned a generated one."));
                }

                if (string.IsNullOrWhiteSpace(obstacle.Type))
                {
                    obstacle.Type = "gate";
                    errors.Add(Warning($"Obstacles[{i}].Type", "Missing Type; defaulted to 'gate'."));
                }
                else if (!KnownObstacleTypes.Contains(obstacle.Type))
                {
                    errors.Add(Warning($"Obstacles[{i}].Type", $"Unrecognized obstacle type '{obstacle.Type}' — will fall back to a primitive placeholder at generation time."));
                }

                obstacle.Position = RepairVector(obstacle.Position, Vector3.zero, $"Obstacles[{i}].Position", errors);
                obstacle.RotationEuler = RepairVector(obstacle.RotationEuler, Vector3.zero, $"Obstacles[{i}].RotationEuler", errors);
                obstacle.Scale = RepairScale(obstacle.Scale, $"Obstacles[{i}].Scale", errors);

                if (obstacle.CheckpointIndex.HasValue && obstacle.CheckpointIndex.Value < 0)
                {
                    errors.Add(Warning($"Obstacles[{i}].CheckpointIndex", "Negative checkpoint index treated as 'not a checkpoint'."));
                    obstacle.CheckpointIndex = null;
                }
            }
        }

        private static Vector3 RepairVector(Vector3 value, Vector3 fallback, string field, List<ValidationError> errors)
        {
            if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) ||
                float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z))
            {
                errors.Add(Warning(field, $"Contained NaN/Infinity; substituted {fallback}."));
                return fallback;
            }

            return value;
        }

        private static Vector3 RepairScale(Vector3 value, string field, List<ValidationError> errors)
        {
            Vector3 safe = RepairVector(value, Vector3.one, field, errors);
            Vector3 clamped = new Vector3(
                Mathf.Clamp(safe.x, WorldGenerationLimits.MinObstacleScaleComponent, WorldGenerationLimits.MaxObstacleScaleComponent),
                Mathf.Clamp(safe.y, WorldGenerationLimits.MinObstacleScaleComponent, WorldGenerationLimits.MaxObstacleScaleComponent),
                Mathf.Clamp(safe.z, WorldGenerationLimits.MinObstacleScaleComponent, WorldGenerationLimits.MaxObstacleScaleComponent));

            if (clamped != safe)
                errors.Add(Warning(field, $"Degenerate scale component(s) clamped (was {safe}, now {clamped})."));

            return clamped;
        }

        private static void ValidateWeather(WorldSpecification spec, List<ValidationError> errors)
        {
            if (spec.Weather == null)
            {
                spec.Weather = new WeatherSpecification();
                errors.Add(Warning("Weather", "Weather was null; substituted defaults."));
                return;
            }

            spec.Weather.FogDensity01 = ClampField(spec.Weather.FogDensity01, "Weather.FogDensity01", errors);
            spec.Weather.WindStrength01 = ClampField(spec.Weather.WindStrength01, "Weather.WindStrength01", errors);
        }

        private static void ValidateLighting(WorldSpecification spec, List<ValidationError> errors)
        {
            if (spec.Lighting == null)
            {
                spec.Lighting = new LightingSpecification();
                errors.Add(Warning("Lighting", "Lighting was null; substituted defaults."));
                return;
            }

            float t = spec.Lighting.TimeOfDayHours;
            if (float.IsNaN(t) || float.IsInfinity(t) || t < 0f || t > 24f)
            {
                errors.Add(Warning("Lighting.TimeOfDayHours", $"Out of [0,24] range (was {t}); wrapped/clamped."));
                spec.Lighting.TimeOfDayHours = float.IsNaN(t) || float.IsInfinity(t) ? 12f : Mathf.Clamp(t, 0f, 24f);
            }
        }

        private static void ValidateSpawn(WorldSpecification spec, List<ValidationError> errors)
        {
            if (spec.Spawn == null)
            {
                spec.Spawn = new SpawnSpecification();
                errors.Add(Warning("Spawn", "Spawn was null; substituted defaults."));
                return;
            }

            spec.Spawn.Position = RepairVector(spec.Spawn.Position, new Vector3(0f, 5f, 0f), "Spawn.Position", errors);
            spec.Spawn.RotationEuler = RepairVector(spec.Spawn.RotationEuler, Vector3.zero, "Spawn.RotationEuler", errors);
            spec.Spawn.AlternateSpawnPoints ??= new List<Vector3>();
        }

        private static void ValidateFlight(WorldSpecification spec, List<ValidationError> errors)
        {
            if (spec.Flight == null)
            {
                spec.Flight = new FlightCharacteristics();
                errors.Add(Warning("Flight", "FlightCharacteristics was null; substituted defaults."));
                return;
            }

            spec.Flight.TightnessScore01 = ClampField(spec.Flight.TightnessScore01, "Flight.TightnessScore01", errors);
            spec.Flight.ObstacleDensity01 = ClampField(spec.Flight.ObstacleDensity01, "Flight.ObstacleDensity01", errors);
            spec.Flight.VerticalityScore01 = ClampField(spec.Flight.VerticalityScore01, "Flight.VerticalityScore01", errors);
        }

        private static float ClampField(float value, string field, List<ValidationError> errors)
        {
            float clamped = Mathf.Clamp01(SafeOrDefault(value, 0f));
            if (!Mathf.Approximately(clamped, value))
                errors.Add(Warning(field, $"Clamped to [0,1] (was {value})."));
            return clamped;
        }

        private static float SafeOrDefault(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        private static ValidationError Warning(string field, string message) =>
            new ValidationError(field, message, ValidationSeverity.Warning);
    }
}
