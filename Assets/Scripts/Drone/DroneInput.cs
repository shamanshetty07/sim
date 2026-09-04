using UnityEngine;
using UnityEngine.InputSystem;

namespace Sim.Drone
{
    /// <summary>
    /// Raw stick sample for one frame, already shaped (deadzone + expo applied) but not
    /// yet interpreted by a flight mode. Pitch/Roll/Yaw are -1..1, Throttle is 0..1.
    /// </summary>
    public readonly struct DroneInputSample
    {
        public readonly float Throttle;
        public readonly float Pitch;
        public readonly float Roll;
        public readonly float Yaw;
        public readonly bool CycleModeRequested;
        public readonly bool ToggleArmRequested;
        public readonly bool ResetRequested;

        public DroneInputSample(float throttle, float pitch, float roll, float yaw,
            bool cycleModeRequested, bool toggleArmRequested, bool resetRequested)
        {
            Throttle = throttle;
            Pitch = pitch;
            Roll = roll;
            Yaw = yaw;
            CycleModeRequested = cycleModeRequested;
            ToggleArmRequested = toggleArmRequested;
            ResetRequested = resetRequested;
        }
    }

    /// <summary>
    /// Reads RC-style stick input for the drone. Uses Unity's Input System package
    /// (Gamepad/Keyboard device classes) rather than the legacy Input Manager.
    ///
    /// Source priority: a connected Gamepad is treated as the RC controller; the
    /// keyboard is always polled as a fallback so the drone is flyable with no
    /// controller attached. Gamepad and keyboard axes are summed and clamped, so
    /// nudging the keyboard while flying with a pad also works (handy for testing).
    ///
    /// Stick mapping is RC "Mode 2" (the most common transmitter layout):
    ///   Left stick  = Throttle (Y) + Yaw (X)
    ///   Right stick = Pitch (Y) + Roll (X)
    /// Throttle uses the right trigger where available instead of the left stick's Y
    /// axis, because a trigger rests at 0 like a real non-centering throttle stick —
    /// a spring-centered thumbstick would default to "50% throttle" at rest.
    /// </summary>
    public class DroneInput : MonoBehaviour
    {
        [SerializeField] private DroneConfig _config;

        private float _keyboardThrottle;

        public void Configure(DroneConfig config) => _config = config;

        /// <summary>Samples all input sources for this step and returns shaped stick values.</summary>
        public DroneInputSample Sample(float deltaTime)
        {
            var gamepad = Gamepad.current;
            var keyboard = Keyboard.current;

            float rawThrottle = 0f;
            float rawPitch = 0f;
            float rawRoll = 0f;
            float rawYaw = 0f;
            bool cycleMode = false;
            bool toggleArm = false;
            bool reset = false;

            if (gamepad != null)
            {
                rawThrottle += gamepad.rightTrigger.ReadValue();
                rawYaw += gamepad.leftStick.x.ReadValue();
                rawPitch += gamepad.rightStick.y.ReadValue();
                rawRoll += gamepad.rightStick.x.ReadValue();
                cycleMode |= gamepad.buttonNorth.wasPressedThisFrame;
                toggleArm |= gamepad.startButton.wasPressedThisFrame;
                reset |= gamepad.selectButton.wasPressedThisFrame;
            }

            if (keyboard != null)
            {
                // Non-centering keyboard throttle: ramps while held, holds value when released.
                if (keyboard.spaceKey.isPressed)
                    _keyboardThrottle += _config.KeyboardThrottleRatePerSecond * deltaTime;
                else if (keyboard.leftCtrlKey.isPressed)
                    _keyboardThrottle -= _config.KeyboardThrottleRatePerSecond * deltaTime;
                _keyboardThrottle = Mathf.Clamp01(_keyboardThrottle);
                rawThrottle += _keyboardThrottle;

                // W = positive pitch input = nose-down/forward (see DroneFlightModel's pitch
                // sign inversion), S = nose-up/back — matches docs/DRONE_PHYSICS.md's control table.
                if (keyboard.wKey.isPressed) rawPitch += 1f;
                if (keyboard.sKey.isPressed) rawPitch -= 1f;
                if (keyboard.dKey.isPressed) rawRoll += 1f;
                if (keyboard.aKey.isPressed) rawRoll -= 1f;
                if (keyboard.eKey.isPressed) rawYaw += 1f;
                if (keyboard.qKey.isPressed) rawYaw -= 1f;

                cycleMode |= keyboard.tabKey.wasPressedThisFrame;
                toggleArm |= keyboard.backspaceKey.wasPressedThisFrame;
                reset |= keyboard.rKey.wasPressedThisFrame;
            }

            float throttle = ShapeUnsigned(Mathf.Clamp01(rawThrottle), _config.StickDeadzone, _config.ThrottleExpo);
            float pitch = ShapeSigned(Mathf.Clamp(rawPitch, -1f, 1f), _config.StickDeadzone, _config.StickExpo);
            float roll = ShapeSigned(Mathf.Clamp(rawRoll, -1f, 1f), _config.StickDeadzone, _config.StickExpo);
            float yaw = ShapeSigned(Mathf.Clamp(rawYaw, -1f, 1f), _config.StickDeadzone, _config.StickExpo);

            return new DroneInputSample(throttle, pitch, roll, yaw, cycleMode, toggleArm, reset);
        }

        /// <summary>Resets the keyboard's stateful throttle accumulator (e.g. on disarm or drone reset).</summary>
        public void ResetKeyboardThrottle(float value = 0f) => _keyboardThrottle = Mathf.Clamp01(value);

        /// <summary>
        /// Applies a deadzone (input below it reads as zero, output still reaches ±1 at full
        /// deflection) then an expo curve (cubic blend) that softens response near center while
        /// leaving full-stick output unchanged. Standard RC transmitter shaping.
        /// </summary>
        private static float ShapeSigned(float raw, float deadzone, float expo)
        {
            float sign = Mathf.Sign(raw);
            float mag = Mathf.Abs(raw);
            if (mag < deadzone) return 0f;

            mag = Mathf.Clamp01((mag - deadzone) / (1f - deadzone));
            float shaped = (1f - expo) * mag + expo * mag * mag * mag;
            return sign * shaped;
        }

        /// <summary>Same shaping as <see cref="ShapeSigned"/> but for a unipolar 0..1 input like throttle.</summary>
        private static float ShapeUnsigned(float raw, float deadzone, float expo)
        {
            float mag = Mathf.Clamp01(raw);
            if (mag < deadzone) return 0f;

            mag = Mathf.Clamp01((mag - deadzone) / (1f - deadzone));
            return (1f - expo) * mag + expo * mag * mag * mag;
        }
    }
}
