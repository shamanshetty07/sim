using NUnit.Framework;
using Sim.WorldGeneration.Validation;

namespace Sim.Tests.EditMode
{
    public class ValidationResultTests
    {
        [Test]
        public void IsValid_NoErrors_IsTrue()
        {
            var result = new ValidationResult();
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void IsValid_OnlyWarnings_IsTrue()
        {
            var result = new ValidationResult();
            result.Errors.Add(new ValidationError("Terrain.Width", "clamped to max", ValidationSeverity.Warning));

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void IsValid_AnyError_IsFalse()
        {
            var result = new ValidationResult();
            result.Errors.Add(new ValidationError("Terrain.Width", "clamped to max", ValidationSeverity.Warning));
            result.Errors.Add(new ValidationError("Seed", "negative seed is invalid", ValidationSeverity.Error));

            Assert.IsFalse(result.IsValid);
        }
    }
}
