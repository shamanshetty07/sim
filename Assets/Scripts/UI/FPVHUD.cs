using UnityEngine;
using Sim.Drone;

namespace Sim.UI
{
    /// <summary>
    /// Coordinates the FPV HUD. This is the only class in the UI layer that talks to
    /// DroneController — TelemetryUI never reaches back into the drone, keeping the data
    /// flow strictly Input -> Drone -> Telemetry -> UI and never the reverse.
    ///
    /// Wiring: the initial drone reference is a serialized field (<see cref="_droneController"/>)
    /// so Editor tooling / the Inspector can assign it and have that assignment survive a
    /// scene save — a plain runtime field set only via a method call would not, since Unity's
    /// serializer only persists [SerializeField] fields, and C# event subscriptions are never
    /// serialized at all regardless. Awake() performs the actual event subscription fresh
    /// every time the scene loads/Play begins, from whatever _droneController was assigned to.
    ///
    /// SetDroneController is the runtime re-wiring path (e.g. Phase 7+ destroying and
    /// recreating the drone when a world regenerates): it always unsubscribes from whatever
    /// it was previously listening to before subscribing to the new one, and accepts null to
    /// cleanly detach. That makes it safe to call repeatedly, with the same controller, a
    /// different one, or none, without ever double-subscribing or leaving a stale
    /// subscription on a DroneController that no longer exists.
    /// </summary>
    [RequireComponent(typeof(TelemetryUI))]
    public class FPVHUD : MonoBehaviour
    {
        [Tooltip("Assigned by Editor tooling or the Inspector. Re-wire at runtime via SetDroneController, not by editing this directly.")]
        [SerializeField] private DroneController _droneController;

        [SerializeField] private TelemetryUI _telemetryUI;

        // Exponential-decay smoothing speed for the FPS readout (see CameraSmoothing for the
        // same technique) — a raw instantaneous 1/deltaTime value jitters too much to read.
        private const float FpsSmoothingSpeed = 10f;

        private DroneController _subscribedController;
        private float _smoothedFps;

        private void Awake()
        {
            if (_telemetryUI == null) _telemetryUI = GetComponent<TelemetryUI>();
            SetDroneController(_droneController);
        }

        /// <summary>(Re-)targets the HUD at a DroneController, safely detaching from any previous one first. Pass null to detach entirely.</summary>
        public void SetDroneController(DroneController controller)
        {
            if (_subscribedController != null)
                _subscribedController.TelemetryUpdated -= HandleTelemetryUpdated;

            _droneController = controller;
            _subscribedController = controller;

            if (_subscribedController != null)
                _subscribedController.TelemetryUpdated += HandleTelemetryUpdated;
        }

        private void OnDestroy()
        {
            // Defensive: guarantees this HUD never leaves a subscription on a DroneController
            // that outlives it, even if something tears the HUD down without calling
            // SetDroneController(null) first.
            if (_subscribedController != null)
                _subscribedController.TelemetryUpdated -= HandleTelemetryUpdated;
        }

        private void HandleTelemetryUpdated(FlightTelemetry telemetry) => _telemetryUI.UpdateTelemetry(telemetry);

        // FPS is a rendering-time concern, not a physics one, so it's computed here once per
        // rendered frame rather than folded into FlightTelemetry (which updates at the
        // physics rate, a different frequency entirely). No per-frame allocations: just a
        // float smoothed in place.
        private void Update()
        {
            float instantFps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            float t = 1f - Mathf.Exp(-FpsSmoothingSpeed * Time.unscaledDeltaTime);
            _smoothedFps = Mathf.Lerp(_smoothedFps, instantFps, t);
            _telemetryUI.UpdateFps(_smoothedFps);
        }
    }
}
