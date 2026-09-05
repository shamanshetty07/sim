using Sim.Drone;
using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>
    /// Production IDroneSpawnTarget/IDroneStateSource — wraps the existing DroneController.
    /// SetSpawn/ResetToSpawn (Phase 3) for placement, and DroneController.transform (already a
    /// public Unity API on any Component — no new drone API needed) for the read side Phase
    /// 12's DroneRecoveryController needs. Does not touch DronePhysics/DroneInput/flight logic
    /// directly; goes through the same public API the Editor tooling and Phase 3 "R to reset"
    /// behaviour already use. One adapter, two small capabilities — not a second drone
    /// abstraction.
    /// </summary>
    public sealed class DroneControllerSpawnTarget : IDroneSpawnTarget, IDroneStateSource
    {
        private readonly DroneController _droneController;

        public Vector3 Position => _droneController != null ? _droneController.transform.position : Vector3.zero;
        public Quaternion Rotation => _droneController != null ? _droneController.transform.rotation : Quaternion.identity;

        public DroneControllerSpawnTarget(DroneController droneController)
        {
            _droneController = droneController;
        }

        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            if (_droneController == null) return;

            _droneController.SetSpawn(position, rotation);
            _droneController.ResetToSpawn();
        }
    }
}
