using UnityEngine;

namespace Sim.WorldGeneration.Obstacles
{
    /// <summary>One entry in the generated checkpoint sequence — handed to Sim.Gameplay.CheckpointManager (visual construction and race-state tracking stay separate, per the project's layering rules).</summary>
    public sealed class CheckpointDefinition
    {
        public int Index { get; }
        public string ObstacleId { get; }
        public Vector3 WorldPosition { get; }

        public CheckpointDefinition(int index, string obstacleId, Vector3 worldPosition)
        {
            Index = index;
            ObstacleId = obstacleId;
            WorldPosition = worldPosition;
        }
    }
}
