using NUnit.Framework;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Persistence;

namespace Sim.Tests.EditMode
{
    /// <summary>WorldSaveData.FromSpecification is the only supported construction path — these tests confirm Prompt/Seed/Metadata always mirror Specification exactly, so the two can never independently drift.</summary>
    public class WorldSaveDataTests
    {
        private static WorldSpecification MinimalSpecification() => new WorldSpecification
        {
            OriginalPrompt = "Create a small test course.",
            Seed = 42,
            Metadata = new WorldGenerationMetadata { ProviderName = "Mock" }
        };

        [Test]
        public void FromSpecification_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => WorldSaveData.FromSpecification(null));
        }

        [Test]
        public void FromSpecification_MirrorsPromptExactly()
        {
            WorldSpecification spec = MinimalSpecification();
            WorldSaveData data = WorldSaveData.FromSpecification(spec);

            Assert.AreEqual(spec.OriginalPrompt, data.Prompt);
        }

        [Test]
        public void FromSpecification_MirrorsSeedExactly()
        {
            WorldSpecification spec = MinimalSpecification();
            WorldSaveData data = WorldSaveData.FromSpecification(spec);

            Assert.AreEqual(spec.Seed, data.Seed);
        }

        [Test]
        public void FromSpecification_CarriesTheSameSpecificationInstance()
        {
            WorldSpecification spec = MinimalSpecification();
            WorldSaveData data = WorldSaveData.FromSpecification(spec);

            Assert.AreSame(spec, data.Specification);
        }

        [Test]
        public void FromSpecification_CarriesTheSameMetadataInstance()
        {
            WorldSpecification spec = MinimalSpecification();
            WorldSaveData data = WorldSaveData.FromSpecification(spec);

            Assert.AreSame(spec.Metadata, data.Metadata);
        }

        [Test]
        public void FromSpecification_WritesCurrentVersion()
        {
            WorldSaveData data = WorldSaveData.FromSpecification(MinimalSpecification());
            Assert.AreEqual(WorldSaveData.CurrentVersion, data.Version);
        }
    }
}
