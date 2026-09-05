using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Sim.AI.WorldDesign;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Validation;
using Debug = UnityEngine.Debug;

namespace Sim.Core
{
    /// <summary>
    /// The single authoritative entry point for the whole prompt-to-playable-world pipeline:
    /// GenerateWorldAsync(prompt) / LoadWorld(specification) / Cancel() / ClearGeneratedWorld().
    /// Owns the Idle -&gt; Designing -&gt; Validating -&gt; Generating -&gt; Ready/Failed/Cancelled
    /// state machine and drives IWorldDesigner -&gt; IWorldSpecificationValidator ->
    /// WorldGenerator in order. Callers never talk to any of the three directly, and never need
    /// to know which IWorldDesigner (Mock or a real LLM provider) is active — they only observe
    /// <see cref="State"/> (via <see cref="StateChanged"/>) and read
    /// <see cref="LastGeneratedWorld"/> / <see cref="LastValidSpecification"/> /
    /// <see cref="LastErrorMessage"/> once a terminal state is reached.
    ///
    /// Phase 14 (save/load): <see cref="LoadWorld"/> drives the exact same
    /// Validating -&gt; Generating -&gt; Ready/Failed tail as GenerateWorldAsync (see
    /// <see cref="ValidateAndGenerate"/>), just starting from an already-known specification
    /// instead of asking <see cref="_designer"/> for one — Designing is skipped entirely, and
    /// no code path from LoadWorld ever reaches IWorldDesigner. Deserializing/validating the
    /// save file itself is entirely Sim.WorldGeneration.Persistence's job (see
    /// docs/PHASE_14_SAVE_LOAD.md); this class only ever sees the resulting WorldSpecification,
    /// exactly like every other caller of ValidateAndGenerate.
    ///
    /// Extended Phase 9 from the Phase 8 version, which stopped at validation. Per this
    /// phase's explicit "extend/refactor it, don't create a second competing controller"
    /// instruction, this is the same class, given one more constructor dependency
    /// (WorldGenerator) and one more pipeline stage — not a new orchestrator. This remains the
    /// *only* place that owns pipeline state; a thin runtime layer
    /// (Sim.Simulation.WorldGenerationRuntimeService) composes this with the drone, but does not
    /// duplicate or shadow its state machine — it forwards this controller's own State/
    /// StateChanged/LastErrorMessage directly (see that class's remarks).
    ///
    /// Deliberately still has no reference to Sim.Drone — WorldGenerator doesn't either (see
    /// its own remarks), and keeping that boundary here too is what lets this class be used
    /// standalone (as Editor tooling already does) without needing a drone in the scene at all.
    ///
    /// Threading: GenerateWorldAsync is async because IWorldDesigner may genuinely need to
    /// await network I/O (a real LLM call). WorldGenerator.Generate() itself is synchronous,
    /// main-thread-only Unity object construction — safe to call directly here because Unity's
    /// SynchronizationContext marshals every `await` continuation in this method back onto the
    /// main thread automatically (this code never uses Task.Run or ConfigureAwait(false), which
    /// are the two ways that guarantee would be broken). See docs/PHASE_9_RUNTIME_PIPELINE.md
    /// "Threading" for the full reasoning.
    ///
    /// Plain C# class, not a MonoBehaviour — Unity lifecycle (a bootstrap/UI script) owns an
    /// instance of this and forwards Generate/Cancel/Clear calls into it.
    /// </summary>
    public sealed class WorldGenerationController
    {
        private readonly IWorldDesigner _designer;
        private readonly IWorldSpecificationValidator _validator;
        private readonly WorldGenerator _worldGenerator;

        private CancellationTokenSource _cts;

        public WorldGenerationState State { get; private set; } = WorldGenerationState.Idle;
        public WorldSpecification LastValidSpecification { get; private set; }
        public GeneratedWorldResult LastGeneratedWorld { get; private set; }
        public string LastErrorMessage { get; private set; }
        public WorldDesignFailureReason LastFailureReason { get; private set; } = WorldDesignFailureReason.None;

        public event Action<WorldGenerationState> StateChanged;

        public WorldGenerationController(IWorldDesigner designer, IWorldSpecificationValidator validator, WorldGenerator worldGenerator)
        {
            _designer = designer ?? throw new ArgumentNullException(nameof(designer));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _worldGenerator = worldGenerator ?? throw new ArgumentNullException(nameof(worldGenerator));
        }

        /// <summary>
        /// Runs one full design -&gt; validate -&gt; generate attempt. Cancels any attempt already
        /// in flight first, so calling this again is exactly "Retry"/"Generate a new one" — the
        /// caller never needs to call Cancel() first itself. The prompt is passed to
        /// IWorldDesigner completely unmodified — nothing in this method inspects, parses, or
        /// keyword-matches it.
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
            SetState(WorldGenerationState.Designing);

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
            ValidateAndGenerate(outcome.Specification, token);
        }

        /// <summary>
        /// Phase 14 (save/load): validates and generates from a specification that is already
        /// fully known — a saved WorldSpecification, deserialized and pre-validated by
        /// Sim.WorldGeneration.Persistence — skipping the design/LLM stage entirely. This is the
        /// one thing that structurally guarantees loading a save never calls IWorldDesigner: no
        /// code path from here reaches <see cref="_designer"/> at all. Reuses the exact same
        /// Validating -&gt; Generating -&gt; Ready/Failed tail GenerateWorldAsync uses (via
        /// <see cref="ValidateAndGenerate"/>) — not a second validate/generate implementation —
        /// so a loaded world is held to precisely the same validation and generation guarantees
        /// as a freshly designed one. Cancels any attempt already in flight first, exactly like
        /// GenerateWorldAsync, so this participates in the same single-flight semantics.
        /// </summary>
        public void LoadWorld(WorldSpecification specification)
        {
            CancelInternal();
            var cts = new CancellationTokenSource();
            _cts = cts;
            CancellationToken token = cts.Token;

            LastErrorMessage = null;
            LastFailureReason = WorldDesignFailureReason.None;

            if (specification == null)
            {
                Debug.LogWarning("[WorldGeneration] LoadWorld called with a null specification.");
                LastErrorMessage = "No world specification to load.";
                LastFailureReason = WorldDesignFailureReason.InvalidResponse;
                SetState(WorldGenerationState.Failed);
                return;
            }

            Debug.Log("[WorldGeneration] Loading a saved world specification — no design/LLM step.");
            ValidateAndGenerate(specification, token);
        }

        /// <summary>
        /// The shared Validating -&gt; Generating -&gt; Ready/Failed tail both GenerateWorldAsync
        /// (after a fresh design) and LoadWorld (given an already-known specification) drive —
        /// extracted so loading a save is held to the exact same validation/generation path as a
        /// freshly designed world, never a second, slightly-different implementation. Entirely
        /// synchronous (WorldGenerator.Generate() always was); the CancellationToken checks still
        /// guard against being superseded by a newer GenerateWorldAsync/LoadWorld call in between
        /// the async steps of the *other* method, even though nothing in this method itself awaits.
        /// </summary>
        private void ValidateAndGenerate(WorldSpecification specification, CancellationToken token)
        {
            if (!IsCurrent(token)) return;
            SetState(WorldGenerationState.Validating);

            ValidationResult validation = _validator.Validate(specification);

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

            // Cancellation cannot safely interrupt WorldGenerator.Generate() itself — it runs
            // synchronously on the main thread and is not written to be interruptible partway
            // (Unity object construction generally isn't safe to abandon mid-call). This is the
            // one place a cancellation request that arrived during/just after validation is
            // honored: check once, right before the point of no return, and never start
            // generation at all if so — "cancel the design phase and prevent subsequent
            // generation," exactly as this phase specifies, rather than attempting to tear down
            // partially-constructed Unity objects from an interrupted call.
            if (!IsCurrent(token) || token.IsCancellationRequested)
            {
                HandleCancelled(token);
                return;
            }

            SetState(WorldGenerationState.Generating);

            GeneratedWorldResult generated = _worldGenerator.Generate(validation.RepairedSpecification);

            if (!generated.Success)
            {
                Debug.LogWarning($"[WorldGeneration] World generation failed: {generated.ErrorMessage}");
                if (!IsCurrent(token)) return;
                LastErrorMessage = generated.ErrorMessage;
                LastFailureReason = WorldDesignFailureReason.Unknown;
                SetState(WorldGenerationState.Failed);
                return;
            }

            Debug.Log("[WorldGeneration] World generation completed.");
            if (!IsCurrent(token)) return;
            LastValidSpecification = validation.RepairedSpecification;
            LastGeneratedWorld = generated;
            SetState(WorldGenerationState.Ready);
        }

        /// <summary>
        /// Destroys the currently-generated world (via WorldGenerator.Clear() — the same
        /// single authoritative cleanup path used everywhere else) and returns to Idle.
        /// Cancels any in-flight generation first. Safe to call when nothing has been
        /// generated yet.
        /// </summary>
        public void ClearGeneratedWorld()
        {
            CancelInternal();
            _worldGenerator.Clear();
            LastValidSpecification = null;
            LastGeneratedWorld = null;
            LastErrorMessage = null;
            LastFailureReason = WorldDesignFailureReason.None;
            SetState(WorldGenerationState.Idle);
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
