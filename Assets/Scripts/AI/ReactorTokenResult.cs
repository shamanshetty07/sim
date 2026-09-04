namespace Sim.AI
{
    /// <summary>
    /// Result of successfully exchanging the OpenWorld Reactor API key for a session JWT (see
    /// docs/OPENWORLD_REACTOR_INTEGRATION.md for the verified request/response schema). The
    /// JWT itself is short-lived and scoped (not a long-term secret in the same sense as the
    /// API key), but is still not logged anywhere — treat it as sensitive.
    /// </summary>
    public readonly struct ReactorTokenResult
    {
        public readonly string Jwt;

        /// <summary>Unix epoch seconds.</summary>
        public readonly long ExpiresAtUnixSeconds;

        public ReactorTokenResult(string jwt, long expiresAtUnixSeconds)
        {
            Jwt = jwt;
            ExpiresAtUnixSeconds = expiresAtUnixSeconds;
        }
    }
}
