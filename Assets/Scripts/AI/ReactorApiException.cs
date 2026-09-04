using System;

namespace Sim.AI
{
    /// <summary>
    /// Thrown by low-level Reactor API calls (currently just session-token minting) when the
    /// call reaches the network layer but fails — a non-2xx response, a connection error, a
    /// malformed response body. Distinct from ReactorNotConfiguredException (no credentials to
    /// even attempt the call) so callers can tell "we tried and Reactor/the network said no"
    /// apart from "we never had the means to try."
    ///
    /// The message is always safe to log — it never includes the API key or JWT (see
    /// OpenWorldReactorWorldGenerationService: the request header carrying the key is never
    /// echoed back into an exception message, and UnityWebRequest.error does not include
    /// request headers).
    /// </summary>
    public sealed class ReactorApiException : Exception
    {
        /// <summary>True for a connection-level failure (DNS/timeout/refused) — maps to WorldGenerationFailureReason.NetworkError. False for a reachable-but-rejected response (e.g. 401/403/5xx) — maps to WorldGenerationFailureReason.Unavailable.</summary>
        public bool IsConnectionError { get; }

        public ReactorApiException(string message, bool isConnectionError) : base(message)
        {
            IsConnectionError = isConnectionError;
        }
    }
}
