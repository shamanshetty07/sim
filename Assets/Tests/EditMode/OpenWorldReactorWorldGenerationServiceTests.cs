using NUnit.Framework;
using Sim.AI;
using Sim.WorldGeneration.Models;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Confirms the stub fails loudly and explicitly rather than silently returning a fake
    /// result — this is the behaviour the rest of the pipeline (and a caller deciding whether
    /// to fall back to Mock) depends on until real OpenWorld Reactor access exists.
    /// </summary>
    public class OpenWorldReactorWorldGenerationServiceTests
    {
        [Test]
        public void GenerateWorldAsync_ThrowsReactorNotConfiguredException()
        {
            var service = new OpenWorldReactorWorldGenerationService();
            var request = new WorldGenerationRequest("Create a futuristic city.");

            Assert.ThrowsAsync<ReactorNotConfiguredException>(async () => await service.GenerateWorldAsync(request));
        }
    }
}
