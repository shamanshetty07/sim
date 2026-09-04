using System;
using Newtonsoft.Json;
using Sim.WorldGeneration.Models;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// The one place LLM-generated text becomes a WorldSpecification object. This is the
    /// project's "never execute AI-generated code" boundary in concrete form — read the
    /// settings below carefully before changing them:
    ///
    ///  - <c>TypeNameHandling.None</c> (the safe default, set explicitly rather than left
    ///    implicit because this is a security-relevant choice, not an accident): the
    ///    deserializer never resolves a type from a <c>$type</c> field in the JSON. Untrusted
    ///    input can never cause this parser to instantiate an arbitrary .NET type — only the
    ///    fixed, closed set of types reachable from WorldSpecification's own property graph.
    ///    NEVER change this to Auto/All/Objects/Arrays; doing so on untrusted input is a
    ///    well-known deserialization-remote-code-execution pattern.
    ///  - <c>MetadataPropertyHandling.Ignore</c>: any <c>$type</c>/<c>$id</c>/<c>$ref</c> the
    ///    model's output happens to contain is ignored outright, not merely unresolved.
    ///  - <c>MissingMemberHandling.Ignore</c>: an unrecognized field in the JSON (the model
    ///    added something not in our schema) is dropped, not a hard failure — the LLM's output
    ///    doesn't have to be byte-for-byte exact, only usable.
    ///  - <c>MaxDepth</c>: bounds recursive parsing depth against a pathologically nested
    ///    payload (a cheap, real DoS vector for any JSON deserializer on untrusted input).
    ///
    /// Using Newtonsoft.Json here (rather than Unity's built-in JsonUtility) is a deliberate,
    /// justified dependency: JsonUtility cannot deserialize into auto-implemented C# properties
    /// (only public fields), and WorldSpecification's model classes use properties throughout —
    /// Unity's own officially-distributed Newtonsoft.Json package
    /// (com.unity.nuget.newtonsoft-json) is the standard solution to exactly this gap.
    /// </summary>
    public sealed class WorldSpecificationJsonParser : IWorldSpecificationJsonParser
    {
        private static readonly JsonSerializerSettings SafeSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            MaxDepth = 32
        };

        public WorldSpecificationParseResult TryParse(string rawText, WorldDesignRequest originalRequest)
        {
            if (originalRequest == null)
                return WorldSpecificationParseResult.Failed("No originating request — cannot establish OriginalPrompt.");

            if (string.IsNullOrWhiteSpace(rawText))
                return WorldSpecificationParseResult.Failed("LLM response was empty.");

            string json = StripCodeFence(rawText);

            WorldSpecification specification;
            try
            {
                specification = JsonConvert.DeserializeObject<WorldSpecification>(json, SafeSettings);
            }
            catch (JsonException ex)
            {
                // Includes JsonReaderException/JsonSerializationException — malformed JSON and
                // schema mismatches both land here. Message is safe to log (it's Newtonsoft's
                // own parse-error text, not a copy of the untrusted payload). Note MaxDepth
                // above turns what would otherwise be a stack-overflow risk from pathologically
                // nested input into an ordinary JsonException caught right here.
                return WorldSpecificationParseResult.Failed($"Response was not valid JSON matching the expected schema: {ex.Message}");
            }

            if (specification == null)
                return WorldSpecificationParseResult.Failed("Response parsed to a null specification.");

            // The request, never the model's own text, is authoritative for these two fields —
            // matches ReactorWorldAdapter's identical rule for the same reason (Phase 5).
            specification.OriginalPrompt = originalRequest.Prompt;
            if (originalRequest.Seed.HasValue)
                specification.Seed = originalRequest.Seed.Value;

            return WorldSpecificationParseResult.Succeeded(specification);
        }

        /// <summary>LLMs commonly wrap JSON output in a markdown code fence even when explicitly told not to — stripped defensively rather than treated as a parse failure.</summary>
        private static string StripCodeFence(string text)
        {
            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```")) return trimmed;

            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0) return trimmed;

            string withoutOpeningFence = trimmed.Substring(firstNewline + 1);
            int closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
            return closingFenceIndex >= 0
                ? withoutOpeningFence.Substring(0, closingFenceIndex).Trim()
                : withoutOpeningFence.Trim();
        }
    }
}
