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
    ///
    /// Phase 15: Mode/Armed/FPS are dirty-checked against their own last-displayed value before
    /// reformatting+reassigning — unlike altitude/speed/attitude (which genuinely change nearly
    /// every FixedUpdate during flight, so dirty-checking them buys little), Mode/Armed only
    /// ever change on an explicit discrete action (cycle-mode/arm/disarm), and the rounded FPS
    /// integer is frequently unchanged across many consecutive rendered frames once smoothed —
    /// so in the common case this now skips a string-interpolation allocation (TelemetryFormatter)
    /// and a UI text assignment for those three fields, with the displayed text unchanged in
    /// every case. Not applied to the continuously-varying fields — see docs/PHASE_15_PERFORMANCE.md.
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

        // Phase 15 dirty-check state — see class remarks. Nullable so the very first update
        // always paints regardless of what value happens to arrive first.
        private FlightMode? _lastDisplayedMode;
        private bool? _lastDisplayedArmed;
        private int? _lastDisplayedFps;

        /// <summary>Applies one telemetry snapshot to every bound field. Called by FPVHUD, never by anything below the UI layer.</summary>
        public void UpdateTelemetry(in FlightTelemetry telemetry)
        {
            if (_lastDisplayedMode != telemetry.Mode)
            {
                _lastDisplayedMode = telemetry.Mode;
                SetText(_modeText, TelemetryFormatter.FormatMode(telemetry.Mode));
            }

            if (_lastDisplayedArmed != telemetry.Armed)
            {
                _lastDisplayedArmed = telemetry.Armed;
                SetText(_armedText, TelemetryFormatter.FormatArmed(telemetry.Armed));
            }

            // Altitude/speed/attitude genuinely change on nearly every FixedUpdate during actual
            // flight, so dirty-checking these (unlike Mode/Armed/Fps above) would rarely skip
            // any work in the case that actually matters — always reformatted, unchanged from
            // before this phase.
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
        public void UpdateFps(float fps)
        {
            int rounded = Mathf.RoundToInt(fps);
            if (_lastDisplayedFps == rounded) return;

            _lastDisplayedFps = rounded;
            SetText(_fpsText, TelemetryFormatter.FormatFps(fps));
        }

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
