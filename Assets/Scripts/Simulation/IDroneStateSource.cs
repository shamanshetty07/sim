using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>
    /// The one thing DroneRecoveryController needs to *read* from the drone: its current
    /// world-space position/rotation. Deliberately a separate interface from IDroneSpawnTarget
    /// (which only ever *writes* — "place the drone here") rather than added to it, so every
    /// existing IDroneSpawnTarget-only fake across Phase 9/11's tests is completely unaffected
    /// by this Phase 12 addition. DroneControllerSpawnTarget implements both — the same single
    /// adapter over DroneController, not a second drone abstraction.
    /// </summary>
    public interface IDroneStateSource
    {
        Vector3 Position { get; }
        Quaternion Rotation { get; }
    }
}
