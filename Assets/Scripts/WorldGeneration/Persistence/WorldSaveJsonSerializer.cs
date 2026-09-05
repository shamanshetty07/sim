using Newtonsoft.Json;

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
    /// </summary>
    public sealed class WorldSaveJsonSerializer : IWorldSaveSerializer
    {
        private static readonly JsonSerializerSettings SafeSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            MaxDepth = 32,
            Formatting = Formatting.Indented
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
