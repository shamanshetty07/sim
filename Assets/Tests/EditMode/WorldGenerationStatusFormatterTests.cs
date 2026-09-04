using NUnit.Framework;
using Sim.Core;
using Sim.UI;

namespace Sim.Tests.EditMode
{
    public class WorldGenerationStatusFormatterTests
    {
        [TestCase(WorldGenerationState.Idle, "Enter a world description.")]
        [TestCase(WorldGenerationState.Designing, "Designing world...")]
        [TestCase(WorldGenerationState.Validating, "Validating world specification...")]
        [TestCase(WorldGenerationState.Generating, "Generating Unity world...")]
        [TestCase(WorldGenerationState.Ready, "World ready — fly!")]
        [TestCase(WorldGenerationState.Cancelled, "Generation cancelled.")]
        public void Format_KnownStates_MatchExpectedText(WorldGenerationState state, string expected)
        {
            Assert.AreEqual(expected, WorldGenerationStatusFormatter.Format(state, null));
        }

        [Test]
        public void Format_Failed_WithMessage_IncludesMessage()
        {
            string result = WorldGenerationStatusFormatter.Format(WorldGenerationState.Failed, "Terrain size must be greater than 0.");
            Assert.AreEqual("Generation failed: Terrain size must be greater than 0.", result);
        }

        [Test]
        public void Format_Failed_WithoutMessage_UsesGenericText()
        {
            Assert.AreEqual("Generation failed.", WorldGenerationStatusFormatter.Format(WorldGenerationState.Failed, null));
        }

        [TestCase(WorldGenerationState.Idle, true)]
        [TestCase(WorldGenerationState.Designing, false)]
        [TestCase(WorldGenerationState.Validating, false)]
        [TestCase(WorldGenerationState.Generating, false)]
        [TestCase(WorldGenerationState.Ready, true)]
        [TestCase(WorldGenerationState.Failed, true)]
        [TestCase(WorldGenerationState.Cancelled, true)]
        public void IsGenerateAvailable_OnlyFalseWhileBusy(WorldGenerationState state, bool expected)
        {
            Assert.AreEqual(expected, WorldGenerationStatusFormatter.IsGenerateAvailable(state));
        }

        [TestCase(WorldGenerationState.Designing, true)]
        [TestCase(WorldGenerationState.Generating, true)]
        [TestCase(WorldGenerationState.Ready, false)]
        [TestCase(WorldGenerationState.Idle, false)]
        public void IsCancelAvailable_IsExactOppositeOfGenerateAvailable(WorldGenerationState state, bool expected)
        {
            Assert.AreEqual(expected, WorldGenerationStatusFormatter.IsCancelAvailable(state));
        }

        [TestCase(WorldGenerationState.Ready, true)]
        [TestCase(WorldGenerationState.Failed, true)]
        [TestCase(WorldGenerationState.Cancelled, true)]
        [TestCase(WorldGenerationState.Idle, false)]
        [TestCase(WorldGenerationState.Designing, false)]
        [TestCase(WorldGenerationState.Generating, false)]
        public void IsClearAvailable_OnlyForTerminalStatesWithSomethingToClose(WorldGenerationState state, bool expected)
        {
            Assert.AreEqual(expected, WorldGenerationStatusFormatter.IsClearAvailable(state));
        }
    }
}
