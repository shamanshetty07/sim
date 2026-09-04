using Sim.WorldGeneration.Models;

namespace Sim.AI.WorldDesign
{
    /// <summary>Parses raw LLM output text into a WorldSpecification. Its own interface specifically so it's unit-testable with canned JSON strings, independent of any real or mock LLM call.</summary>
    public interface IWorldSpecificationJsonParser
    {
        /// <summary>
        /// Never throws. <paramref name="originalRequest"/>'s prompt is written into the
        /// result's OriginalPrompt (and its Seed, if set, overrides whatever the LLM
        /// produced) regardless of what the JSON contains — the caller's request is always the
        /// source of truth for those two fields, never the model's echo of them.
        /// </summary>
        WorldSpecificationParseResult TryParse(string rawText, WorldDesignRequest originalRequest);
    }
}
