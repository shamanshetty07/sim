using UnityEngine;

namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// One FPV gameplay obstacle: a racing gate, ring, wall, pole, tunnel, checkpoint, or
    /// landing pad. <see cref="Type"/> is free-form for the same reason as
    /// ObjectSpecification.Category — Phase 10's obstacle generator maps recognized types to
    /// real behaviour (collision, checkpoint tracking) and falls back to a primitive
    /// placeholder for anything else, rather than rejecting an obstacle type it doesn't yet
    /// implement.
    /// </summary>
    public sealed class ObstacleSpecification
    {
        /// <summary>Stable identifier, e.g. "gate_01". Used for save/load and checkpoint tracking.</summary>
        public string Id { get; set; }

        /// <summary>Free-form: "gate", "ring", "wall", "pole", "tunnel", "checkpoint", "landing_pad", etc.</summary>
        public string Type { get; set; }

        public Vector3 Position { get; set; }
        public Vector3 RotationEuler { get; set; }
        public Vector3 Scale { get; set; } = Vector3.one;

        /// <summary>Position in the checkpoint sequence, if this obstacle is part of a race course. Null if it's decorative/non-gameplay.</summary>
        public int? CheckpointIndex { get; set; }
    }
}
