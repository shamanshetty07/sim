using Sim.WorldGeneration.Models;

namespace Sim.AI.WorldDesign
{
    /// <summary>What an IWorldDesigner call returns: a raw (unvalidated) WorldSpecification on success, or a reason + message on failure. Never throws for an expected failure — see WorldDesignFailureReason.</summary>
    public sealed class WorldDesignOutcome
    {
        public bool Success { get; private set; }
        public WorldSpecification Specification { get; private set; }
        public WorldDesignFailureReason FailureReason { get; private set; }
        public string ErrorMessage { get; private set; }

        private WorldDesignOutcome() { }

        public static WorldDesignOutcome Succeeded(WorldSpecification specification) => new WorldDesignOutcome
        {
            Success = true,
            Specification = specification,
            FailureReason = WorldDesignFailureReason.None
        };

        public static WorldDesignOutcome Failed(WorldDesignFailureReason reason, string message) => new WorldDesignOutcome
        {
            Success = false,
            FailureReason = reason,
            ErrorMessage = message
        };
    }
}
