using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI.WorldDesign;

namespace Sim.Tests.EditMode
{
    public class MockWorldDesignerTests
    {
        private MockWorldDesigner _designer;

        [SetUp]
        public void SetUp() => _designer = new MockWorldDesigner();

        [Test]
        public async Task DesignWorldAsync_ValidRequest_Succeeds_ReturnsRichSpecification()
        {
            var request = new WorldDesignRequest("Create a mountain FPV course.");
            WorldDesignOutcome outcome = await _designer.DesignWorldAsync(request);

            Assert.IsTrue(outcome.Success);
            Assert.IsNotNull(outcome.Specification);
            Assert.Greater(outcome.Specification.EnvironmentObjects.Count, 0, "Mock should return a richly-populated example, not a mostly-empty stub.");
            Assert.Greater(outcome.Specification.Obstacles.Count, 0);
            Assert.IsNotNull(outcome.Specification.Course);
        }

        [Test]
        public async Task DesignWorldAsync_NullRequest_FailsGracefully_DoesNotThrow()
        {
            WorldDesignOutcome outcome = await _designer.DesignWorldAsync(null);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldDesignFailureReason.InvalidResponse, outcome.FailureReason);
        }

        [Test]
        public async Task DesignWorldAsync_PreservesOriginalPrompt()
        {
            const string prompt = "Create a desert canyon FPV racing course with tunnels, large rocks and 12 gates.";
            var request = new WorldDesignRequest(prompt);
            WorldDesignOutcome outcome = await _designer.DesignWorldAsync(request);

            Assert.AreEqual(prompt, outcome.Specification.OriginalPrompt);
        }

        [Test]
        public async Task DesignWorldAsync_ExplicitSeed_IsUsed()
        {
            var request = new WorldDesignRequest("prompt", seed: 999);
            WorldDesignOutcome outcome = await _designer.DesignWorldAsync(request);

            Assert.AreEqual(999, outcome.Specification.Seed);
        }

        [Test]
        public async Task DesignWorldAsync_SameSeed_ProducesSameOutputSeed()
        {
            var requestA = new WorldDesignRequest("Create a mountain course.", seed: 123);
            var requestB = new WorldDesignRequest("Create a different-worded prompt entirely.", seed: 123);

            WorldDesignOutcome a = await _designer.DesignWorldAsync(requestA);
            WorldDesignOutcome b = await _designer.DesignWorldAsync(requestB);

            Assert.AreEqual(a.Specification.Seed, b.Specification.Seed, "An explicit seed must produce the same seed regardless of prompt wording.");
        }

        [Test]
        public async Task DesignWorldAsync_NoSeedGiven_SamePromptProducesSameSeed()
        {
            var requestA = new WorldDesignRequest("Create a desert canyon FPV racing course.");
            var requestB = new WorldDesignRequest("Create a desert canyon FPV racing course.");

            WorldDesignOutcome a = await _designer.DesignWorldAsync(requestA);
            WorldDesignOutcome b = await _designer.DesignWorldAsync(requestB);

            Assert.AreEqual(a.Specification.Seed, b.Specification.Seed);
        }

        [Test]
        public async Task DesignWorldAsync_RequestedScale_IsHonored()
        {
            var request = new WorldDesignRequest("prompt", constraints: new WorldDesignConstraints
            {
                PreferredScale = Sim.WorldGeneration.Models.WorldScale.Small
            });

            WorldDesignOutcome outcome = await _designer.DesignWorldAsync(request);

            Assert.AreEqual(Sim.WorldGeneration.Models.WorldScale.Small, outcome.Specification.Scale);
        }
    }
}
