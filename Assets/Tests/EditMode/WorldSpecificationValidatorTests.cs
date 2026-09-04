using System.Collections.Generic;
using NUnit.Framework;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    public class WorldSpecificationValidatorTests
    {
        private WorldSpecificationValidator _validator;

        [SetUp]
        public void SetUp() => _validator = new WorldSpecificationValidator();

        private static WorldSpecification ValidSpec() => new WorldSpecification
        {
            OriginalPrompt = "Create a mountain FPV course.",
            Seed = 1
        };

        [Test]
        public void Validate_NullSpecification_ReturnsError_RepairedIsNull()
        {
            ValidationResult result = _validator.Validate(null);

            Assert.IsFalse(result.IsValid);
            Assert.IsNull(result.RepairedSpecification);
        }

        [Test]
        public void Validate_MissingPrompt_ReturnsError()
        {
            var spec = ValidSpec();
            spec.OriginalPrompt = "";

            ValidationResult result = _validator.Validate(spec);

            Assert.IsFalse(result.IsValid);
            Assert.IsNull(result.RepairedSpecification);
        }

        [Test]
        public void Validate_WellFormedSpecification_IsValid_NoErrors()
        {
            ValidationResult result = _validator.Validate(ValidSpec());

            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(result.RepairedSpecification);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [Test]
        public void Validate_NullTerrain_RepairsToDefault_Warning()
        {
            var spec = ValidSpec();
            spec.Terrain = null;

            ValidationResult result = _validator.Validate(spec);

            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(result.RepairedSpecification.Terrain);
            Assert.IsTrue(result.Errors.Exists(e => e.Severity == ValidationSeverity.Warning && e.Field == "Terrain"));
        }

        [Test]
        public void Validate_NaNTerrainWidth_RepairsToDefault()
        {
            var spec = ValidSpec();
            spec.Terrain.Width = float.NaN;

            ValidationResult result = _validator.Validate(spec);

            Assert.IsTrue(result.IsValid);
            Assert.IsFalse(float.IsNaN(result.RepairedSpecification.Terrain.Width));
        }

        [Test]
        public void Validate_TerrainWidthOverLimit_ClampsToLimit()
        {
            var spec = ValidSpec();
            spec.Terrain.Width = WorldGenerationLimits.MaxTerrainDimensionMeters * 10f;

            ValidationResult result = _validator.Validate(spec);

            Assert.AreEqual(WorldGenerationLimits.MaxTerrainDimensionMeters, result.RepairedSpecification.Terrain.Width);
            Assert.IsTrue(result.Errors.Exists(e => e.Field == "Terrain.Width"));
        }

        [Test]
        public void Validate_NegativeTerrainWidth_ClampsToMinimum()
        {
            var spec = ValidSpec();
            spec.Terrain.Width = -500f;

            ValidationResult result = _validator.Validate(spec);

            Assert.GreaterOrEqual(result.RepairedSpecification.Terrain.Width, WorldGenerationLimits.MinTerrainDimensionMeters);
        }

        [Test]
        public void Validate_HeightVariationOutOfRange_ClampsToZeroOne()
        {
            var spec = ValidSpec();
            spec.Terrain.HeightVariation01 = 5f;

            ValidationResult result = _validator.Validate(spec);

            Assert.LessOrEqual(result.RepairedSpecification.Terrain.HeightVariation01, 1f);
        }

        [Test]
        public void Validate_NegativeObjectCount_ClampsToZero()
        {
            var spec = ValidSpec();
            spec.EnvironmentObjects = new List<ObjectSpecification> { new ObjectSpecification { Category = "tree", Count = -50 } };

            ValidationResult result = _validator.Validate(spec);

            Assert.AreEqual(0, result.RepairedSpecification.EnvironmentObjects[0].Count);
        }

        [Test]
        public void Validate_ObjectCountOverLimit_ClampsToLimit()
        {
            var spec = ValidSpec();
            spec.EnvironmentObjects = new List<ObjectSpecification>
            {
                new ObjectSpecification { Category = "tree", Count = WorldGenerationLimits.MaxObjectCountPerCategory * 100 }
            };

            ValidationResult result = _validator.Validate(spec);

            Assert.AreEqual(WorldGenerationLimits.MaxObjectCountPerCategory, result.RepairedSpecification.EnvironmentObjects[0].Count);
        }

        [Test]
        public void Validate_EmptyObjectCategory_DefaultsToMisc()
        {
            var spec = ValidSpec();
            spec.EnvironmentObjects = new List<ObjectSpecification> { new ObjectSpecification { Category = "", Count = 10 } };

            ValidationResult result = _validator.Validate(spec);

            Assert.AreEqual("misc", result.RepairedSpecification.EnvironmentObjects[0].Category);
        }

        [Test]
        public void Validate_TooManyObstacles_TrimsToLimit()
        {
            var spec = ValidSpec();
            spec.Obstacles = new List<ObstacleSpecification>();
            for (int i = 0; i < WorldGenerationLimits.MaxObstacleCount + 50; i++)
                spec.Obstacles.Add(new ObstacleSpecification { Id = $"g{i}", Type = "gate" });

            ValidationResult result = _validator.Validate(spec);

            Assert.AreEqual(WorldGenerationLimits.MaxObstacleCount, result.RepairedSpecification.Obstacles.Count);
        }

        [Test]
        public void Validate_ObstacleWithMissingId_AssignsOne()
        {
            var spec = ValidSpec();
            spec.Obstacles = new List<ObstacleSpecification> { new ObstacleSpecification { Id = null, Type = "gate" } };

            ValidationResult result = _validator.Validate(spec);

            Assert.IsFalse(string.IsNullOrEmpty(result.RepairedSpecification.Obstacles[0].Id));
        }

        [Test]
        public void Validate_UnrecognizedObstacleType_IsWarningNotError_AndPreserved()
        {
            var spec = ValidSpec();
            spec.Obstacles = new List<ObstacleSpecification> { new ObstacleSpecification { Id = "a", Type = "neon_arch" } };

            ValidationResult result = _validator.Validate(spec);

            Assert.IsTrue(result.IsValid, "An unrecognized-but-well-formed obstacle type must not be a validation Error — it's structurally allowed by design (see docs/WORLD_SPECIFICATION.md).");
            Assert.AreEqual("neon_arch", result.RepairedSpecification.Obstacles[0].Type, "Must not silently overwrite an unrecognized-but-valid type.");
            Assert.IsTrue(result.Errors.Exists(e => e.Field == "Obstacles[0].Type" && e.Severity == ValidationSeverity.Warning));
        }

        [Test]
        public void Validate_DegenerateObstacleScale_ClampsToMinimum()
        {
            var spec = ValidSpec();
            spec.Obstacles = new List<ObstacleSpecification>
            {
                new ObstacleSpecification { Id = "a", Type = "gate", Scale = Vector3.zero }
            };

            ValidationResult result = _validator.Validate(spec);

            Vector3 scale = result.RepairedSpecification.Obstacles[0].Scale;
            Assert.GreaterOrEqual(scale.x, WorldGenerationLimits.MinObstacleScaleComponent);
            Assert.GreaterOrEqual(scale.y, WorldGenerationLimits.MinObstacleScaleComponent);
            Assert.GreaterOrEqual(scale.z, WorldGenerationLimits.MinObstacleScaleComponent);
        }

        [Test]
        public void Validate_NaNObstaclePosition_RepairsToOrigin()
        {
            var spec = ValidSpec();
            spec.Obstacles = new List<ObstacleSpecification>
            {
                new ObstacleSpecification { Id = "a", Type = "gate", Position = new Vector3(float.NaN, 0f, 0f) }
            };

            ValidationResult result = _validator.Validate(spec);

            Assert.IsFalse(float.IsNaN(result.RepairedSpecification.Obstacles[0].Position.x));
        }

        [Test]
        public void Validate_NaNSpawnPosition_RepairsToSafeDefault()
        {
            var spec = ValidSpec();
            spec.Spawn.Position = new Vector3(float.NaN, float.PositiveInfinity, 0f);

            ValidationResult result = _validator.Validate(spec);

            Assert.IsTrue(result.IsValid);
            Vector3 repaired = result.RepairedSpecification.Spawn.Position;
            Assert.IsFalse(float.IsNaN(repaired.x) || float.IsInfinity(repaired.y));
        }

        [Test]
        public void Validate_NegativeCheckpointIndex_ClearsToNull()
        {
            var spec = ValidSpec();
            spec.Obstacles = new List<ObstacleSpecification>
            {
                new ObstacleSpecification { Id = "a", Type = "checkpoint", CheckpointIndex = -3 }
            };

            ValidationResult result = _validator.Validate(spec);

            Assert.IsNull(result.RepairedSpecification.Obstacles[0].CheckpointIndex);
        }

        [Test]
        public void Validate_TimeOfDayOutOfRange_ClampsToValidRange()
        {
            var spec = ValidSpec();
            spec.Lighting.TimeOfDayHours = 40f;

            ValidationResult result = _validator.Validate(spec);

            Assert.LessOrEqual(result.RepairedSpecification.Lighting.TimeOfDayHours, 24f);
        }

        [Test]
        public void Validate_FlightScoresOutOfRange_ClampToZeroOne()
        {
            var spec = ValidSpec();
            spec.Flight.TightnessScore01 = -5f;
            spec.Flight.ObstacleDensity01 = 99f;

            ValidationResult result = _validator.Validate(spec);

            Assert.GreaterOrEqual(result.RepairedSpecification.Flight.TightnessScore01, 0f);
            Assert.LessOrEqual(result.RepairedSpecification.Flight.ObstacleDensity01, 1f);
        }

        [Test]
        public void Validate_NullCourse_RepairsToDefault_Warning()
        {
            var spec = ValidSpec();
            spec.Course = null;

            ValidationResult result = _validator.Validate(spec);

            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(result.RepairedSpecification.Course);
            Assert.IsTrue(result.Errors.Exists(e => e.Field == "Course" && e.Severity == ValidationSeverity.Warning));
        }

        [Test]
        public void Validate_NegativeGateCount_ClampsToZero()
        {
            var spec = ValidSpec();
            spec.Course.GateCount = -5;

            ValidationResult result = _validator.Validate(spec);

            Assert.AreEqual(0, result.RepairedSpecification.Course.GateCount);
        }

        [Test]
        public void Validate_RichCourseIntent_SurvivesValidationUnchanged()
        {
            var spec = ValidSpec();
            spec.Course.Style = "technical_then_high_speed";
            spec.Course.Difficulty = "hard";
            spec.Course.GateCount = 15;
            spec.Course.SectionDescriptions = new List<string> { "technical and tight", "opens into a high-speed valley" };

            ValidationResult result = _validator.Validate(spec);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual("technical_then_high_speed", result.RepairedSpecification.Course.Style);
            Assert.AreEqual(15, result.RepairedSpecification.Course.GateCount);
            Assert.AreEqual(2, result.RepairedSpecification.Course.SectionDescriptions.Count);
        }

        [Test]
        public void Validate_RepairsMutateAndReturnTheSameInstance()
        {
            var spec = ValidSpec();
            spec.Terrain.Width = -1f;

            ValidationResult result = _validator.Validate(spec);

            Assert.AreSame(spec, result.RepairedSpecification, "Repairs are applied in place — documented behaviour, not an accident.");
        }
    }
}
