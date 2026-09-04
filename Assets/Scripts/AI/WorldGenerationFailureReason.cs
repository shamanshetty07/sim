namespace Sim.AI
{
    /// <summary>
    /// Structured failure categories for a WorldGenerationOutcome. Deliberately an enum +
    /// message on a non-throwing result type, not a proliferation of exception subclasses —
    /// an expected, recoverable failure (Reactor unreachable, not configured, request
    /// cancelled) is a normal outcome the UI must react to, not an exceptional program state
    /// that should unwind the stack. Exceptions in this codebase are reserved for genuine
    /// programmer-error/can't-proceed-at-all conditions (see ReactorNotConfiguredException,
    /// ReactorApiException — both caught and converted to one of these reasons before
    /// crossing the IWorldGenerationService boundary).
    /// </summary>
    public enum WorldGenerationFailureReason
    {
        None,

        /// <summary>No credentials found for the backend (maps to the brief's "OpenWorldReactorNotConfigured").</summary>
        NotConfigured,

        /// <summary>Credentials exist but the network/connection itself failed (DNS, timeout, connection refused).</summary>
        NetworkError,

        /// <summary>Reached the backend but it rejected the request or errored (maps to "OpenWorldReactorUnavailable").</summary>
        Unavailable,

        Timeout,

        /// <summary>The backend responded, but its response couldn't be used (malformed, missing required fields).</summary>
        InvalidResponse,

        /// <summary>The adapted WorldSpecification failed validation.</summary>
        ValidationFailed,

        Cancelled,

        /// <summary>Authentication/connectivity succeeded, but the requested operation isn't built yet — see docs/OPENWORLD_REACTOR_INTEGRATION.md.</summary>
        NotImplemented,

        Unknown
    }
}
