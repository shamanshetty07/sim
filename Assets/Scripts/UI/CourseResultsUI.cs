using Sim.Gameplay;
using Sim.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI
{
    /// <summary>
    /// The results/summary panel shown when a course finishes. Contains NO gameplay logic of
    /// its own: visibility is a pure function of CourseGameplayController.State (visible only
    /// while Finished), every displayed value is a pure function (CourseResultFormatter) of the
    /// CourseResult it was handed, and both buttons only call into existing controllers — same
    /// "UI ↓ controller, never the reverse" rule CourseHUD/WorldGenerationUI already follow.
    ///
    /// Coexists with CourseHUD rather than replacing it — CourseHUD (top-right) keeps showing
    /// FINISHED/final gate count/timer exactly as it already did before this phase; this panel
    /// adds a dedicated, more prominent "COURSE COMPLETE" moment plus the Restart/New World
    /// actions, without duplicating any of CourseHUD's timer/progress display logic.
    ///
    /// RESTART calls CourseGameplayController.Reset() directly — the exact same Phase 11 method
    /// CourseHUD's own Reset button already calls; NEW WORLD calls
    /// WorldGenerationRuntimeService.ClearWorld() directly — the exact same method
    /// WorldGenerationUI's own Clear button already calls. Neither button implements a second
    /// reset/generation pipeline; both are thin forwarding calls onto pre-existing, tested paths.
    /// </summary>
    public sealed class CourseResultsUI : MonoBehaviour
    {
        [Tooltip("The panel GameObject to show/hide as a whole. If left unassigned, this component's own GameObject is toggled instead.")]
        [SerializeField] private GameObject _panelRoot;

        [SerializeField] private TextMeshProUGUI _finalTimeText;
        [SerializeField] private TextMeshProUGUI _gatesText;
        [SerializeField] private TextMeshProUGUI _recoveriesText;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _newWorldButton;

        private CourseGameplayController _course;
        private CourseResultsController _results;
        private WorldGenerationRuntimeService _service;

        private void Awake()
        {
            if (_restartButton != null) _restartButton.onClick.AddListener(OnRestartClicked);
            if (_newWorldButton != null) _newWorldButton.onClick.AddListener(OnNewWorldClicked);

            SetPanelVisible(false);
        }

        private void OnDestroy() => Detach();

        /// <summary>
        /// Wires this panel to the course/results/service triple — called once by
        /// RuntimeSimulationBootstrap after all three are constructed. Safe to call more than
        /// once: always detaches from whatever it was previously listening to first, same
        /// re-wiring pattern CourseHUD.Initialize/FPVHUD.SetDroneController already use.
        /// </summary>
        public void Initialize(CourseGameplayController course, CourseResultsController results, WorldGenerationRuntimeService service)
        {
            Detach();

            _course = course;
            _results = results;
            _service = service;

            if (_course != null) _course.StateChanged += HandleStateChanged;
            if (_results != null) _results.ResultsReady += HandleResultsReady;

            UpdateVisibility();
        }

        private void Detach()
        {
            if (_course != null) _course.StateChanged -= HandleStateChanged;
            if (_results != null) _results.ResultsReady -= HandleResultsReady;
            _course = null;
            _results = null;
        }

        private void OnRestartClicked() => _course?.Reset();

        private void OnNewWorldClicked() => _service?.ClearWorld();

        private void HandleStateChanged(CourseState state) => UpdateVisibility();

        private void HandleResultsReady(CourseResult result) => DisplayResult(result);

        private void UpdateVisibility()
        {
            bool visible = _course != null && _course.State == CourseState.Finished;
            SetPanelVisible(visible);

            // Covers the case where this panel is (re)initialized after a result already
            // exists (e.g. re-wiring at runtime) — display whatever the current result is
            // rather than waiting for a ResultsReady event that already fired.
            if (visible && _results?.LastResult != null)
                DisplayResult(_results.LastResult);
        }

        private void DisplayResult(CourseResult result)
        {
            if (_finalTimeText != null) _finalTimeText.text = CourseResultFormatter.FormatFinalTime(result.ElapsedSeconds);
            if (_gatesText != null) _gatesText.text = CourseResultFormatter.FormatCompletionCount(result.CompletedCheckpoints, result.TotalCheckpoints);
            if (_recoveriesText != null) _recoveriesText.text = CourseResultFormatter.FormatRecoveryCount(result.RecoveryCount);
        }

        private void SetPanelVisible(bool visible)
        {
            GameObject target = _panelRoot != null ? _panelRoot : gameObject;
            if (target.activeSelf != visible)
                target.SetActive(visible);
        }
    }
}
