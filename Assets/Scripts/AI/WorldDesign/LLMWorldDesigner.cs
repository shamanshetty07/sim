using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// The real IWorldDesigner: sends the complete user prompt to a general-purpose LLM (via
    /// the injected, provider-neutral ILLMClient) with instructions to interpret it into
    /// WorldSpecification-shaped JSON, then parses that JSON via
    /// IWorldSpecificationJsonParser. All prompt-engineering and JSON-handling logic lives
    /// here exactly once — swapping OpenAI for Claude for a local model is swapping the
    /// injected ILLMClient, not touching this class. See ILLMClient's remarks for why that's
    /// the chosen shape over parallel per-provider designer classes.
    ///
    /// This class never does keyword matching or biome-preset selection of its own — its only
    /// job is building the system prompt, delegating interpretation entirely to the model, and
    /// safely handling whatever comes back. The actual "what does this prompt mean" reasoning
    /// happens inside the LLM, not here.
    /// </summary>
    public sealed class LLMWorldDesigner : IWorldDesigner
    {
        private readonly ILLMClient _client;
        private readonly IWorldSpecificationJsonParser _parser;

        public LLMWorldDesigner(ILLMClient client, IWorldSpecificationJsonParser parser = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _parser = parser ?? new WorldSpecificationJsonParser();
        }

        public async Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                return WorldDesignOutcome.Failed(WorldDesignFailureReason.InvalidResponse, "Request was null.");

            Debug.Log("[WorldDesign] Prompt received.");
            Debug.Log($"[WorldDesign] Provider: {_client.ProviderName}");

            var completionRequest = new LLMCompletionRequest
            {
                SystemPrompt = BuildSystemPrompt(),
                UserPrompt = request.Prompt
            };

            LLMCompletionResult completion;
            try
            {
                Debug.Log("[WorldDesign] Design started.");
                completion = await _client.CompleteAsync(completionRequest, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[WorldDesign] Design cancelled.");
                return WorldDesignOutcome.Failed(WorldDesignFailureReason.Cancelled, "World design was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldDesign] Unexpected exception from ILLMClient ({_client.ProviderName}): {ex}");
                return WorldDesignOutcome.Failed(WorldDesignFailureReason.Unknown, "World design failed.");
            }

            if (!completion.Success)
            {
                Debug.LogWarning($"[WorldDesign] Provider {_client.ProviderName} failed: {completion.ErrorMessage}");
                return WorldDesignOutcome.Failed(WorldDesignFailureReason.Unavailable, "World design failed.");
            }

            WorldSpecificationParseResult parsed = _parser.TryParse(completion.Text, request);
            if (!parsed.Success)
            {
                Debug.LogWarning($"[WorldDesign] Response could not be parsed: {parsed.ErrorMessage}");
                return WorldDesignOutcome.Failed(WorldDesignFailureReason.InvalidResponse, "World design produced an unusable response.");
            }

            Debug.Log("[WorldDesign] Design completed.");
            return WorldDesignOutcome.Succeeded(parsed.Specification);
        }

        /// <summary>
        /// Instructs the model to interpret the ENTIRE prompt richly into the
        /// WorldSpecification schema — never to reduce it to a fixed preset, and never to
        /// treat the JSON schema as a checklist of the only things worth expressing (the
        /// free-form Category/Type/TerrainType/Style fields exist so nothing the prompt
        /// mentions has to be dropped for lack of a matching enum value).
        /// </summary>
        private static string BuildSystemPrompt()
        {
            return
                "You are the world designer for an FPV (first-person-view) drone flight " +
                "simulator. You will receive one complete natural-language prompt describing " +
                "an environment and/or racing course. Interpret the FULL prompt — its style, " +
                "scale, atmosphere, terrain, specific objects and structures named, obstacle/" +
                "gate count and placement, difficulty, and any multi-section narrative (e.g. " +
                "\"tight and technical at first, then opens up\") — into a single JSON object " +
                "matching the schema below. Respond with ONLY that JSON object: no markdown " +
                "code fences, no commentary before or after it.\n\n" +
                "Do not reduce the prompt to a simple preset (e.g. do not just pick one of " +
                "\"mountain\"/\"forest\"/\"desert\" and stop there) — use the free-form string " +
                "fields (TerrainType, Category, Type, Style, Difficulty, PlacementHint, " +
                "WaterFeatureHint) to express anything the prompt describes that doesn't fit a " +
                "common pattern. Represent named structures (cabins, ruins, bridges, towers, " +
                "etc.) as EnvironmentObjects entries with a descriptive Category, not just " +
                "trees/rocks. Use Course.SectionDescriptions (an ordered list of short phrases) " +
                "to capture any described progression through the course.\n\n" +
                "Schema (all fields optional except where noted; omit a field to accept its " +
                "default rather than guessing):\n" +
                "{\n" +
                "  \"WorldName\": string, \"Description\": string,\n" +
                "  \"Scale\": \"Small\"|\"Medium\"|\"Large\"|\"Huge\",\n" +
                "  \"Flight\": { \"PreferredStyle\": \"Cruise\"|\"Race\"|\"Freestyle\"|\"Technical\", " +
                "\"TightnessScore01\": 0-1, \"ObstacleDensity01\": 0-1, \"VerticalityScore01\": 0-1 },\n" +
                "  \"Terrain\": { \"TerrainType\": string, \"Width\": meters, \"Depth\": meters, " +
                "\"MaxHeight\": meters, \"HeightVariation01\": 0-1, \"HasWater\": bool, \"WaterFeatureHint\": string },\n" +
                "  \"EnvironmentObjects\": [ { \"Category\": string, \"Count\": int, \"Density01\": 0-1, \"PlacementHint\": string } ],\n" +
                "  \"Obstacles\": [ { \"Id\": string, \"Type\": \"gate\"|\"ring\"|\"wall\"|\"pole\"|\"tunnel\"|\"checkpoint\"|\"landing_pad\", " +
                "\"Position\": {\"x\":.,\"y\":.,\"z\":.}, \"RotationEuler\": {\"x\":.,\"y\":.,\"z\":.}, " +
                "\"Scale\": {\"x\":.,\"y\":.,\"z\":.}, \"CheckpointIndex\": int|null } ],\n" +
                "  \"Course\": { \"Style\": string, \"Difficulty\": string, \"GateCount\": int, \"SectionDescriptions\": [string] },\n" +
                "  \"Weather\": { \"Type\": string, \"FogDensity01\": 0-1, \"WindStrength01\": 0-1 },\n" +
                "  \"Lighting\": { \"TimeOfDayHours\": 0-24, \"SunIntensity\": float },\n" +
                "  \"Spawn\": { \"Position\": {\"x\":.,\"y\":.,\"z\":.} }\n" +
                "}\n\n" +
                "Do not include a Seed or OriginalPrompt field — the application supplies those " +
                "itself and will overwrite anything you provide.";
        }
    }
}
