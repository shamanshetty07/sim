using UnityEngine;

namespace Sim.Drone
{
    /// <summary>
    /// The force/torque this step's inputs produce. In local (body) space —
    /// DronePhysics is responsible for applying it to the Rigidbody.
    /// </summary>
    public readonly struct FlightOutput
    {
        public readonly float ThrustForceNewtons;
        public readonly Vector3 LocalTorque;

        public FlightOutput(float thrustForceNewtons, Vector3 localTorque)
        {
            ThrustForceNewtons = thrustForceNewtons;
            LocalTorque = localTorque;
        }

        public static readonly FlightOutput Zero = new FlightOutput(0f, Vector3.zero);
    }

    /// <summary>
    /// The current attitude/rate state DroneFlightModel needs, extracted by the caller
    /// (DronePhysics) from the Rigidbody each step. Kept as a plain struct — separate
    /// from any Rigidbody/Transform reference — so this whole model is usable and unit
    /// -testable from EditMode tests with no scene, no Play mode, no physics step at all.
    /// </summary>
    public readonly struct DroneAttitudeState
    {
        /// <summary>Current pitch, degrees. Positive = nose up. See DroneFlightModel remarks for axis convention.</summary>
        public readonly float PitchDeg;

        /// <summary>Current roll, degrees. Positive = right side up (banked left).</summary>
        public readonly float RollDeg;

        /// <summary>Current body-space angular velocity, degrees/second per local axis (x=pitch, y=yaw, z=roll).</summary>
        public readonly Vector3 LocalAngularVelocityDegPerSec;

        public DroneAttitudeState(float pitchDeg, float rollDeg, Vector3 localAngularVelocityDegPerSec)
        {
            PitchDeg = pitchDeg;
            RollDeg = rollDeg;
            LocalAngularVelocityDegPerSec = localAngularVelocityDegPerSec;
        }
    }

    /// <summary>
    /// Pure flight-dynamics math: given shaped stick input, the active flight mode, the
    /// drone's current attitude/rates and its DroneConfig, computes the thrust and torque
    /// to apply this step. Has no Unity physics dependency (no Rigidbody, no MonoBehaviour) —
    /// only UnityEngine.Vector3/Mathf for the math — so the same computation used at runtime
    /// is exactly what EditMode tests exercise.
    ///
    /// Axis convention (Unity, left-handed, Y-up):
    ///   Local X axis = pitch axis (rotating around it tips the nose up/down)
    ///   Local Y axis = yaw axis   (rotating around it turns the nose left/right)
    ///   Local Z axis = roll axis  (rotating around it banks left/right; Z is "forward")
    ///
    /// Every mode runs the same cascaded control structure real flight controllers use:
    /// an outer loop turns stick/attitude into a *target angular rate*, then a single inner
    /// rate loop (this class) turns (target rate - current rate) into torque. Acro mode's
    /// "outer loop" is just the identity (stick maps straight to target rate); Angle mode's
    /// outer loop is a P controller on angle error; Horizon blends the two. Yaw never gets
    /// an angle outer loop in any mode — real flight controllers don't hold a yaw heading
    /// either, they only ever rate-control yaw.
    /// </summary>
    public static class DroneFlightModel
    {
        public static FlightOutput Compute(
            DroneInputSample input,
            FlightMode mode,
            DroneAttitudeState attitude,
            DroneConfig config)
        {
            if (config == null) return FlightOutput.Zero;

            float thrust = config.MaxThrustForce * config.ThrottleCurve.Evaluate(Mathf.Clamp01(input.Throttle));

            float targetPitchRateDegPerSec = ComputePitchRollTargetRate(
                input.Pitch, attitude.PitchDeg, mode, config, invert: true);
            float targetRollRateDegPerSec = ComputePitchRollTargetRate(
                input.Roll, attitude.RollDeg, mode, config, invert: false);

            // Yaw is always pure rate control: stick deflection maps directly to a target
            // angular rate, with no self-leveling in any flight mode.
            float targetYawRateDegPerSec = input.Yaw * config.MaxYawRateDegPerSec;

            float pitchRateError = targetPitchRateDegPerSec - attitude.LocalAngularVelocityDegPerSec.x;
            float rollRateError = targetRollRateDegPerSec - attitude.LocalAngularVelocityDegPerSec.z;
            float yawRateError = targetYawRateDegPerSec - attitude.LocalAngularVelocityDegPerSec.y;

            float pitchTorque = pitchRateError * config.PitchRateGain;
            float rollTorque = rollRateError * config.RollRateGain;

            // Yaw gets an additional damping term proportional to its own current rate, on
            // top of the rate-error term above — without it yaw tends to "wag" because a
            // single P term on rate error alone under-damps the yaw axis in practice.
            float yawTorque = yawRateError * config.YawRateGain
                               - attitude.LocalAngularVelocityDegPerSec.y * config.YawDamping;

            return new FlightOutput(thrust, new Vector3(pitchTorque, yawTorque, rollTorque));
        }

        /// <summary>
        /// Computes the target angular rate (deg/s) for the pitch or roll axis, according to
        /// the active flight mode. <paramref name="invert"/> flips the sign for pitch, because
        /// "stick forward" (positive input) should pitch the nose *down* to move the drone
        /// forward, which is a *negative* rotation around the pitch axis in this convention.
        /// </summary>
        private static float ComputePitchRollTargetRate(
            float stickInput, float currentAngleDeg, FlightMode mode, DroneConfig config, bool invert)
        {
            float sign = invert ? -1f : 1f;

            float acroRate = stickInput * config.MaxPitchRollRateDegPerSec * sign;

            if (mode == FlightMode.Acro)
                return acroRate;

            float targetAngleDeg = stickInput * config.MaxTiltAngleDeg * sign;
            float angleError = targetAngleDeg - currentAngleDeg;
            float angleModeRate = Mathf.Clamp(
                angleError * config.AngleGainP,
                -config.MaxPitchRollRateDegPerSec,
                config.MaxPitchRollRateDegPerSec);

            if (mode == FlightMode.Angle)
                return angleModeRate;

            // Horizon: blend from pure self-leveling near center stick to full acro-style
            // rate at full deflection, so small corrections stay locked to the horizon while
            // a full-deflection flip/roll still gets acro's unrestricted rotation rate.
            float deflection = Mathf.Clamp01(Mathf.Abs(stickInput));
            float blend = Mathf.Clamp01(Mathf.InverseLerp(config.HorizonBlendStart, 1f, deflection));
            return Mathf.Lerp(angleModeRate, acroRate, blend);
        }
    }
}
