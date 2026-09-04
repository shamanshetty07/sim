using Sim.WorldGeneration.Models;

namespace Sim.AI.WorldDesign
{
    /// <summary>Result of attempting to parse LLM output text into a WorldSpecification. Failure here means "not usable JSON for our schema" — a normal, expected outcome for untrusted model output, not an exceptional one.</summary>
    public sealed class WorldSpecificationParseResult
    {
        public bool Success { get; private set; }
        public WorldSpecification Specification { get; private set; }
        public string ErrorMessage { get; private set; }

        private WorldSpecificationParseResult() { }

        public static WorldSpecificationParseResult Succeeded(WorldSpecification specification) =>
            new WorldSpecificationParseResult { Success = true, Specification = specification };

        public static WorldSpecificationParseResult Failed(string message) =>
            new WorldSpecificationParseResult { Success = false, ErrorMessage = message };
    }
}
