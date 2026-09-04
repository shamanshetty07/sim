using Sim.Drone;
using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>Production IDroneSpawnTarget — wraps the existing DroneController.SetSpawn/ResetToSpawn (Phase 3). Does not touch DronePhysics/DroneInput/flight logic directly; goes through the same public API the Editor tooling and Phase 3 "R to reset" behaviour already use.</summary>
    public sealed class DroneControllerSpawnTarget : IDroneSpawnTarget
    {
        private readonly DroneController _droneController;

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
