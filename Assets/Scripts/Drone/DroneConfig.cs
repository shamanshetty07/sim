using UnityEngine;

namespace Sim.Drone
{
    /// <summary>
    /// All tunable parameters for one drone "airframe". Kept as a ScriptableObject
    /// (per the project's coding rules) so designers can create multiple presets
    /// (e.g. "5-inch freestyle", "3-inch whoop") without touching code, and so
    /// DroneFlightModel/DronePhysics stay free of magic numbers.
    ///
    /// Defaults approximate a typical 5" FPV freestyle quad. They are reasonable
    /// starting points, not physically derived — playtest and retune in the Editor.
    /// </summary>
    [CreateAssetMenu(fileName = "DroneConfig", menuName = "FPV Sim/Drone Config")]
    public class DroneConfig : ScriptableObject
    {
        [Header("Mass & Thrust")]
        [Tooltip("Airframe mass in kilograms.")]
        [Min(0.05f)] public float Mass = 0.5f;

        [Tooltip("Maximum combined thrust all motors can produce, in newtons, at 100% throttle. " +
                 "A thrust-to-weight ratio of 2:1-4:1 (MaxThrustForce / (Mass * 9.81)) is typical for FPV freestyle/race quads.")]
        [Min(0.1f)] public float MaxThrustForce = 18f;

        [Tooltip("Shapes throttle -> thrust beyond the stick expo curve. Default is linear (straight diagonal). " +
                 "Bend it to change how thrust builds through the throttle range, e.g. for a softer feel near hover.")]
        public AnimationCurve ThrottleCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Air Resistance")]
        [Tooltip("Linear (speed-proportional) drag coefficient opposing the drone's linear velocity.")]
        [Min(0f)] public float LinearDragCoefficient = 0.15f;

        [Tooltip("Quadratic (speed-squared-proportional) drag coefficient. Dominates at higher speeds " +
                 "and is what actually caps top speed, since linear drag alone grows too slowly.")]
        [Min(0f)] public float QuadraticDragCoefficient = 0.02f;

        [Tooltip("Extra angular damping opposing angular velocity directly (propwash/airframe resistance), " +
                 "on top of the rate-loop's own implicit damping.")]
        [Min(0f)] public float AngularDragCoefficient = 0.02f;

        [Header("Angle Mode")]
        [Tooltip("Maximum pitch/roll tilt angle (degrees) the drone will hold at full stick deflection in Angle mode.")]
        [Range(5f, 70f)] public float MaxTiltAngleDeg = 35f;

        [Tooltip("Outer-loop gain converting a pitch/roll angle error (deg) into a target rate (deg/s). " +
                 "Higher = snappier self-leveling, too high = oscillation.")]
        [Min(0f)] public float AngleGainP = 8f;

        [Header("Acro Mode Rates")]
        [Tooltip("Maximum pitch/roll angular rate (deg/s) at full stick deflection in Acro mode.")]
        [Min(10f)] public float MaxPitchRollRateDegPerSec = 540f;

        [Tooltip("Maximum yaw angular rate (deg/s) at full stick deflection. Yaw is always rate-controlled, " +
                 "in every flight mode, matching how real flight controllers treat yaw.")]
        [Min(10f)] public float MaxYawRateDegPerSec = 270f;

        [Header("Rate Loop Gains (inner loop, all modes)")]
        [Tooltip("Gain converting a pitch rate error (target - current, deg/s) into pitch torque.")]
        [Min(0f)] public float PitchRateGain = 0.03f;

        [Tooltip("Gain converting a roll rate error (target - current, deg/s) into roll torque.")]
        [Min(0f)] public float RollRateGain = 0.03f;

        [Tooltip("Gain converting a yaw rate error (target - current, deg/s) into yaw torque.")]
        [Min(0f)] public float YawRateGain = 0.02f;

        [Tooltip("Extra yaw damping proportional to current yaw angular velocity, independent of the rate-error term. " +
                 "Called out separately because yaw tends to oscillate/wag without it, even with a correct rate PID.")]
        [Min(0f)] public float YawDamping = 0.01f;

        [Header("Horizon Mode")]
        [Tooltip("Below this stick deflection (0-1) Horizon behaves like pure Angle mode; above it, blends toward Acro. " +
                 "Kept separate from a hard 0-1 lerp so small corrections near center stay locked to the horizon.")]
        [Range(0f, 1f)] public float HorizonBlendStart = 0f;

        [Header("Input Shaping")]
        [Tooltip("Stick input below this magnitude (0-1) is treated as zero. Filters out gamepad stick noise/drift.")]
        [Range(0f, 0.3f)] public float StickDeadzone = 0.05f;

        [Tooltip("Expo curve applied to pitch/roll/yaw stick input (0 = linear, 1 = strong cubic softening near center).")]
        [Range(0f, 1f)] public float StickExpo = 0.35f;

        [Tooltip("Expo curve applied to throttle input.")]
        [Range(0f, 1f)] public float ThrottleExpo = 0.2f;

        [Tooltip("Throttle change per second when using the keyboard fallback's held-key throttle (units: 0-1 per second).")]
        [Min(0.05f)] public float KeyboardThrottleRatePerSecond = 0.6f;

        [Header("Safety")]
        [Tooltip("Arming is refused if throttle input is above this value, to avoid an accidental throttle-up on arm.")]
        [Range(0f, 0.5f)] public float MaxThrottleToArm = 0.05f;
    }
}
