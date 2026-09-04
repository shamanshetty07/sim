using System;
using NUnit.Framework;
using Sim.WorldGeneration.Adapters;
using Sim.WorldGeneration.Models;

namespace Sim.Tests.EditMode
{
    public class ReactorWorldAdapterTests
    {
        private ReactorWorldAdapter _adapter;
        private WorldGenerationRequest _request;

        [SetUp]
        public void SetUp()
        {
            _adapter = new ReactorWorldAdapter();
            _request = new WorldGenerationRequest("Create a mountain FPV course with cliffs and rocks.", seed: 42);
        }

        [Test]
        public void Adapt_NullResult_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _adapter.Adapt(null, _request));
        }

        [Test]
        public void Adapt_NullRequest_Throws()
        {
            var result = new ReactorWorldResult { WorldName = "X", Seed = 1 };
            Assert.Throws<ArgumentNullException>(() => _adapter.Adapt(result, null));
        }

        [Test]
        public void Adapt_PreservesOriginalPromptFromRequest_NotFromResult()
        {
            var result = new ReactorWorldResult { WorldName = "Some World", Seed = 1 };
            WorldSpecification spec = _adapter.Adapt(result, _request);

            Assert.AreEqual(_request.Prompt, spec.OriginalPrompt);
        }

        [Test]
        public void Adapt_CopiesNameDescriptionSeedMetadata()
        {
            var metadata = new WorldGenerationMetadata { ProviderName = "Mock" };
            var result = new ReactorWorldResult
            {
                WorldName = "Cliffside Course",
                Description = "A test description",
                Seed = 999,
                Metadata = metadata
            };

            WorldSpecification spec = _adapter.Adapt(result, _request);

            Assert.AreEqual("Cliffside Course", spec.WorldName);
            Assert.AreEqual("A test description", spec.Description);
            Assert.AreEqual(999, spec.Seed);
            Assert.AreSame(metadata, spec.Metadata);
        }

        [Test]
        public void Adapt_MissingWorldName_FallsBackToDefault()
        {
            var result = new ReactorWorldResult { WorldName = null, Seed = 1 };
            WorldSpecification spec = _adapter.Adapt(result, _request);
            Assert.AreEqual("Generated World", spec.WorldName);
        }

        [Test]
        public void Adapt_UsesRequestedScaleFromRequest()
        {
            var request = new WorldGenerationRequest("prompt", requestedScale: WorldScale.Huge);
            var result = new ReactorWorldResult { WorldName = "X", Seed = 1 };

            WorldSpecification spec = _adapter.Adapt(result, request);

            Assert.AreEqual(WorldScale.Huge, spec.Scale);
        }

        [Test]
        public void Adapt_NoRequestedScale_DefaultsToMedium()
        {
            var result = new ReactorWorldResult { WorldName = "X", Seed = 1 };
            WorldSpecification spec = _adapter.Adapt(result, _request);
            Assert.AreEqual(WorldScale.Medium, spec.Scale);
        }

        [TestCase(ReactorWorldPayloadKind.Unknown)]
        [TestCase(ReactorWorldPayloadKind.StructuredData)]
        [TestCase(ReactorWorldPayloadKind.NativeSceneReference)]
        public void Adapt_AnyPayloadKind_NeverThrows_AndReturnsUsableDefaults(ReactorWorldPayloadKind kind)
        {
            var result = new ReactorWorldResult
            {
                WorldName = "X",
                Seed = 1,
                PayloadKind = kind,
                StructuredPayloadJson = "{}",
                NativeAssetReference = "some://reference"
            };

            WorldSpecification spec = null;
            Assert.DoesNotThrow(() => spec = _adapter.Adapt(result, _request));

            Assert.IsNotNull(spec.Terrain);
            Assert.IsNotNull(spec.Weather);
            Assert.IsNotNull(spec.Lighting);
            Assert.IsNotNull(spec.Spawn);
            Assert.IsNotNull(spec.Flight);
            Assert.IsNotNull(spec.EnvironmentObjects);
            Assert.IsNotNull(spec.Obstacles);
        }
    }
}
