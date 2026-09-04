using UnityEngine;

namespace Sim.WorldGeneration.Spawn
{
    public sealed class SpawnResolutionResult
    {
        public bool Success { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        public string ErrorMessage { get; private set; }

        private SpawnResolutionResult() { }

        public static SpawnResolutionResult Succeeded(Vector3 position, Quaternion rotation) =>
            new SpawnResolutionResult { Success = true, Position = position, Rotation = rotation };

        public static SpawnResolutionResult Failed(string message) =>
            new SpawnResolutionResult { Success = false, ErrorMessage = message };
    }
}
