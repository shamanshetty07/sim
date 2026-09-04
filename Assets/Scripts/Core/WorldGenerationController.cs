using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Sim.AI;
using Sim.WorldGeneration.Adapters;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Validation;
using Debug = UnityEngine.Debug;

namespace Sim.Core
{
    /// <summary>
    /// The single entry point a future UI (Phase 8) needs: GenerateWorldAsync(prompt) /
    /// Cancel(). Owns the Idle -&gt; Requesting -&gt; Validating -&gt; Completed/Failed/Cancelled
    /// state machine (docs/ARCHITECTURE.md §7) and drives IWorldGenerationService ->
    /// IReactorWorldAdapter -> IWorldSpecificationValidator in order. The UI never talks to
    /// any of those three directly, and never needs to know OpenWorld Reactor, Mock, or any
    /// other provider exists — it only observes <see cref="State"/> (via
    /// <see cref="StateChanged"/>) and reads <see cref="LastValidSpecification"/> /
    /// <see cref="LastErrorMessage"/> once a terminal state is reached.
    ///
    /// Plain C# class, not a MonoBehaviour — Unity lifecycle (a Canvas/HUD script) owns an
    /// instance of this and forwards Generate/Cancel button clicks into it, matching the
    /// project's "keep gameplay/orchestration logic independent from rendering" rule.
    /// </summary>
    public sealed class WorldGenerationController
    {
        private readonly IWorldGenerationService _service;
        private readonly IReactorWorldAdapter _adapter;
        private readonly IWorldSpecificationValidator _validator;

        private CancellationTokenSource _cts;

        public WorldGenerationState State { get; private set; } = WorldGenerationState.Idle;
        public WorldSpecification LastValidSpecification { get; private set; }
        public string LastErrorMessage { get; private set; }
        public WorldGenerationFailureReason LastFailureReason { get; private set; } = WorldGenerationFailureReason.None;

        public event Action<WorldGenerationState> StateChanged;

        public WorldGenerationController(
            IWorldGenerationService service,
            IReactorWorldAdapter adapter,
            IWorldSpecificationValidator validator)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <summary>
        /// Runs one full generation attempt. Cancels any generation already in flight first, so
        /// calling this again is exactly "Retry"/"Generate a new one" — the caller never needs
        /// to call Cancel() first itself.
        /// </summary>
        public async Task GenerateWorldAsync(string prompt, int? seed = null, WorldScale? scale = null)
        {
            CancelInternal();
            var cts = new CancellationTokenSource();
            _cts = cts;
            CancellationToken token = cts.Token;

            LastErrorMessage = null;
            LastFailureReason = WorldGenerationFailureReason.None;

            WorldGenerationRequest request;
            try
            {
                request = new WorldGenerationRequest(prompt, seed, scale);
            }
            catch (ArgumentException ex)
            {
                LastErrorMessage = "World generation failed.";
                LastFailureReason = WorldGenerationFailureReason.InvalidResponse;
                Debug.LogWarning($"[WorldGeneration] Invalid request: {ex.Message}");
                SetState(WorldGenerationState.Failed);
                return;
            }

            Debug.Log("[WorldGeneration] Prompt received.");
            SetState(WorldGenerationState.Requesting);

            var stopwatch = Stopwatch.StartNew();
            WorldGenerationOutcome outcome;
            try
            {
                outcome = await _service.GenerateWorldAsync(request, token);
            }
            catch (OperationCanceledException)
            {
                HandleCancelled(token);
                return;
            }
            catch (Exception ex)
            {
                // Anything the service throws that isn't OperationCanceledException is a
                // programmer-error-shaped failure (the service contract is supposed to report
                // expected failures via WorldGenerationOutcome, not throw) — still must not
                // crash the caller. Logged with full detail; the UI-facing message stays generic.
                Debug.LogError($"[WorldGeneration] Unexpected exception from IWorldGenerationService: {ex}");
                if (!IsCurrent(token)) return;
                LastErrorMessage = "World generation failed.";
                LastFailureReason = WorldGenerationFailureReason.Unknown;
                SetState(WorldGenerationState.Failed);
                return;
            }

            if (token.IsCancellationRequested)
            {
                HandleCancelled(token);
                return;
            }

            // From here on, every branch mutates shared state (LastErrorMessage/
            // LastValidSpecification/State) — guard each one against a stale, already-
            // superseded call (see GenerateWorldAsync's remarks) rather than trusting that a
            // concrete IWorldGenerationService always honors the cancellation token promptly.
            if (!outcome.Success)
            {
                Debug.LogWarning($"[WorldGeneration] Generation failed: {outcome.FailureReason} — {outcome.ErrorMessage}");
                if (!IsCurrent(token)) return;
                LastErrorMessage = "World generation failed.";
                LastFailureReason = outcome.FailureReason;
                SetState(WorldGenerationState.Failed);
                return;
            }

            Debug.Log($"[WorldGeneration] Generation completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            if (!IsCurrent(token)) return;
            SetState(WorldGenerationState.Validating);

            WorldSpecification specification = _adapter.Adapt(outcome.Result, request);
            ValidationResult validation = _validator.Validate(specification);

            if (!validation.IsValid)
            {
                foreach (ValidationError error in validation.Errors)
                    Debug.LogWarning($"[WorldGeneration] Validation {error.Severity}: {error.Field} — {error.Message}");

                if (!IsCurrent(token)) return;
                LastErrorMessage = "World specification failed validation.";
                LastFailureReason = WorldGenerationFailureReason.ValidationFailed;
                SetState(WorldGenerationState.Failed);
                return;
            }

            Debug.Log("[WorldGeneration] Validation passed.");
            if (!IsCurrent(token)) return;
            LastValidSpecification = validation.RepairedSpecification;
            SetState(WorldGenerationState.Completed);
        }

        /// <summary>True if `token` belongs to the attempt this controller currently considers "the" in-flight/most-recent one — false for a stale call superseded by a later GenerateWorldAsync.</summary>
        private bool IsCurrent(CancellationToken token) => _cts != null && _cts.Token == token;

        /// <summary>Cancels the in-flight generation, if any. Safe to call when nothing is running.</summary>
        public void Cancel() => CancelInternal();

        private void CancelInternal()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
                _cts.Cancel();
        }

        private void HandleCancelled(CancellationToken token)
        {
            // Only reflect the cancellation if it's still the current attempt's token — an
            // older, already-superseded generation's cancellation must not stomp on a newer
            // one that's already progressed past it.
            if (!IsCurrent(token)) return;

            Debug.Log("[WorldGeneration] Generation cancelled.");
            LastErrorMessage = "World generation was cancelled.";
            LastFailureReason = WorldGenerationFailureReason.Cancelled;
            SetState(WorldGenerationState.Cancelled);
        }

        private void SetState(WorldGenerationState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
