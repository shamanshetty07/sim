using NUnit.Framework;
using Sim.AI.WorldDesign;
using Sim.WorldGeneration.Models;

namespace Sim.Tests.EditMode
{
    public class WorldSpecificationJsonParserTests
    {
        private WorldSpecificationJsonParser _parser;
        private WorldDesignRequest _request;

        [SetUp]
        public void SetUp()
        {
            _parser = new WorldSpecificationJsonParser();
            _request = new WorldDesignRequest("Create a mountain FPV course with cliffs and rocks.", seed: 42);
        }

        [Test]
        public void TryParse_ValidJson_Succeeds()
        {
            const string json = "{\"WorldName\":\"Test World\",\"Description\":\"A test.\"}";
            WorldSpecificationParseResult result = _parser.TryParse(json, _request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("Test World", result.Specification.WorldName);
        }

        [Test]
        public void TryParse_MalformedJson_Fails_DoesNotThrow()
        {
            const string malformed = "{ this is not valid json at all";
            WorldSpecificationParseResult result = null;

            Assert.DoesNotThrow(() => result = _parser.TryParse(malformed, _request));
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.ErrorMessage);
        }

        [Test]
        public void TryParse_EmptyString_Fails()
        {
            WorldSpecificationParseResult result = _parser.TryParse("", _request);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void TryParse_NullText_Fails_DoesNotThrow()
        {
            WorldSpecificationParseResult result = null;
            Assert.DoesNotThrow(() => result = _parser.TryParse(null, _request));
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void TryParse_LiteralJsonNull_Fails()
        {
            WorldSpecificationParseResult result = _parser.TryParse("null", _request);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void TryParse_NullOriginalRequest_Fails_DoesNotThrow()
        {
            WorldSpecificationParseResult result = null;
            Assert.DoesNotThrow(() => result = _parser.TryParse("{}", null));
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void TryParse_WrapsInMarkdownCodeFence_StillParses()
        {
            const string fenced = "```json\n{\"WorldName\":\"Fenced World\"}\n```";
            WorldSpecificationParseResult result = _parser.TryParse(fenced, _request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("Fenced World", result.Specification.WorldName);
        }

        [Test]
        public void TryParse_AlwaysOverwritesOriginalPromptFromRequest_NotFromJson()
        {
            const string json = "{\"OriginalPrompt\":\"something the model made up\",\"WorldName\":\"X\"}";
            WorldSpecificationParseResult result = _parser.TryParse(json, _request);

            Assert.AreEqual(_request.Prompt, result.Specification.OriginalPrompt);
        }

        [Test]
        public void TryParse_ExplicitSeedInRequest_OverridesJsonSeed()
        {
            const string json = "{\"Seed\":111}";
            WorldSpecificationParseResult result = _parser.TryParse(json, _request); // _request has seed 42

            Assert.AreEqual(42, result.Specification.Seed);
        }

        [Test]
        public void TryParse_NoSeedInRequest_UsesJsonSeed()
        {
            var requestWithoutSeed = new WorldDesignRequest("prompt");
            const string json = "{\"Seed\":777}";
            WorldSpecificationParseResult result = _parser.TryParse(json, requestWithoutSeed);

            Assert.AreEqual(777, result.Specification.Seed);
        }

        [Test]
        public void TryParse_RichNestedContent_SurvivesConversion()
        {
            const string json = @"{
                ""WorldName"": ""Himalayan Valley"",
                ""Terrain"": { ""TerrainType"": ""mountain_valley"", ""HasWater"": true, ""WaterFeatureHint"": ""waterfalls"" },
                ""EnvironmentObjects"": [
                    { ""Category"": ""pine_tree"", ""Count"": 500, ""PlacementHint"": ""dense_forest"" },
                    { ""Category"": ""abandoned_cabin"", ""Count"": 4 }
                ],
                ""Obstacles"": [ { ""Id"": ""gate_00"", ""Type"": ""gate"", ""CheckpointIndex"": 0 } ],
                ""Course"": {
                    ""Style"": ""technical_then_high_speed"",
                    ""Difficulty"": ""hard"",
                    ""GateCount"": 15,
                    ""SectionDescriptions"": [""technical and tight"", ""opens into a high-speed valley""]
                }
            }";

            WorldSpecificationParseResult result = _parser.TryParse(json, _request);

            Assert.IsTrue(result.Success);
            WorldSpecification spec = result.Specification;
            Assert.AreEqual("mountain_valley", spec.Terrain.TerrainType);
            Assert.AreEqual("waterfalls", spec.Terrain.WaterFeatureHint);
            Assert.AreEqual(2, spec.EnvironmentObjects.Count);
            Assert.AreEqual("abandoned_cabin", spec.EnvironmentObjects[1].Category);
            Assert.AreEqual(1, spec.Obstacles.Count);
            Assert.AreEqual("technical_then_high_speed", spec.Course.Style);
            Assert.AreEqual(15, spec.Course.GateCount);
            Assert.AreEqual(2, spec.Course.SectionDescriptions.Count);
            Assert.AreEqual("opens into a high-speed valley", spec.Course.SectionDescriptions[1]);
        }

        [Test]
        public void TryParse_UnrecognizedExtraFields_AreIgnored_NotAFailure()
        {
            const string json = "{\"WorldName\":\"X\",\"SomeFieldThatDoesNotExist\":\"value\",\"AnotherOne\":123}";
            WorldSpecificationParseResult result = _parser.TryParse(json, _request);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void TryParse_TypeMetadataField_IsIgnored_NeverUsedForTypeResolution()
        {
            // A malicious/malformed response attempting to smuggle a $type hint. With
            // TypeNameHandling.None (see WorldSpecificationJsonParser remarks) this can never
            // cause arbitrary type instantiation — confirms it's simply ignored, not acted on.
            const string json = "{\"$type\":\"System.Diagnostics.Process, System\",\"WorldName\":\"X\"}";

            WorldSpecificationParseResult result = null;
            Assert.DoesNotThrow(() => result = _parser.TryParse(json, _request));
            Assert.IsTrue(result.Success);
            Assert.IsInstanceOf<WorldSpecification>(result.Specification);
            Assert.AreEqual("X", result.Specification.WorldName);
        }

        [Test]
        public void TryParse_SuspiciousStringContent_TreatedAsInertData_NeverExecuted()
        {
            // Script/command-injection-shaped strings in a free-form field must end up as
            // plain, inert string data — there is no code path in this parser (or anywhere
            // downstream that consumes WorldSpecification) that evaluates, shells out to, or
            // reflects on field content.
            const string json = "{\"EnvironmentObjects\":[{\"Category\":\"'; DROP TABLE worlds; --\",\"Count\":1}],\"Description\":\"<script>alert(1)</script>\"}";

            WorldSpecificationParseResult result = _parser.TryParse(json, _request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("'; DROP TABLE worlds; --", result.Specification.EnvironmentObjects[0].Category);
            Assert.AreEqual("<script>alert(1)</script>", result.Specification.Description);
        }

        [Test]
        public void TryParse_DeeplyNestedJson_FailsCleanly_DoesNotStackOverflow()
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < 200; i++) builder.Append("{\"a\":");
            builder.Append("1");
            for (int i = 0; i < 200; i++) builder.Append('}');

            WorldSpecificationParseResult result = null;
            Assert.DoesNotThrow(() => result = _parser.TryParse(builder.ToString(), _request));
            Assert.IsFalse(result.Success, "Depth beyond MaxDepth should fail cleanly, not succeed or crash.");
        }
    }
}
