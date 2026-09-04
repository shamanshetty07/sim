using System;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Thrown by an ILLMClient implementation when its own bounded timeout elapses before the
    /// provider responded — kept distinct from a plain OperationCanceledException specifically
    /// so LLMWorldDesigner can tell "the caller cancelled" (-&gt; WorldDesignFailureReason.Cancelled)
    /// apart from "we gave up waiting" (-&gt; WorldDesignFailureReason.Timeout); both enum values
    /// already existed for exactly this distinction. Same "signal an exceptional, non-retryable
    /// condition via a dedicated exception type" idiom as LLMNotConfiguredException.
    /// </summary>
    public sealed class LLMRequestTimeoutException : Exception
    {
        public LLMRequestTimeoutException(string providerName, int timeoutSeconds) : base(
            $"{providerName} request timed out after {timeoutSeconds}s.")
        {
        }
    }
}
