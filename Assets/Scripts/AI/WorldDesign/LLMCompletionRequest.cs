namespace Sim.AI.WorldDesign
{
    /// <summary>A single provider-neutral completion request — the shape every ILLMClient implementation accepts, regardless of how it maps this onto its own provider's real API.</summary>
    public sealed class LLMCompletionRequest
    {
        /// <summary>Instructions describing the required output schema/behaviour — built by LLMWorldDesigner, not the caller.</summary>
        public string SystemPrompt { get; set; }

        /// <summary>The user's complete, unmodified prompt.</summary>
        public string UserPrompt { get; set; }

        public float Temperature { get; set; } = 0.7f;

        public int MaxOutputTokens { get; set; } = 4096;
    }
}
