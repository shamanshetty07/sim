using System;
using UnityEngine;

namespace Sim.Drone
{
    /// <summary>
    /// Wires DroneInput -> FlightModeController -> DroneFlightModel -> DronePhysics
    /// together and publishes FlightTelemetry. This is the only script other systems
    /// (UI/OSD, race logic, save/load) should need a reference to; it deliberately does
    /// not expose the Rigidbody, DroneFlightModel, or raw input directly.
    ///
    /// All physics-affecting work happens in FixedUpdate, never Update, per the project's
    /// physics rules — including input sampling. At the default 50Hz fixed timestep this
    /// is not a practical problem for button taps (mode/arm/reset), but if the fixed
    /// timestep is ever lowered enough for FixedUpdate to run less often than human input
    /// events, button-edge detection (not the continuous stick axes) should move to Update
    /// and be latched for the next FixedUpdate to consume.
    /// </summary>
    [RequireComponent(typeof(DronePhysics))]
    [RequireComponent(typeof(DroneInput))]
    public class DroneController : MonoBehaviour
    {
        [SerializeField] private DroneConfig _config;

        private DronePhysics _physics;
        private DroneInput _input;
        private FlightModeController _flightModeController;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        /// <summary>Raised once per FixedUpdate with the latest telemetry, for UI/OSD/debug overlays.</summary>
        public event Action<FlightTelemetry> TelemetryUpdated;

        public FlightTelemetry CurrentTelemetry { get; private set; }
        public FlightMode CurrentMode => _flightModeController.Mode;
        public bool IsArmed => _flightModeController.Armed;

        private void Awake()
        {
            _physics = GetComponent<DronePhysics>();
            _input = GetComponent<DroneInput>();

            if (_config == null)
                Debug.LogError($"{nameof(DroneController)} on '{name}' has no DroneConfig assigned; flight will be inert.", this);

            _physics.Configure(_config);
            _input.Configure(_config);
            _flightModeController = new FlightModeController(_config);

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        /// <summary>
        /// Called by the world generator after placing the drone at a generated spawn
        /// point, so "reset" (the R key / a future respawn button) returns here rather
        /// than to the drone's original prefab-authoring position.
        /// </summary>
        public void SetSpawn(Vector3 position, Quaternion rotation)
        {
            _spawnPosition = position;
            _spawnRotation = rotation;
        }

        public void ResetToSpawn()
        {
            _physics.ResetTo(_spawnPosition, _spawnRotation);
            _input.ResetKeyboardThrottle();
            _flightModeController.ForceDisarm();
        }

        private void FixedUpdate()
        {
            if (_config == null) return;

            DroneInputSample sample = _input.Sample(Time.fixedDeltaTime);

            if (sample.CycleModeRequested)
                _flightModeController.CycleMode();

            if (sample.ToggleArmRequested)
                _flightModeController.ToggleArmed(sample.Throttle);

            if (sample.ResetRequested)
            {
                ResetToSpawn();
                return;
            }

            DroneAttitudeState attitude = _physics.ReadAttitude();

            // Disarmed: no thrust/torque is applied at all (motors off), rather than just
            // zeroing stick input, so the drone free-falls under gravity/drag exactly like
            // a real disarmed quad rather than hovering motionless.
            FlightOutput output = _flightModeController.Armed
                ? DroneFlightModel.Compute(sample, _flightModeController.Mode, attitude, _config)
                : FlightOutput.Zero;

            _physics.Apply(output);

            CurrentTelemetry = new FlightTelemetry(
                _flightModeController.Mode,
                _flightModeController.Armed,
                transform.position.y,
                _physics.Rigidbody.velocity,
                attitude.PitchDeg,
                attitude.RollDeg,
                transform.eulerAngles.y,
                output.ThrustForceNewtons / Mathf.Max(_config.MaxThrustForce, 0.0001f));

            TelemetryUpdated?.Invoke(CurrentTelemetry);
        }
    }
}
