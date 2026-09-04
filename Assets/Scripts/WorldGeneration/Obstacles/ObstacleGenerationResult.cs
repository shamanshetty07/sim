using System.Collections.Generic;
using UnityEngine;

namespace Sim.WorldGeneration.Obstacles
{
    /// <summary>Result of one ObstacleGenerator.Generate call: the built hierarchy root and the ordered checkpoint sequence for Sim.Gameplay.CheckpointManager to track.</summary>
    public sealed class ObstacleGenerationResult
    {
        public GameObject Root { get; }

        /// <summary>Ordered by Index — CheckpointManager reads this directly, no re-sorting needed.</summary>
        public IReadOnlyList<CheckpointDefinition> Checkpoints { get; }

        public ObstacleGenerationResult(GameObject root, IReadOnlyList<CheckpointDefinition> checkpoints)
        {
            Root = root;
            Checkpoints = checkpoints;
        }
    }
}
