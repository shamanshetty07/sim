using System;
using UnityEngine;

namespace Sim.Gameplay
{
    /// <summary>
    /// Prototype-sensible defaults for DroneRecoveryController. A plain [Serializable] class
    /// (not a ScriptableObject) — small enough that a dedicated asset would be overkill, per
    /// this phase's explicit "do not create a giant settings framework" instruction.
    /// Deliberately has no maximum-altitude field: this phase's brief is explicit that an
    /// arbitrary max altitude must not be imposed unless the world/course actually defines one,
    /// and WorldSpecification/CourseSpecification do not — see docs/PHASE_12_RECOVERY.md
    /// "Why no max altitude" for the full reasoning. Adding one would need a new
    /// WorldSpecification field, which is out of this phase's scope.
    /// </summary>
    [Serializable]
    public sealed class DroneRecoveryConfig
    {
        [Tooltip("Master switch. When false, DroneRecoveryController.Tick() never recovers anything — manual Reset (CourseGameplayController.Reset()) is a completely separate action and keeps working regardless.")]
        public bool Enabled = true;

        [Tooltip("Meters beyond the generated terrain's actual horizontal footprint before the drone counts as out of bounds. Keeps the recovery boundary from coinciding with the visual terrain edge.")]
        public float RecoveryMargin = 25f;

        [Tooltip("Meters below the sampled ground height at the drone's current X/Z before it counts as having fallen through/below the world.")]
        public float BelowWorldMargin = 15f;

        [Tooltip("How long a horizontal/below-world violation must persist before recovery actually triggers — debounces a single noisy/transient frame. Non-finite (NaN/Infinity) positions skip this and recover immediately, regardless of this value.")]
        public float ConfirmationDurationSeconds = 0.5f;

        [Tooltip("How long checkpoint processing stays suppressed and monitoring pauses after a recovery completes, so the drone can stabilize at its spawn before another recovery can trigger.")]
        public float CooldownDurationSeconds = 1.5f;
    }
}
