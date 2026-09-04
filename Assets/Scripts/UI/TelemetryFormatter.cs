using UnityEngine;
using Sim.Drone;

namespace Sim.UI
{
    /// <summary>
    /// Pure string formatting for OSD telemetry values. No MonoBehaviour, no TextMeshPro
    /// dependency, no reference to DroneController — takes primitives/telemetry values in,
    /// returns display strings out. This is what keeps TelemetryUI a thin wiring layer and
    /// makes formatting behaviour unit-testable without a scene.
    /// </summary>
    public static class TelemetryFormatter
    {
        public static string FormatMode(FlightMode mode) => mode.ToString().ToUpperInvariant();

        public static string FormatArmed(bool armed) => armed ? "ARMED" : "DISARMED";

        public static string FormatPercent(float value01) => $"{Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";

        public static string FormatMeters(float meters) => $"{meters:F1} m";

        public static string FormatSpeed(float metersPerSecond) => $"{metersPerSecond:F1} m/s";

        /// <summary>Always signed (+/-) so climb vs. descent is unambiguous at a glance.</summary>
        public static string FormatVerticalSpeed(float metersPerSecond)
        {
            string sign = metersPerSecond >= 0f ? "+" : "";
            return $"{sign}{metersPerSecond:F1} m/s";
        }

        public static string FormatDegrees(float degrees) => $"{degrees:F0}°";

        public static string FormatAngularSpeed(float degreesPerSecond) => $"{degreesPerSecond:F0} °/s";

        public static string FormatFps(float fps) => $"{Mathf.RoundToInt(fps)} FPS";
    }
}
