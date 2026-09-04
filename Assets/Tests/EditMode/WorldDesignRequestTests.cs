using System;
using NUnit.Framework;
using Sim.AI.WorldDesign;

namespace Sim.Tests.EditMode
{
    public class WorldDesignRequestTests
    {
        [Test]
        public void Constructor_PreservesPromptVerbatim()
        {
            const string prompt = "Create a huge cinematic FPV racing course through a Himalayan mountain valley with pine forests, waterfalls, abandoned cabins, cliffs, tunnels and 15 gates. Make the first section technical and tight, then open into a high-speed valley.";
            var request = new WorldDesignRequest(prompt);

            Assert.AreEqual(prompt, request.Prompt);
        }

        [Test]
        public void Constructor_NullPrompt_Throws()
        {
            Assert.Throws<ArgumentException>(() => new WorldDesignRequest(null));
        }

        [Test]
        public void Constructor_WhitespaceOnlyPrompt_Throws()
        {
            Assert.Throws<ArgumentException>(() => new WorldDesignRequest("   "));
        }

        [Test]
        public void Constructor_NoRequestIdGiven_GeneratesOne()
        {
            var request = new WorldDesignRequest("test prompt");
            Assert.AreNotEqual(Guid.Empty, request.RequestId);
        }

        [Test]
        public void Constructor_SeedIsOptional_DefaultsToNull()
        {
            var request = new WorldDesignRequest("test prompt");
            Assert.IsNull(request.Seed);
        }

        [Test]
        public void Constructor_ExplicitSeed_IsPreserved()
        {
            var request = new WorldDesignRequest("test prompt", seed: 42);
            Assert.AreEqual(42, request.Seed);
        }

        [Test]
        public void Constructor_ConstraintsAreOptional_DefaultsToNull()
        {
            var request = new WorldDesignRequest("test prompt");
            Assert.IsNull(request.Constraints);
        }

        [Test]
        public void Constructor_ExplicitConstraints_ArePreserved()
        {
            var constraints = new WorldDesignConstraints { MaxObstacles = 10 };
            var request = new WorldDesignRequest("test prompt", constraints: constraints);
            Assert.AreSame(constraints, request.Constraints);
        }
    }
}
