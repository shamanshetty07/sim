using System;
using UnityEngine;

namespace Sim.Drone
{
    /// <summary>
    /// Owns the drone's flight-state: which FlightMode is active and whether it's armed.
    /// Deliberately has no physics/input-shaping knowledge of its own — DroneController
    /// feeds it raw mode-switch/arm requests from DroneInput, and DroneFlightModel reads
    /// its Mode/Armed state back out. Keeping this as its own small class (rather than
    /// folding it into DroneController) is what lets the arm/mode rules be unit tested
    /// without touching Rigidbody or MonoBehaviour lifecycle at all.
    /// </summary>
    public class FlightModeController
    {
        private readonly DroneConfig _config;
        private readonly FlightMode[] _cycleOrder = { FlightMode.Angle, FlightMode.Horizon, FlightMode.Acro };
        private int _cycleIndex;

        public FlightMode Mode => _cycleOrder[_cycleIndex];
        public bool Armed { get; private set; }

        /// <summary>Raised whenever the flight mode changes, for UI/OSD to react without polling.</summary>
        public event Action<FlightMode> ModeChanged;

        /// <summary>Raised whenever armed state changes.</summary>
        public event Action<bool> ArmedChanged;

        public FlightModeController(DroneConfig config)
        {
            _config = config;
        }

        /// <summary>Advances to the next flight mode in the cycle (Angle -> Horizon -> Acro -> Angle...).</summary>
        public void CycleMode()
        {
            _cycleIndex = (_cycleIndex + 1) % _cycleOrder.Length;
            ModeChanged?.Invoke(Mode);
        }

        /// <summary>
        /// Toggles armed state. Arming is refused while throttle is above
        /// <see cref="DroneConfig.MaxThrottleToArm"/>, to avoid the drone lurching the instant
        /// it arms because the pilot's throttle stick/key was already partway up.
        /// Disarming is always allowed immediately (it's the safety-critical direction).
        /// </summary>
        /// <returns>True if the arm/disarm request was accepted.</returns>
        public bool ToggleArmed(float currentThrottle01)
        {
            if (Armed)
            {
                Armed = false;
                ArmedChanged?.Invoke(false);
                return true;
            }

            if (currentThrottle01 > _config.MaxThrottleToArm)
                return false;

            Armed = true;
            ArmedChanged?.Invoke(true);
            return true;
        }

        /// <summary>Forces disarm, e.g. after a crash. Always succeeds.</summary>
        public void ForceDisarm()
        {
            if (!Armed) return;
            Armed = false;
            ArmedChanged?.Invoke(false);
        }
    }
}
