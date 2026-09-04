using TMPro;
using UnityEngine;
using Sim.Drone;

namespace Sim.UI
{
    /// <summary>
    /// Converts a FlightTelemetry snapshot into displayed values: TextMeshPro fields plus the
    /// minimal artificial horizon bar. Presentation only — holds no reference to
    /// DroneController/Rigidbody/physics of any kind, and performs no physics math of its
    /// own; every value it shows is either copied straight from FlightTelemetry or formatted
    /// via the pure TelemetryFormatter. FPVHUD is the only thing that calls into this class;
    /// this class never reaches back out to the drone.
    /// </summary>
    public class TelemetryUI : MonoBehaviour
    {
        [Header("Flight State")]
        [SerializeField] private TextMeshProUGUI _modeText;
        [SerializeField] private TextMeshProUGUI _armedText;

        [Header("Motion")]
        [SerializeField] private TextMeshProUGUI _altitudeText;
        [SerializeField] private TextMeshProUGUI _speedText;
        [SerializeField] private TextMeshProUGUI _verticalSpeedText;
        [SerializeField] private TextMeshProUGUI _throttleText;

        [Header("Attitude")]
        [SerializeField] private TextMeshProUGUI _pitchText;
        [SerializeField] private TextMeshProUGUI _rollText;
        [SerializeField] private TextMeshProUGUI _yawText;
        [SerializeField] private TextMeshProUGUI _angularSpeedText;

        [Header("System")]
        [SerializeField] private TextMeshProUGUI _fpsText;

        [Header("Horizon Indicator (minimal)")]
        [Tooltip("RectTransform of a simple horizontal bar/line. Rotated by roll, offset vertically by pitch.")]
        [SerializeField] private RectTransform _horizonBar;
        [SerializeField] private float _pixelsPerDegreePitch = 4f;
        [SerializeField] private float _maxHorizonOffsetPixels = 120f;

        /// <summary>Applies one telemetry snapshot to every bound field. Called by FPVHUD, never by anything below the UI layer.</summary>
        public void UpdateTelemetry(in FlightTelemetry telemetry)
        {
            SetText(_modeText, TelemetryFormatter.FormatMode(telemetry.Mode));
            SetText(_armedText, TelemetryFormatter.FormatArmed(telemetry.Armed));
            SetText(_altitudeText, TelemetryFormatter.FormatMeters(telemetry.AltitudeMeters));
            SetText(_speedText, TelemetryFormatter.FormatSpeed(telemetry.TotalSpeedMetersPerSecond));
            SetText(_verticalSpeedText, TelemetryFormatter.FormatVerticalSpeed(telemetry.VerticalVelocityMetersPerSecond));
            SetText(_throttleText, TelemetryFormatter.FormatPercent(telemetry.ThrottlePercent01));
            SetText(_pitchText, TelemetryFormatter.FormatDegrees(telemetry.PitchDeg));
            SetText(_rollText, TelemetryFormatter.FormatDegrees(telemetry.RollDeg));
            SetText(_yawText, TelemetryFormatter.FormatDegrees(telemetry.YawDeg));
            SetText(_angularSpeedText, TelemetryFormatter.FormatAngularSpeed(telemetry.AngularSpeedDegPerSec));

            UpdateHorizonBar(telemetry);
        }

        /// <summary>Called once per rendered frame by FPVHUD with an already-smoothed FPS value — this class does no FPS math of its own.</summary>
        public void UpdateFps(float fps) => SetText(_fpsText, TelemetryFormatter.FormatFps(fps));

        private void UpdateHorizonBar(in FlightTelemetry telemetry)
        {
            if (_horizonBar == null) return;

            _horizonBar.localRotation = Quaternion.Euler(0f, 0f, -telemetry.RollDeg);

            float offset = Mathf.Clamp(-telemetry.PitchDeg * _pixelsPerDegreePitch, -_maxHorizonOffsetPixels, _maxHorizonOffsetPixels);
            Vector2 pos = _horizonBar.anchoredPosition;
            pos.y = offset;
            _horizonBar.anchoredPosition = pos;
        }

        private static void SetText(TextMeshProUGUI field, string value)
        {
            if (field != null) field.text = value;
        }
    }
}
