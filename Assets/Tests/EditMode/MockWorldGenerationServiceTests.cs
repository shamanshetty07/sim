using System.Threading;
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

        [Test]
        public async Task GenerateWorldAsync_SameExplicitSeed_ProducesSameResultSeed_TwiceInARow()
        {
            var requestA = new WorldGenerationRequest("Create a mountain course.", seed: 555);
            var requestB = new WorldGenerationRequest("Create a mountain course.", seed: 555);

            WorldGenerationOutcome a = await _service.GenerateWorldAsync(requestA);
            WorldGenerationOutcome b = await _service.GenerateWorldAsync(requestB);

            Assert.AreEqual(a.Result.Seed, b.Result.Seed);
        }

        [Test]
        public async Task GenerateWorldAsync_NoSeedGiven_SamePromptProducesSameSeed()
        {
            var requestA = new WorldGenerationRequest("Create a desert canyon FPV racing course.");
            var requestB = new WorldGenerationRequest("Create a desert canyon FPV racing course.");

            WorldGenerationOutcome a = await _service.GenerateWorldAsync(requestA);
            WorldGenerationOutcome b = await _service.GenerateWorldAsync(requestB);

            Assert.AreEqual(a.Result.Seed, b.Result.Seed, "Same prompt with no explicit seed should deterministically derive the same seed.");
        }

        [Test]
        public async Task GenerateWorldAsync_NoSeedGiven_DifferentPromptProducesDifferentSeed()
        {
            var requestA = new WorldGenerationRequest("Create a desert canyon course.");
            var requestB = new WorldGenerationRequest("Create a mountain forest course.");

            WorldGenerationOutcome a = await _service.GenerateWorldAsync(requestA);
            WorldGenerationOutcome b = await _service.GenerateWorldAsync(requestB);

            Assert.AreNotEqual(a.Result.Seed, b.Result.Seed);
        }

        [Test]
        public async Task GenerateWorldAsync_Cancellation_ThrowsOperationCanceledException()
        {
            _service.SimulatedDelayMilliseconds = 5000; // long enough that the cancel below always wins
            var request = new WorldGenerationRequest("prompt");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await AsyncAssert.ThrowsAsync<TaskCanceledException>(() => _service.GenerateWorldAsync(request, cts.Token));
        }

        [Test]
        public async Task GenerateWorldAsync_ZeroDelay_CompletesWithoutWaiting()
        {
            _service.SimulatedDelayMilliseconds = 0;
            var request = new WorldGenerationRequest("prompt");

            WorldGenerationOutcome outcome = await _service.GenerateWorldAsync(request);

            Assert.IsTrue(outcome.Success);
        }
    }
}
