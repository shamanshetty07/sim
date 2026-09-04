namespace Sim.AI.WorldDesign
{
    /// <summary>Structured failure categories for a WorldDesignOutcome — same non-throwing-outcome philosophy as WorldGenerationFailureReason (Sim.AI), kept as its own type since this is a deliberately separate pipeline stage.</summary>
    public enum WorldDesignFailureReason
    {
        None,

        /// <summary>No credentials configured for the LLM provider.</summary>
        NotConfigured,

        NetworkError,

        /// <summary>Reached the provider but it rejected the request or errored.</summary>
        Unavailable,

        Timeout,

        /// <summary>The LLM responded, but its text wasn't valid/parseable JSON matching the expected schema.</summary>
        InvalidResponse,

        Cancelled,

        Unknown
    }
}
