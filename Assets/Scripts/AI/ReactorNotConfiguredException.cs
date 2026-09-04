using System;

namespace Sim.AI
{
    /// <summary>
    /// Thrown by OpenWorldReactorWorldGenerationService while it has no real SDK/API access to
    /// integrate against (see docs/WORLD_SPECIFICATION.md "Open questions" and
    /// docs/ARCHITECTURE.md §6). Deliberately a distinct type from a generic exception so
    /// callers can catch specifically "the real backend isn't wired up yet" and decide to fall
    /// back to Mock, rather than treating it the same as a network failure.
    /// </summary>
    [Serializable]
    public sealed class ReactorNotConfiguredException : Exception
    {
        public ReactorNotConfiguredException() : base(
            "OpenWorld Reactor is not configured — no SDK/API access was found in this " +
            "environment. See docs/WORLD_SPECIFICATION.md \"Open questions\" for what's needed " +
            "to complete this integration.")
        {
        }

        public ReactorNotConfiguredException(string message) : base(message) { }

        public ReactorNotConfiguredException(string message, Exception innerException) : base(message, innerException) { }
    }
}
