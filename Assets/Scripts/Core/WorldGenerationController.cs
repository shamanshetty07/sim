using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Sim.AI.WorldDesign;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Validation;
using Debug = UnityEngine.Debug;

namespace Sim.Core
{
    /// <summary>
    /// The single entry point a future UI needs: GenerateWorldAsync(prompt) / Cancel(). Owns
    /// the Idle -&gt; Requesting -&gt; Validating -&gt; Completed/Failed/Cancelled state machine
    /// and drives IWorldDesigner -&gt; IWorldSpecificationValidator in order. The UI never talks
    /// to either directly, and never needs to know which IWorldDesigner (Mock or a real LLM
    /// provider) is active — it only observes <see cref="State"/> (via
    /// <see cref="StateChanged"/>) and reads <see cref="LastValidSpecification"/> /
    /// <see cref="LastErrorMessage"/> once a terminal state is reached.
    ///
    /// Migrated Phase 8 from the Phase 6 version, which drove the Reactor-shaped
    /// IWorldGenerationService/IReactorWorldAdapter pipeline. Per the Phase 7 architecture
    /// pivot (OpenWorld Reactor is no longer authoritative — see docs/AI_WORLD_DESIGNER.md)
    /// and this phase's explicit "reuse WorldGenerationController and its state machine, don't
    /// create a competing one" instruction, this class was repurposed rather than duplicated:
    /// the WorldGenerationState enum, the overall Idle/Requesting/Validating/terminal-state
    /// shape, the stale-call guard, cancellation handling, and the [WorldGeneration] logging
    /// convention are all unchanged — only the "how do we get a WorldSpecification" internals
    /// changed, since IWorldDesigner returns one directly (no separate adapter stage the way
    /// the Reactor pipeline needed). Nothing in production code depended on the old
    /// constructor shape (verified before migrating); only its own test needed updating.
    ///
    /// Plain C# class, not a MonoBehaviour — Unity lifecycle (a Canvas/HUD script) owns an
    /// instance of this and forwards Generate/Cancel button clicks into it.
    /// </summary>
    public sealed class WorldGenerationController
    {
        private readonly IWorldDesigner _designer;
        private readonly IWorldSpecificationValidator _validator;

        private CancellationTokenSource _cts;

        public WorldGenerationState State { get; private set; } = WorldGenerationState.Idle;
        public WorldSpecification LastValidSpecification { get; private set; }
        public string LastErrorMessage { get; private set; }
        public WorldDesignFailureReason LastFailureReason { get; private set; } = WorldDesignFailureReason.None;

        public event Action<WorldGenerationState> StateChanged;

        public WorldGenerationController(IWorldDesigner designer, IWorldSpecificationValidator validator)
        {
            _designer = designer ?? throw new ArgumentNullException(nameof(designer));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <summary>
        /// Runs one full design+validation attempt. Cancels any attempt already in flight
        /// first, so calling this again is exactly "Retry"/"Generate a new one" — the caller
        /// never needs to call Cancel() first itself.
        /// </summary>
        public async Task GenerateWorldAsync(string prompt, int? seed = null, WorldDesignConstraints constraints = null)
        {
            CancelInternal();
            var cts = new CancellationTokenSource();
            _cts = cts;
            CancellationToken token = cts.Token;

            LastErrorMessage = null;
            LastFailureReason = WorldDesignFailureReason.None;

            WorldDesignRequest request;
            try
            {
                request = new WorldDesignRequest(prompt, seed, constraints);
            }
            catch (ArgumentException ex)
            {
                LastErrorMessage = "World generation failed.";
                LastFailureReason = WorldDesignFailureReason.InvalidResponse;
                Debug.LogWarning($"[WorldGeneration] Invalid request: {ex.Message}");
                SetState(WorldGenerationState.Failed);
                return;
            }

            Debug.Log("[WorldGeneration] Prompt received.");
            SetState(WorldGenerationState.Requesting);

            var stopwatch = Stopwatch.StartNew();
            WorldDesignOutcome outcome;
            try
            {
                outcome = await _designer.DesignWorldAsync(request, token);
            }
            catch (OperationCanceledException)
            {
                HandleCancelled(token);
                return;
            }
            catch (Exception ex)
            {
                // Anything the designer throws that isn't OperationCanceledException is a
                // programmer-error-shaped failure (the contract is supposed to report expected
                // failures via WorldDesignOutcome, not throw) — still must not crash the caller.
                Debug.LogError($"[WorldGeneration] Unexpected exception from IWorldDesigner: {ex}");
                if (!IsCurrent(token)) return;
                LastErrorMessage = "World generation failed.";
                LastFailureReason = WorldDesignFailureReason.Unknown;
                SetState(WorldGenerationState.Failed);
                return;
            }

            if (token.IsCancellationRequested)
            {
                HandleCancelled(token);
                return;
            }

            // From here on, every branch mutates shared state — guard each one against a
            // stale, already-superseded call rather than trusting that a concrete
            // IWorldDesigner always honors the cancellation token promptly.
            if (!outcome.Success)
            {
                Debug.LogWarning($"[WorldGeneration] Design failed: {outcome.FailureReason} — {outcome.ErrorMessage}");
                if (!IsCurrent(token)) return;
                LastErrorMessage = "World generation failed.";
                LastFailureReason = outcome.FailureReason;
                SetState(WorldGenerationState.Failed);
                return;
            }

            Debug.Log($"[WorldGeneration] Design completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            if (!IsCurrent(token)) return;
            SetState(WorldGenerationState.Validating);

            ValidationResult validation = _validator.Validate(outcome.Specification);

            if (!validation.IsValid)
            {
                foreach (ValidationError error in validation.Errors)
                    Debug.LogWarning($"[WorldGeneration] Validation {error.Severity}: {error.Field} — {error.Message}");

                if (!IsCurrent(token)) return;
                LastErrorMessage = "World specification failed validation.";
                LastFailureReason = WorldDesignFailureReason.ValidationFailed;
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
            if (!IsCurrent(token)) return;

            Debug.Log("[WorldGeneration] Generation cancelled.");
            LastErrorMessage = "World generation was cancelled.";
            LastFailureReason = WorldDesignFailureReason.Cancelled;
            SetState(WorldGenerationState.Cancelled);
        }

        private void SetState(WorldGenerationState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
