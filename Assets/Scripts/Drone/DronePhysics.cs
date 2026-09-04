using UnityEngine;

namespace Sim.Drone
{
    /// <summary>
    /// Applies a FlightOutput (thrust + torque) to the drone's Rigidbody every FixedUpdate,
    /// and adds air resistance. This is the only script in the drone stack that touches
    /// Rigidbody directly — DroneFlightModel stays pure math, DroneController stays
    /// orchestration. All motion comes from AddForce/AddRelativeTorque; nothing here ever
    /// writes transform.position or transform.rotation directly.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DronePhysics : MonoBehaviour
    {
        [SerializeField] private DroneConfig _config;

        private Rigidbody _rigidbody;

        public Rigidbody Rigidbody => _rigidbody;

        /// <summary>
        /// Assigns (or re-assigns) the config and immediately re-applies its Rigidbody-facing
        /// values (currently just Mass). Safe to call before or after Awake — Unity does not
        /// guarantee Awake ordering between sibling components, so DroneController.Awake()
        /// calling this to propagate a config assigned only on itself must not depend on
        /// this component's own Awake having already run.
        /// </summary>
        public void Configure(DroneConfig config)
        {
            _config = config;
            if (_rigidbody != null && _config != null)
                _rigidbody.mass = _config.Mass;
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.useGravity = true;
            // Drag is modeled manually below (linear + quadratic + explicit angular damping)
            // so it can be tuned per-axis and per-speed-regime; the built-in Rigidbody drag
            // terms are zeroed to avoid double-damping the same motion twice.
            // Unity 2022.3 LTS Rigidbody API: "drag"/"angularDrag" (renamed to
            // linearDamping/angularDamping starting in Unity 6 — update these two lines
            // if this project is ever moved to a Unity 6.x baseline).
            _rigidbody.drag = 0f;
            _rigidbody.angularDrag = 0f;
            // Small, fast-moving rigidbody: continuous collision detection avoids tunneling
            // through thin obstacles (gates, walls) at racing speeds.
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (_config != null) _rigidbody.mass = _config.Mass;
        }

        /// <summary>Reads the Rigidbody's current attitude/rates for DroneFlightModel to consume.</summary>
        public DroneAttitudeState ReadAttitude()
        {
            // Pitch/roll are derived from how far the body's forward/right axes tilt away
            // from world up. Valid for the ±90 degree range Angle/Horizon self-leveling
            // actually operates in; Acro mode never reads this (open-loop rate control),
            // so the ambiguity at extreme/inverted attitudes never affects it.
            float pitchDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(transform.forward, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
            float rollDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(transform.right, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;

            // Rigidbody.angularVelocity is world-space radians/second. InverseTransformDirection
            // rotates it into body-space (still radians/second) without any small-angle
            // approximation; converting to degrees/second matches the rest of this module's units.
            Vector3 localAngularVelocityRad = transform.InverseTransformDirection(_rigidbody.angularVelocity);
            Vector3 localAngularVelocityDeg = localAngularVelocityRad * Mathf.Rad2Deg;

            return new DroneAttitudeState(pitchDeg, rollDeg, localAngularVelocityDeg);
        }

        /// <summary>Applies one step's flight output plus air resistance. Call from FixedUpdate only.</summary>
        public void Apply(FlightOutput output)
        {
            _rigidbody.AddForce(transform.up * output.ThrustForceNewtons, ForceMode.Force);
            _rigidbody.AddRelativeTorque(output.LocalTorque, ForceMode.Force);
            ApplyAirResistance();
        }

        /// <summary>
        /// Manual drag model: a linear term (dominant at low speed) plus a quadratic term
        /// (dominant at high speed, this is what actually caps top speed — real aerodynamic
        /// drag scales with velocity squared) opposing linear velocity, plus simple angular
        /// damping opposing spin beyond what the rate loop already removes.
        /// </summary>
        private void ApplyAirResistance()
        {
            if (_config == null) return;

            Vector3 velocity = _rigidbody.velocity;
            float speed = velocity.magnitude;
            if (speed > 0.0001f)
            {
                float dragMagnitude = _config.LinearDragCoefficient * speed
                                       + _config.QuadraticDragCoefficient * speed * speed;
                _rigidbody.AddForce(-velocity.normalized * dragMagnitude, ForceMode.Force);
            }

            _rigidbody.AddTorque(-_rigidbody.angularVelocity * _config.AngularDragCoefficient, ForceMode.Force);
        }

        /// <summary>Hard reset used for the "R" debug key / respawn — zeroes all motion, not just position.</summary>
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }
    }
}
