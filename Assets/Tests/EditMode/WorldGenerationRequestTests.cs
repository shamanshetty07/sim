using System;
using NUnit.Framework;
using Sim.WorldGeneration.Models;

namespace Sim.Tests.EditMode
{
    public class WorldGenerationRequestTests
    {
        [Test]
        public void Constructor_PreservesPromptVerbatim()
        {
            const string prompt = "Create a huge mountain FPV course with waterfalls, cliffs, forests, tunnels, abandoned buildings and 20 racing gates.";
            var request = new WorldGenerationRequest(prompt);

            Assert.AreEqual(prompt, request.Prompt, "The prompt must be preserved exactly — it is the primary input to world generation.");
        }

        [Test]
        public void Constructor_NullPrompt_Throws()
        {
            Assert.Throws<ArgumentException>(() => new WorldGenerationRequest(null));
        }

        [Test]
        public void Constructor_EmptyPrompt_Throws()
        {
            Assert.Throws<ArgumentException>(() => new WorldGenerationRequest(""));
        }

        [Test]
        public void Constructor_WhitespaceOnlyPrompt_Throws()
        {
            Assert.Throws<ArgumentException>(() => new WorldGenerationRequest("   "));
        }

        [Test]
        public void Constructor_NoRequestIdGiven_GeneratesOne()
        {
            var request = new WorldGenerationRequest("test prompt");
            Assert.AreNotEqual(Guid.Empty, request.RequestId);
        }

        [Test]
        public void Constructor_ExplicitRequestId_IsPreserved()
        {
            Guid id = Guid.NewGuid();
            var request = new WorldGenerationRequest("test prompt", requestId: id);
            Assert.AreEqual(id, request.RequestId);
        }

        [Test]
        public void Constructor_SeedIsOptional_DefaultsToNull()
        {
            var request = new WorldGenerationRequest("test prompt");
            Assert.IsNull(request.Seed);
        }

        [Test]
        public void Constructor_ExplicitSeed_IsPreserved()
        {
            var request = new WorldGenerationRequest("test prompt", seed: 12345);
            Assert.AreEqual(12345, request.Seed);
        }

        [Test]
        public void Constructor_TwoRequestsWithSamePrompt_GetDifferentRequestIds()
        {
            var a = new WorldGenerationRequest("same prompt");
            var b = new WorldGenerationRequest("same prompt");
            Assert.AreNotEqual(a.RequestId, b.RequestId);
        }
    }
}
