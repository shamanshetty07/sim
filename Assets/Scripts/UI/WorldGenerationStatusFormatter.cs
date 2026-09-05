using Sim.Core;

namespace Sim.UI
{
    /// <summary>Pure state -> display-text mapping — no MonoBehaviour, no UI dependency, so it's unit-testable on its own (same pattern as TelemetryFormatter, Phase 4).</summary>
    public static class WorldGenerationStatusFormatter
    {
        public static string Format(WorldGenerationState state, string lastErrorMessage)
        {
            switch (state)
            {
                case WorldGenerationState.Idle: return "Enter a world description.";
                case WorldGenerationState.Designing: return "Designing world...";
                case WorldGenerationState.Validating: return "Validating world specification...";
                case WorldGenerationState.Generating: return "Generating Unity world...";
                case WorldGenerationState.Ready: return "World ready — fly!";
                case WorldGenerationState.Cancelled: return "Generation cancelled.";
                case WorldGenerationState.Failed:
                    return string.IsNullOrEmpty(lastErrorMessage) ? "Generation failed." : $"Generation failed: {lastErrorMessage}";
                default: return string.Empty;
            }
        }

        /// <summary>Whether the Generate button should be interactable in this state — busy (Designing/Validating/Generating) disables it.</summary>
        public static bool IsGenerateAvailable(WorldGenerationState state) =>
            state != WorldGenerationState.Designing && state != WorldGenerationState.Validating && state != WorldGenerationState.Generating;

        public static bool IsCancelAvailable(WorldGenerationState state) => !IsGenerateAvailable(state);

        public static bool IsClearAvailable(WorldGenerationState state) =>
            state == WorldGenerationState.Ready || state == WorldGenerationState.Failed || state == WorldGenerationState.Cancelled;

        /// <summary>Phase 14: Save only makes sense once a world actually exists to save.</summary>
        public static bool IsSaveAvailable(WorldGenerationState state) => state == WorldGenerationState.Ready;

        /// <summary>Phase 14: Load is available whenever Generate is (i.e. not while busy designing/validating/generating) — loading is just another way to reach Ready.</summary>
        public static bool IsLoadAvailable(WorldGenerationState state) => IsGenerateAvailable(state);
    }
}
