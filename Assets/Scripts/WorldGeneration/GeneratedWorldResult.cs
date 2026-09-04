using Sim.Gameplay;
using UnityEngine;

namespace Sim.WorldGeneration
{
    /// <summary>Result of one WorldGenerator.Generate call. On failure, Root/CheckpointManager are null and no generated GameObjects remain in the scene (WorldGenerator cleans up after itself on failure — see its remarks).</summary>
    public sealed class GeneratedWorldResult
    {
        public bool Success { get; private set; }
        public GameObject Root { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Quaternion SpawnRotation { get; private set; }
        public CheckpointManager CheckpointManager { get; private set; }
        public string ErrorMessage { get; private set; }

        private GeneratedWorldResult() { }

        public static GeneratedWorldResult Succeeded(GameObject root, Vector3 spawnPosition, Quaternion spawnRotation, CheckpointManager checkpointManager) =>
            new GeneratedWorldResult
            {
                Success = true,
                Root = root,
                SpawnPosition = spawnPosition,
                SpawnRotation = spawnRotation,
                CheckpointManager = checkpointManager
            };

        public static GeneratedWorldResult Failed(string message) => new GeneratedWorldResult
        {
            Success = false,
            ErrorMessage = message
        };
    }
}
