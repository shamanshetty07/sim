using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI;
using Sim.WorldGeneration.Models;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Deliberately never touches real credentials or the network — a fake
    /// IReactorCredentialsProvider is injected so this suite's result is identical whether or
    /// not the machine running it happens to have a real .env.local configured (it does, on
    /// the machine this was developed on — that must not change test behaviour).
    /// </summary>
    public class OpenWorldReactorWorldGenerationServiceTests
    {
        private sealed class NoCredentialsProvider : IReactorCredentialsProvider
        {
            public bool TryGetApiKey(out string apiKey) { apiKey = null; return false; }
            public bool TryGetModel(out string model) { model = null; return false; }
        }

        [Test]
        public async Task GenerateWorldAsync_NoCredentials_ReturnsNotConfigured_DoesNotThrow()
        {
            var service = new OpenWorldReactorWorldGenerationService(new NoCredentialsProvider());
            var request = new WorldGenerationRequest("Create a futuristic city.");

            WorldGenerationOutcome outcome = await service.GenerateWorldAsync(request);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldGenerationFailureReason.NotConfigured, outcome.FailureReason);
        }

        [Test]
        public async Task GenerateWorldAsync_NullRequest_ReturnsInvalidResponse_DoesNotThrow()
        {
            var service = new OpenWorldReactorWorldGenerationService(new NoCredentialsProvider());

            WorldGenerationOutcome outcome = await service.GenerateWorldAsync(null);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(WorldGenerationFailureReason.InvalidResponse, outcome.FailureReason);
        }

        [Test]
        public async Task MintSessionTokenAsync_NoCredentials_ThrowsReactorNotConfiguredException()
        {
            // The lower-level entry point still throws (it's a "cannot proceed at all" guard,
            // the same category as argument validation — see the exception's own remarks and
            // OpenWorldReactorWorldGenerationService's class remarks). GenerateWorldAsync is
            // the one that must never let this escape uncaught, covered above.
            var service = new OpenWorldReactorWorldGenerationService(new NoCredentialsProvider());

            await AsyncAssert.ThrowsAsync<ReactorNotConfiguredException>(() =>
                service.MintSessionTokenAsync("reactor/lingbot-world-2"));
        }
    }
}
