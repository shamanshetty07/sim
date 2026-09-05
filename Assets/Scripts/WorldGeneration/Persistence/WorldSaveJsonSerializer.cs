using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sim.WorldGeneration.Persistence
{
    /// <summary>
    /// The one place a save file's text becomes a WorldSaveData object, and back. Uses exactly
    /// the same safety settings as WorldSpecificationJsonParser (Sim.AI.WorldDesign) — a save
    /// file is untrusted input in precisely the same sense LLM output is, and deserves the same
    /// "never execute AI-generated/untrusted-file-generated code" boundary:
    ///
    ///  - <c>TypeNameHandling.None</c>: never resolves a type from a <c>$type</c> field. A
    ///    hand-edited or corrupted save file can never cause this to instantiate an arbitrary
    ///    .NET type — only the fixed, closed set of types reachable from WorldSaveData's own
    ///    property graph. NEVER change this to Auto/All/Objects/Arrays.
    ///  - <c>MetadataPropertyHandling.Ignore</c>: any <c>$type</c>/<c>$id</c>/<c>$ref</c> in the
    ///    file is ignored outright, not merely unresolved.
    ///  - <c>MissingMemberHandling.Ignore</c>: an unrecognized field (e.g. from a newer app
    ///    version) is dropped, not a hard failure.
    ///  - <c>MaxDepth</c>: bounds recursive parsing depth against a pathologically nested save
    ///    file — the same cheap, real DoS vector for untrusted JSON, applied here too.
    ///
    /// No second JSON framework: this is the same Newtonsoft.Json (com.unity.nuget.
    /// newtonsoft-json) dependency and pattern already used throughout WorldGeneration/AI.
    ///
    /// Vector3JsonConverter below exists because Newtonsoft's default reflection contract walks
    /// every public property/field it finds — including UnityEngine.Vector3's *derived*
    /// properties (normalized, magnitude, sqrMagnitude), not just x/y/z. <c>normalized</c> is
    /// itself a Vector3 with its own <c>normalized</c> property, so the default resolver recurses
    /// into it forever; this is a structural fact about Vector3, not something any WorldSaveData
    /// content can trigger or avoid, and it only surfaces the moment Serialize() is actually run
    /// (confirmed only once this project was actually opened in a real Unity Editor — no
    /// behavior change on our side made this appear or could have hidden it). The converter
    /// writes/reads only x/y/z, which is everything WorldSaveData's Vector3 fields ever need.
    /// </summary>
    public sealed class WorldSaveJsonSerializer : IWorldSaveSerializer
    {
        private sealed class Vector3JsonConverter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x");
                writer.WriteValue(value.x);
                writer.WritePropertyName("y");
                writer.WriteValue(value.y);
                writer.WritePropertyName("z");
                writer.WriteValue(value.z);
                writer.WriteEndObject();
            }

            public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                JObject obj = JObject.Load(reader);
                return new Vector3(
                    obj["x"]?.Value<float>() ?? 0f,
                    obj["y"]?.Value<float>() ?? 0f,
                    obj["z"]?.Value<float>() ?? 0f);
            }
        }

        /// <summary>Same problem, same fix, as Vector3JsonConverter above — UnityEngine.Color has its own recursive derived properties (linear/gamma, each returning another Color). Writes/reads only r/g/b/a.</summary>
        private sealed class ColorJsonConverter : JsonConverter<Color>
        {
            public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("r");
                writer.WriteValue(value.r);
                writer.WritePropertyName("g");
                writer.WriteValue(value.g);
                writer.WritePropertyName("b");
                writer.WriteValue(value.b);
                writer.WritePropertyName("a");
                writer.WriteValue(value.a);
                writer.WriteEndObject();
            }

            public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                JObject obj = JObject.Load(reader);
                return new Color(
                    obj["r"]?.Value<float>() ?? 0f,
                    obj["g"]?.Value<float>() ?? 0f,
                    obj["b"]?.Value<float>() ?? 0f,
                    obj["a"]?.Value<float>() ?? 1f);
            }
        }

        private static readonly JsonSerializerSettings SafeSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            MaxDepth = 32,
            Formatting = Formatting.Indented,
            Converters = { new Vector3JsonConverter(), new ColorJsonConverter() }
        };

        public string Serialize(WorldSaveData data) => JsonConvert.SerializeObject(data, SafeSettings);

        public WorldSaveDeserializeResult Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return WorldSaveDeserializeResult.Failed("Save file was empty.");

            WorldSaveData data;
            try
            {
                data = JsonConvert.DeserializeObject<WorldSaveData>(json, SafeSettings);
            }
            catch (JsonException ex)
            {
                // Includes JsonReaderException/JsonSerializationException — malformed JSON and
                // schema mismatches both land here. Newtonsoft's own parse-error message is safe
                // to surface (it's never a copy of the untrusted file content itself).
                return WorldSaveDeserializeResult.Failed($"Save file was not valid JSON matching the expected schema: {ex.Message}");
            }

            if (data == null)
                return WorldSaveDeserializeResult.Failed("Save file parsed to a null object.");

            return WorldSaveDeserializeResult.Succeeded(data);
        }
    }
}
