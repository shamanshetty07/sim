using NUnit.Framework;
using Sim.AI;
using Sim.WorldGeneration.Models;

namespace Sim.Tests.EditMode
{
    public class WorldGenerationOutcomeTests
    {
        [Test]
        public void Succeeded_SetsSuccessTrue_AndCarriesResult()
        {
            var result = new ReactorWorldResult { WorldName = "X", Seed = 1 };
            WorldGenerationOutcome outcome = WorldGenerationOutcome.Succeeded(result);

            Assert.IsTrue(outcome.Success);
            Assert.AreSame(result, outcome.Result);
            Assert.AreEqual(WorldGenerationFailureReason.None, outcome.FailureReason);
        }

        [Test]
        public void Failed_SetsSuccessFalse_AndCarriesReasonAndMessage()
        {
            WorldGenerationOutcome outcome = WorldGenerationOutcome.Failed(WorldGenerationFailureReason.NetworkError, "connection refused");

            Assert.IsFalse(outcome.Success);
            Assert.IsNull(outcome.Result);
            Assert.AreEqual(WorldGenerationFailureReason.NetworkError, outcome.FailureReason);
            Assert.AreEqual("connection refused", outcome.ErrorMessage);
        }
    }
}
