using UnityEngine;

namespace Sim.Drone
{
    /// <summary>
    /// A read-only snapshot of the drone's flight state for a single frame/physics step.
    /// Deliberately a plain struct with no behaviour — the UI/OSD layer (Phase 4) consumes
    /// this without depending on DroneController, DronePhysics, or any Rigidbody internals.
    /// </summary>
    public readonly struct FlightTelemetry
    {
        public readonly FlightMode Mode;
        public readonly bool Armed;

        /// <summary>World-space height above the origin plane (transform.position.y). Not terrain-relative altitude.</summary>
        public readonly float AltitudeMeters;

        public readonly Vector3 WorldVelocity;
        public readonly float VerticalVelocityMetersPerSecond;
        public readonly float HorizontalSpeedMetersPerSecond;
        public readonly float TotalSpeedMetersPerSecond;

        public readonly float PitchDeg;
        public readonly float RollDeg;
        public readonly float YawDeg;

        /// <summary>0-1 shaped throttle actually applied this step (post deadzone/expo/curve).</summary>
        public readonly float ThrottlePercent01;

        public FlightTelemetry(
            FlightMode mode,
            bool armed,
            float altitudeMeters,
            Vector3 worldVelocity,
            float pitchDeg,
            float rollDeg,
            float yawDeg,
            float throttlePercent01)
        {
            Mode = mode;
            Armed = armed;
            AltitudeMeters = altitudeMeters;
            WorldVelocity = worldVelocity;
            VerticalVelocityMetersPerSecond = worldVelocity.y;
            HorizontalSpeedMetersPerSecond = new Vector2(worldVelocity.x, worldVelocity.z).magnitude;
            TotalSpeedMetersPerSecond = worldVelocity.magnitude;
            PitchDeg = pitchDeg;
            RollDeg = rollDeg;
            YawDeg = yawDeg;
            ThrottlePercent01 = throttlePercent01;
        }
    }
}
