using Sim.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI
{
    /// <summary>
    /// Course gameplay HUD — complements FPVHUD/TelemetryUI (altitude/velocity/mode/throttle),
    /// never replaces it. Contains NO gameplay logic of its own: every button handler only
    /// calls into CourseGameplayController, and every display value is a pure function
    /// (CourseStatusFormatter) of that controller's own state — same "UI ↓ controller, never
    /// the reverse" rule WorldGenerationUI already follows.
    ///
    /// Subscribes to CourseGameplayController's events for one-shot display updates (state
    /// changed, checkpoint passed, wrong checkpoint) and additionally polls
    /// ElapsedSeconds/CountdownRemainingSeconds once per rendered frame in Update() — the same
    /// pattern FPVHUD uses for its FPS readout — because a live timer/countdown has to visibly
    /// tick between events, not just redraw when something else happens.
    /// </summary>
    public sealed class CourseHUD : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _stateText;
        [SerializeField] private TextMeshProUGUI _checkpointText;
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _resetButton;

        private CourseGameplayController _controller;
        private float _messageClearAtUnscaledTime;

        private void Awake()
        {
            if (_startButton != null) _startButton.onClick.AddListener(OnStartClicked);
            if (_resetButton != null) _resetButton.onClick.AddListener(OnResetClicked);
        }

        private void OnDestroy() => Detach();

        /// <summary>
        /// (Re-)targets the HUD at a CourseGameplayController, safely detaching from any
        /// previous one first — same re-wiring pattern FPVHUD.SetDroneController and
        /// WorldGenerationUI.Initialize already use. Pass null to detach entirely.
        /// </summary>
        public void Initialize(CourseGameplayController controller)
        {
            Detach();

            _controller = controller;
            if (_controller == null) return;

            _controller.StateChanged += HandleStateChanged;
            _controller.CheckpointPassed += HandleCheckpointPassed;
            _controller.WrongCheckpointAttempted += HandleWrongCheckpointAttempted;

            UpdateStaticDisplay();
        }

        private void Detach()
        {
            if (_controller == null) return;

            _controller.StateChanged -= HandleStateChanged;
            _controller.CheckpointPassed -= HandleCheckpointPassed;
            _controller.WrongCheckpointAttempted -= HandleWrongCheckpointAttempted;
            _controller = null;
        }

        private void OnStartClicked() => _controller?.StartRace();

        private void OnResetClicked() => _controller?.Reset();

        private void HandleStateChanged(CourseState state) => UpdateStaticDisplay();

        private void HandleCheckpointPassed(int index) =>
            ShowMessage(CourseStatusFormatter.FormatCheckpointProgress(_controller.CurrentCheckpointIndex, _controller.TotalCheckpoints));

        private void HandleWrongCheckpointAttempted(int attemptedIndex, int requiredIndex) =>
            ShowMessage(CourseStatusFormatter.FormatWrongCheckpoint(requiredIndex));

        private void ShowMessage(string text)
        {
            if (_messageText == null) return;
            _messageText.text = text;
            _messageClearAtUnscaledTime = Time.unscaledTime + 2f;
        }

        // Timer/countdown must visibly tick between events, so this reads the controller's
        // current numbers every rendered frame — the same pattern FPVHUD uses for FPS. This is
        // display polling of already-computed values, not gameplay decision-making: no
        // business logic (checkpoint/timer/state math) lives here, all of it lives in
        // CourseGameplayController/RaceTimer.
        private void Update()
        {
            if (_controller == null) return;

            if (_timerText != null)
                _timerText.text = CourseStatusFormatter.FormatTimer(_controller.ElapsedSeconds);

            if (_checkpointText != null)
            {
                _checkpointText.text = _controller.State == CourseState.Countdown
                    ? CourseStatusFormatter.FormatCountdown(_controller.CountdownRemainingSeconds)
                    : CourseStatusFormatter.FormatCheckpointProgress(_controller.CurrentCheckpointIndex, _controller.TotalCheckpoints);
            }

            if (_messageText != null && !string.IsNullOrEmpty(_messageText.text) && Time.unscaledTime >= _messageClearAtUnscaledTime)
                _messageText.text = string.Empty;
        }

        private void UpdateStaticDisplay()
        {
            if (_controller == null) return;

            if (_stateText != null)
                _stateText.text = CourseStatusFormatter.FormatState(_controller.State, _controller.LastFailureReason);

            if (_startButton != null) _startButton.interactable = CourseStatusFormatter.IsStartAvailable(_controller.State);
            if (_resetButton != null) _resetButton.interactable = CourseStatusFormatter.IsResetAvailable(_controller.State);
        }
    }
}
