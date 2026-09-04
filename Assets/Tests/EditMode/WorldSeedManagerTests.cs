using NUnit.Framework;
using Sim.WorldGeneration;

namespace Sim.Tests.EditMode
{
    public class WorldSeedManagerTests
    {
        [Test]
        public void SameMasterSeedAndStage_ProducesSameSequence()
        {
            var a = new WorldSeedManager(12345);
            var b = new WorldSeedManager(12345);

            System.Random rngA = a.GetRandomForStage("terrain");
            System.Random rngB = b.GetRandomForStage("terrain");

            for (int i = 0; i < 20; i++)
                Assert.AreEqual(rngA.Next(), rngB.Next());
        }

        [Test]
        public void DifferentStages_ProduceDifferentSequences()
        {
            var manager = new WorldSeedManager(12345);

            System.Random terrainRng = manager.GetRandomForStage("terrain");
            System.Random environmentRng = manager.GetRandomForStage("environment");

            Assert.AreNotEqual(terrainRng.Next(), environmentRng.Next());
        }

        [Test]
        public void DifferentMasterSeeds_ProduceDifferentSequences_ForSameStage()
        {
            var a = new WorldSeedManager(1);
            var b = new WorldSeedManager(2);

            Assert.AreNotEqual(a.GetRandomForStage("terrain").Next(), b.GetRandomForStage("terrain").Next());
        }

        [Test]
        public void RepeatedCallsForSameStage_EachReturnAFreshIndependentStream_ButIdenticalToEachOther()
        {
            var manager = new WorldSeedManager(999);

            System.Random first = manager.GetRandomForStage("obstacles");
            System.Random second = manager.GetRandomForStage("obstacles");

            // Two independent Random instances seeded identically produce the same sequence —
            // this is what lets a stage be "replayed" deterministically if ever needed.
            Assert.AreEqual(first.Next(), second.Next());
        }
    }
}
