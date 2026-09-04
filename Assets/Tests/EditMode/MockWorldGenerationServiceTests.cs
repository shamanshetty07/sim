using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI;
using Sim.WorldGeneration.Models;

namespace Sim.Tests.EditMode
{
    public class MockWorldGenerationServiceTests
    {
        private MockWorldGenerationService _service;

        [SetUp]
        public void SetUp() => _service = new MockWorldGenerationService();

        [Test]
        public async Task GenerateWorldAsync_ValidRequest_Succeeds()
        {
            var request = new WorldGenerationRequest("Create a desert canyon environment.");
            WorldGenerationOutcome outcome = await _service.GenerateWorldAsync(request);

            Assert.IsTrue(outcome.Success);
            Assert.IsNotNull(outcome.Result);
        }

        [Test]
        public async Task GenerateWorldAsync_NullRequest_FailsGracefully_DoesNotThrow()
        {
            WorldGenerationOutcome outcome = await _service.GenerateWorldAsync(null);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldGenerationFailureReason.InvalidResponse, outcome.FailureReason);
        }

        [Test]
        public async Task GenerateWorldAsync_ExplicitSeed_IsEchoedBack()
        {
            var request = new WorldGenerationRequest("prompt", seed: 777);
            WorldGenerationOutcome outcome = await _service.GenerateWorldAsync(request);

            Assert.AreEqual(777, outcome.Result.Seed);
        }

        [Test]
        public async Task GenerateWorldAsync_MetadataEchoesRequestId()
        {
            var request = new WorldGenerationRequest("prompt");
            WorldGenerationOutcome outcome = await _service.GenerateWorldAsync(request);

            Assert.AreEqual(request.RequestId, outcome.Result.Metadata.RequestId);
        }

        [Test]
        public async Task GenerateWorldAsync_ProviderNameIsMock()
        {
            var request = new WorldGenerationRequest("prompt");
            WorldGenerationOutcome outcome = await _service.GenerateWorldAsync(request);

            Assert.AreEqual("Mock", outcome.Result.Metadata.ProviderName);
        }
    }
}
