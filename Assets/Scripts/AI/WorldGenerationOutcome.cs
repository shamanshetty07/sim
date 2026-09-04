using Sim.WorldGeneration.Models;

namespace Sim.AI
{
    /// <summary>
    /// What an IWorldGenerationService call returns: either a ReactorWorldResult on success,
    /// or a reason + message on failure. Deliberately not just "throw on failure" — a failed
    /// generation (backend down, not configured, timed out) is an expected, common outcome
    /// the UI needs to react to (Retry / Use last valid world / Use example world per
    /// docs/ARCHITECTURE.md §7), not an exceptional program state.
    /// </summary>
    public sealed class WorldGenerationOutcome
    {
        public bool Success { get; private set; }
        public ReactorWorldResult Result { get; private set; }
        public WorldGenerationFailureReason FailureReason { get; private set; }
        public string ErrorMessage { get; private set; }

        private WorldGenerationOutcome() { }

        public static WorldGenerationOutcome Succeeded(ReactorWorldResult result) => new WorldGenerationOutcome
        {
            Success = true,
            Result = result,
            FailureReason = WorldGenerationFailureReason.None
        };

        public static WorldGenerationOutcome Failed(WorldGenerationFailureReason reason, string message) => new WorldGenerationOutcome
        {
            Success = false,
            FailureReason = reason,
            ErrorMessage = message
        };
    }
}
