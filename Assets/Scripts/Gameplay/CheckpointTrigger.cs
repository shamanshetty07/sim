using Sim.Drone;
using UnityEngine;

namespace Sim.Gameplay
{
    /// <summary>
    /// Sits on a small trigger-only volume at one checkpoint's position (added by
    /// ObstacleGenerator, separate from that obstacle's own blocking colliders — see
    /// ObstacleGenerator remarks). Deliberately knows nothing about race state; it only
    /// reports "the drone passed through index N" to whichever CheckpointManager wires
    /// itself in via <see cref="SetManager"/> — visual/trigger construction
    /// (ObstacleGenerator) and race-state tracking (CheckpointManager) stay fully separate.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CheckpointTrigger : MonoBehaviour
    {
        [SerializeField] private int _checkpointIndex;

        private CheckpointManager _manager;

        public int CheckpointIndex => _checkpointIndex;

        public void Configure(int checkpointIndex) => _checkpointIndex = checkpointIndex;

        public void SetManager(CheckpointManager manager) => _manager = manager;

        private void OnTriggerEnter(Collider other)
        {
            if (_manager == null) return;

            // Identify the drone via its DroneController component rather than a tag string —
            // tags require project-level tag definitions we can't guarantee exist at
            // generation time, and a component check is just as reliable.
            if (other.GetComponentInParent<DroneController>() != null)
                _manager.ReportCheckpointPassed(_checkpointIndex);
        }
    }
}
