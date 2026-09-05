using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Persistence;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>Same safety-property coverage as WorldSpecificationJsonParserTests, applied to the save-file boundary — a save file is untrusted input in exactly the same sense LLM output is.</summary>
    public class WorldSaveJsonSerializerTests
    {
        private WorldSaveJsonSerializer _serializer;

        [SetUp]
        public void SetUp() => _serializer = new WorldSaveJsonSerializer();

        private static WorldSpecification RichSpecification() => new WorldSpecification
        {
            OriginalPrompt = "Create a Himalayan FPV course with 15 gates.",
            WorldName = "Himalayan Valley",
            Seed = 20260904,
            Terrain = new TerrainSpecification { TerrainType = "mountain", Width = 2000f, Depth = 2000f, MaxHeight = 400f, HeightVariation01 = 0.8f },
            EnvironmentObjects = new List<ObjectSpecification> { new ObjectSpecification { Category = "pine_tree", Count = 400 } },
            Obstacles = new List<ObstacleSpecification>
            {
                new ObstacleSpecification { Id = "gate_00", Type = "gate", Position = new Vector3(1f, 2f, 3f), CheckpointIndex = 0 }
            },
            Course = new CourseSpecification { Style = "technical_then_high_speed", GateCount = 15 },
            Spawn = new SpawnSpecification { Position = new Vector3(0f, 30f, 0f) },
            Metadata = new WorldGenerationMetadata { ProviderName = "Mock" }
        };

        // ------------------------------------------------------------------
        // Round trip
        // ------------------------------------------------------------------

        [Test]
        public void SerializeThenDeserialize_RoundTripsPrompt()
        {
            WorldSaveData original = WorldSaveData.FromSpecification(RichSpecification());
            string json = _serializer.Serialize(original);
            WorldSaveDeserializeResult result = _serializer.Deserialize(json);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(original.Prompt, result.Data.Prompt);
        }

        [Test]
        public void SerializeThenDeserialize_RoundTripsSeed()
        {
            WorldSaveData original = WorldSaveData.FromSpecification(RichSpecification());
            WorldSaveDeserializeResult result = _serializer.Deserialize(_serializer.Serialize(original));

            Assert.AreEqual(original.Seed, result.Data.Seed);
        }

        [Test]
        public void SerializeThenDeserialize_RoundTripsSpecificationContent()
        {
            WorldSaveData original = WorldSaveData.FromSpecification(RichSpecification());
            WorldSaveDeserializeResult result = _serializer.Deserialize(_serializer.Serialize(original));

            Assert.AreEqual("Himalayan Valley", result.Data.Specification.WorldName);
            Assert.AreEqual("mountain", result.Data.Specification.Terrain.TerrainType);
            Assert.AreEqual(1, result.Data.Specification.Obstacles.Count);
            Assert.AreEqual("technical_then_high_speed", result.Data.Specification.Course.Style);
            Assert.AreEqual(15, result.Data.Specification.Course.GateCount);
        }

        [Test]
        public void SerializeThenDeserialize_RoundTripsMetadata()
        {
            WorldSaveData original = WorldSaveData.FromSpecification(RichSpecification());
            WorldSaveDeserializeResult result = _serializer.Deserialize(_serializer.Serialize(original));

            Assert.AreEqual("Mock", result.Data.Metadata.ProviderName);
        }

        [Test]
        public void SerializeThenDeserialize_RoundTripsVersion()
        {
            WorldSaveData original = WorldSaveData.FromSpecification(RichSpecification());
            WorldSaveDeserializeResult result = _serializer.Deserialize(_serializer.Serialize(original));

            Assert.AreEqual(WorldSaveData.CurrentVersion, result.Data.Version);
        }

        // ------------------------------------------------------------------
        // Malformed / invalid input
        // ------------------------------------------------------------------

        [Test]
        public void Deserialize_MalformedJson_Fails_DoesNotThrow()
        {
            WorldSaveDeserializeResult result = null;
            Assert.DoesNotThrow(() => result = _serializer.Deserialize("{ this is not valid json at all"));
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.ErrorMessage);
        }

        [Test]
        public void Deserialize_EmptyString_Fails()
        {
            Assert.IsFalse(_serializer.Deserialize("").Success);
        }

        [Test]
        public void Deserialize_NullString_Fails_DoesNotThrow()
        {
            WorldSaveDeserializeResult result = null;
            Assert.DoesNotThrow(() => result = _serializer.Deserialize(null));
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Deserialize_LiteralJsonNull_Fails()
        {
            Assert.IsFalse(_serializer.Deserialize("null").Success);
        }

        [Test]
        public void Deserialize_UnrecognizedExtraFields_AreIgnored_NotAFailure()
        {
            const string json = "{\"Prompt\":\"x\",\"Seed\":1,\"SomeFieldThatDoesNotExist\":\"value\"}";
            Assert.IsTrue(_serializer.Deserialize(json).Success);
        }

        [Test]
        public void Deserialize_TypeMetadataField_IsIgnored_NeverUsedForTypeResolution()
        {
            // A hand-edited or corrupted save file attempting to smuggle a $type hint. With
            // TypeNameHandling.None (see WorldSaveJsonSerializer remarks) this can never cause
            // arbitrary type instantiation — confirms it's simply ignored, not acted on.
            const string json = "{\"$type\":\"System.Diagnostics.Process, System\",\"Prompt\":\"x\",\"Seed\":1}";

            WorldSaveDeserializeResult result = null;
            Assert.DoesNotThrow(() => result = _serializer.Deserialize(json));
            Assert.IsTrue(result.Success);
            Assert.IsInstanceOf<WorldSaveData>(result.Data);
            Assert.AreEqual("x", result.Data.Prompt);
        }

        [Test]
        public void Deserialize_SuspiciousStringContent_TreatedAsInertData_NeverExecuted()
        {
            // Script/command-injection-shaped strings in a free-form field must end up as plain,
            // inert string data — nothing in this serializer (or anywhere downstream that
            // consumes WorldSaveData) evaluates, shells out to, or reflects on field content.
            const string json = "{\"Prompt\":\"<script>alert(1)</script>\",\"Seed\":1,\"Specification\":{\"OriginalPrompt\":\"<script>alert(1)</script>\",\"WorldName\":\"'; DROP TABLE worlds; --\"}}";

            WorldSaveDeserializeResult result = _serializer.Deserialize(json);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("<script>alert(1)</script>", result.Data.Prompt);
            Assert.AreEqual("'; DROP TABLE worlds; --", result.Data.Specification.WorldName);
        }

        [Test]
        public void Deserialize_DeeplyNestedJson_FailsCleanly_DoesNotStackOverflow()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < 200; i++) builder.Append("{\"a\":");
            builder.Append('1');
            for (int i = 0; i < 200; i++) builder.Append('}');

            WorldSaveDeserializeResult result = null;
            Assert.DoesNotThrow(() => result = _serializer.Deserialize(builder.ToString()));
            Assert.IsFalse(result.Success, "Depth beyond MaxDepth should fail cleanly, not succeed or crash.");
        }
    }
}
