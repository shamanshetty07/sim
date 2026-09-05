using Sim.Core;
using Sim.Simulation;
using Sim.WorldGeneration.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI
{
    /// <summary>
    /// The runtime prompt UI. Contains NO world-generation logic of its own — every button
    /// handler only collects input and calls into WorldGenerationRuntimeService; every status
    /// display is a pure function (WorldGenerationStatusFormatter) of the service's own state.
    /// This is deliberate, per this phase's explicit "UI ↓ controller ↓ pipeline, never the
    /// reverse" instruction: nothing here calls IWorldDesigner, the validator, or WorldGenerator
    /// directly, and nothing here parses or transforms the prompt text — it's read from the
    /// input field and handed to the service completely unmodified.
    /// </summary>
    public sealed class WorldGenerationUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _promptInput;
        [SerializeField] private Button _generateButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _clearButton;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Tooltip("Optional — Phase 14 save/load. Leave unassigned and the buttons simply aren't built/wired; world generation itself is completely unaffected either way.")]
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _loadButton;

        private WorldGenerationRuntimeService _service;

        private void Awake()
        {
            if (_generateButton != null) _generateButton.onClick.AddListener(OnGenerateClicked);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
            if (_clearButton != null) _clearButton.onClick.AddListener(OnClearClicked);
            if (_saveButton != null) _saveButton.onClick.AddListener(OnSaveClicked);
            if (_loadButton != null) _loadButton.onClick.AddListener(OnLoadClicked);

            if (_promptInput != null && string.IsNullOrEmpty(_promptInput.text))
                _promptInput.text = ExamplePrompts.Himalayan;

            UpdateDisplay(WorldGenerationState.Idle, null);
        }

        private void OnDestroy()
        {
            if (_service != null)
                _service.Controller.StateChanged -= HandleStateChanged;
        }

        /// <summary>
        /// Wires this UI to a service — called by RuntimeSimulationBootstrap after both are
        /// constructed. Safe to call more than once (e.g. if the bootstrap ever needs to
        /// rebuild the service): always detaches from whatever it was previously listening to
        /// first, so this can never end up with a duplicate subscription or one left dangling
        /// on a service that no longer exists — same pattern FPVHUD uses for DroneController
        /// (Phase 4), applied here to WorldGenerationRuntimeService.
        /// </summary>
        public void Initialize(WorldGenerationRuntimeService service)
        {
            if (_service != null)
                _service.Controller.StateChanged -= HandleStateChanged;

            _service = service;

            if (_service != null)
            {
                _service.Controller.StateChanged += HandleStateChanged;
                UpdateDisplay(_service.Controller.State, _service.Controller.LastErrorMessage);
            }
        }

        private void OnGenerateClicked()
        {
            if (_service == null) return;

            string prompt = _promptInput != null ? _promptInput.text : null;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                if (_statusText != null) _statusText.text = "Enter a world description first.";
                return;
            }

            // Fire-and-forget from a UI event handler is the standard Unity pattern for
            // kicking off an async operation from a synchronous callback — the service surfaces
            // progress/results entirely through StateChanged, not through this call's Task.
            _ = _service.GenerateWorldAsync(prompt);
        }

        private void OnCancelClicked() => _service?.Cancel();

        private void OnClearClicked() => _service?.ClearWorld();

        /// <summary>
        /// Phase 14: forwards to WorldGenerationRuntimeService.SaveWorld() and shows whatever
        /// message it returns — no persistence logic lives here. Saving does not change
        /// WorldGenerationState, so (unlike Load) this message would otherwise never appear on
        /// its own; it's written directly and simply stays until the next state change updates
        /// this same label.
        /// </summary>
        private void OnSaveClicked()
        {
            if (_service == null) return;
            if (_statusText != null) _statusText.text = _service.SaveWorld();
        }

        /// <summary>
        /// Phase 14: forwards to WorldGenerationRuntimeService.LoadWorld(). A null return means
        /// the load was handed off to WorldGenerationController successfully — the existing
        /// StateChanged-driven status text already reports what happens next (Ready or Failed),
        /// exactly as it does for a fresh generation, so nothing more is written here in that
        /// case. A non-null return is a failure that happened before the controller was ever
        /// involved (no save file, corrupted/invalid save data) — shown directly, since no state
        /// change will ever arrive to report it otherwise.
        /// </summary>
        private void OnLoadClicked()
        {
            if (_service == null) return;

            string message = _service.LoadWorld();
            if (message != null && _statusText != null)
                _statusText.text = message;
        }

        private void HandleStateChanged(WorldGenerationState state) => UpdateDisplay(state, _service?.Controller.LastErrorMessage);

        private void UpdateDisplay(WorldGenerationState state, string lastErrorMessage)
        {
            if (_statusText != null)
                _statusText.text = WorldGenerationStatusFormatter.Format(state, lastErrorMessage);

            if (_generateButton != null) _generateButton.interactable = WorldGenerationStatusFormatter.IsGenerateAvailable(state);
            if (_cancelButton != null) _cancelButton.interactable = WorldGenerationStatusFormatter.IsCancelAvailable(state);
            if (_clearButton != null) _clearButton.interactable = WorldGenerationStatusFormatter.IsClearAvailable(state);
            if (_saveButton != null) _saveButton.interactable = WorldGenerationStatusFormatter.IsSaveAvailable(state);
            if (_loadButton != null) _loadButton.interactable = WorldGenerationStatusFormatter.IsLoadAvailable(state);
        }
    }
}
