using NUnit.Framework;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Persistence;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>WorldSaveValidator never duplicates WorldSpecificationValidator's own rules — these tests confirm it (a) rejects save-envelope-level problems that validator can't see, and (b) still folds a real WorldSpecificationValidator pass in, so a save can never bypass it.</summary>
    public class WorldSaveValidatorTests
    {
        private static WorldSpecification ValidSpecification() => new WorldSpecification
        {
            OriginalPrompt = "Create a small test course.",
            Seed = 42,
            Terrain = new TerrainSpecification { TerrainType = "hills", Width = 200f, Depth = 200f, MaxHeight = 40f },
            Spawn = new SpawnSpecification { Position = new Vector3(0f, 25f, 0f) }
        };

        private static WorldSaveData ValidSaveData() => WorldSaveData.FromSpecification(ValidSpecification());

        [Test]
        public void Validate_NullData_Fails()
        {
            Assert.IsFalse(WorldSaveValidator.Validate(null).Success);
        }

        [Test]
        public void Validate_ValidData_Succeeds()
        {
            WorldLoadValidationResult result = WorldSaveValidator.Validate(ValidSaveData());
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.ValidatedSpecification);
        }

        [Test]
        public void Validate_UnsupportedVersion_Fails()
        {
            WorldSaveData data = ValidSaveData();
            data.Version = WorldSaveData.CurrentVersion + 1;

            Assert.IsFalse(WorldSaveValidator.Validate(data).Success);
        }

        [Test]
        public void Validate_MissingPrompt_Fails()
        {
            WorldSaveData data = ValidSaveData();
            data.Prompt = "";

            Assert.IsFalse(WorldSaveValidator.Validate(data).Success);
        }

        [Test]
        public void Validate_PromptTooLong_Fails()
        {
            WorldSaveData data = ValidSaveData();
            data.Prompt = new string('a', WorldSaveValidator.MaxPromptLength + 1);
            data.Specification.OriginalPrompt = data.Prompt;

            Assert.IsFalse(WorldSaveValidator.Validate(data).Success);
        }

        [Test]
        public void Validate_MissingSpecification_Fails()
        {
            WorldSaveData data = ValidSaveData();
            data.Specification = null;

            Assert.IsFalse(WorldSaveValidator.Validate(data).Success);
        }

        [Test]
        public void Validate_PromptDoesNotMatchSpecification_Fails()
        {
            WorldSaveData data = ValidSaveData();
            data.Prompt = "a different prompt entirely";

            Assert.IsFalse(WorldSaveValidator.Validate(data).Success);
        }

        [Test]
        public void Validate_SeedDoesNotMatchSpecification_Fails()
        {
            WorldSaveData data = ValidSaveData();
            data.Seed = data.Specification.Seed + 1;

            Assert.IsFalse(WorldSaveValidator.Validate(data).Success);
        }

        [Test]
        public void Validate_InvalidSpecification_FailsViaExistingValidator()
        {
            // Missing OriginalPrompt is the one genuinely unrecoverable WorldSpecificationValidator
            // error (Sim.WorldGeneration.Validation) — proves WorldSaveValidator actually delegates
            // to it rather than only checking its own envelope-level rules.
            WorldSaveData data = ValidSaveData();
            data.Specification.OriginalPrompt = "";
            data.Prompt = ""; // keep the envelope/specification prompt consistent so only the underlying validator rejects this

            WorldLoadValidationResult result = WorldSaveValidator.Validate(data);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Validate_UsesInjectedSpecificationValidator()
        {
            var alwaysRejects = new AlwaysRejectingValidator();
            WorldLoadValidationResult result = WorldSaveValidator.Validate(ValidSaveData(), alwaysRejects);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(alwaysRejects.WasCalled);
        }

        [Test]
        public void Validate_ReturnsTheRepairedSpecification_NotTheRawOne()
        {
            WorldSaveData data = ValidSaveData();
            data.Specification.Terrain.HeightVariation01 = 5f; // out of [0,1] — WorldSpecificationValidator repairs this by clamping

            WorldLoadValidationResult result = WorldSaveValidator.Validate(data);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1f, result.ValidatedSpecification.Terrain.HeightVariation01);
        }

        private sealed class AlwaysRejectingValidator : IWorldSpecificationValidator
        {
            public bool WasCalled { get; private set; }

            public ValidationResult Validate(WorldSpecification specification)
            {
                WasCalled = true;
                return new ValidationResult
                {
                    Errors = new System.Collections.Generic.List<ValidationError>
                    {
                        new ValidationError("(test)", "Always rejects.", ValidationSeverity.Error)
                    },
                    RepairedSpecification = null
                };
            }
        }
    }
}
