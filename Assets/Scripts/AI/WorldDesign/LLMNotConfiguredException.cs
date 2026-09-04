using System;

namespace Sim.AI.WorldDesign
{
    /// <summary>Thrown by an ILLMClient implementation while it has no credentials to even attempt a call — same "cannot proceed at all" category as ReactorNotConfiguredException (Sim.AI), thrown synchronously from an async-signature method for the same reason (see that type's remarks).</summary>
    public sealed class LLMNotConfiguredException : Exception
    {
        public LLMNotConfiguredException(string providerName) : base(
            $"{providerName} is not configured — no API key/endpoint found for this provider. " +
            "See docs/AI_WORLD_DESIGNER.md for the expected configuration variables.")
        {
        }
    }
}
