using Sim.WorldGeneration.Models;

namespace Sim.WorldGeneration.Persistence
{
    /// <summary>Result of WorldSaveValidator.Validate — success carries the (possibly repaired, per the existing WorldSpecificationValidator repair-vs-reject policy) specification actually safe to generate from.</summary>
    public sealed class WorldLoadValidationResult
    {
        public bool Success { get; private set; }
        public WorldSpecification ValidatedSpecification { get; private set; }
        public string ErrorMessage { get; private set; }

        private WorldLoadValidationResult() { }

        public static WorldLoadValidationResult Succeeded(WorldSpecification specification) =>
            new WorldLoadValidationResult { Success = true, ValidatedSpecification = specification };

        public static WorldLoadValidationResult Failed(string message) =>
            new WorldLoadValidationResult { Success = false, ErrorMessage = message };
    }
}
