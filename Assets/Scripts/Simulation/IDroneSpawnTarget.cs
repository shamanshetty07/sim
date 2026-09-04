using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>
    /// The one thing WorldGenerationRuntimeService needs from the drone: "place it here."
    /// Its own interface specifically so that class is unit-testable without a real,
    /// Rigidbody-backed DroneController — building one of those outside Play mode is awkward
    /// (Awake() doesn't run for a component added via script in Edit mode, so
    /// DronePhysics/DroneInput never get their Rigidbody/config references — see
    /// DronePhysics's own Phase 3 remarks on this exact gap). A fake implementation can just
    /// record what it was asked to do. DroneControllerSpawnTarget is the real, production
    /// implementation — a thin adapter over DroneController.SetSpawn + ResetToSpawn, reusing
    /// that existing drone infrastructure rather than reimplementing it.
    /// </summary>
    public interface IDroneSpawnTarget
    {
        void PlaceAt(Vector3 position, Quaternion rotation);
    }
}
