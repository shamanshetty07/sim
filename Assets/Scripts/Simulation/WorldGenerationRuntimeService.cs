using System;
using System.Threading.Tasks;
using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.Gameplay;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Persistence;
using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>
    /// The thin bridge between WorldGenerationController (prompt -&gt; design -&gt; validate ->
    /// generate, Sim.Core — no knowledge of the drone, course gameplay, recovery, or results) and
    /// the things that need to react once a world is actually ready: the drone (via
    /// IDroneSpawnTarget), Phase 11's course gameplay (via CourseGameplayController), Phase 12's
    /// crash/fall recovery (via DroneRecoveryController), and Phase 13's results snapshotting
    /// (via CourseResultsController — only ever given the generated world's seed; it clears its
    /// own stored result reactively off CourseGameplayController's events, needing no explicit
    /// bind/unbind call here). This is what a UI actually calls, not WorldGenerationController
    /// directly — but it does not duplicate or shadow the controller's state machine:
    /// <see cref="Controller"/> exposes it directly, and this class's own job stays exactly
    /// "react to Ready/not-Ready," never re-implementing design/validate/generate. Keeping this
    /// as a separate, small class rather than folding drone-placement/course-binding/recovery-
    /// binding into WorldGenerationController itself preserves that controller's existing "no
    /// reference to Sim.Drone" boundary (matching WorldGenerator's own "world construction and
    /// drone control stay cleanly separate" rule from Phase 8) — it is also the one place in the
    /// runtime layer allowed to know about all of them.
    ///
    /// Phase 14 adds <see cref="SaveWorld"/>/<see cref="LoadWorld"/>, thin forwards onto an
    /// injected IWorldSaveService (Sim.WorldGeneration.Persistence) and
    /// WorldGenerationController.LoadWorld — no second generation pipeline, and a successful
    /// load reaches Ready through the exact same StateChanged handler below that a fresh
    /// generation already does, so drone placement/course binding/recovery binding/result-seed
    /// tracking all just work for a loaded world with no additional code.
    /// </summary>
    public sealed class WorldGenerationRuntimeService : IDisposable
    {
        private readonly WorldGenerationController _controller;
        private readonly IDroneSpawnTarget _droneSpawnTarget;
        private readonly CourseGameplayController _courseGameplayController;
        private readonly DroneRecoveryController _droneRecoveryController;
        private readonly CourseResultsController _courseResultsController;
        private readonly IWorldSaveService _worldSaveService;

        /// <summary>The single source of truth for pipeline state — a UI reads State/StateChanged/LastErrorMessage from here, not from this service.</summary>
        public WorldGenerationController Controller => _controller;

        /// <summary>
        /// <paramref name="droneSpawnTarget"/> may be null (e.g. Editor tooling generating a
        /// world with no drone in the scene) — in that case Ready is reached normally, just
        /// without a drone being placed (logged once as a warning, not a silent no-op).
        /// <paramref name="courseGameplayController"/>, <paramref name="droneRecoveryController"/>,
        /// <paramref name="courseResultsController"/>, and <paramref name="worldSaveService"/>
        /// may likewise be null (e.g. Editor tooling with no course gameplay in play) — Ready
        /// still places the drone normally, just without any course/recovery/results being bound,
        /// and <see cref="SaveWorld"/>/<see cref="LoadWorld"/> report a clear "not configured"
        /// message instead of throwing.
        /// </summary>
        public WorldGenerationRuntimeService(
            WorldGenerationController controller,
            IDroneSpawnTarget droneSpawnTarget,
            CourseGameplayController courseGameplayController = null,
            DroneRecoveryController droneRecoveryController = null,
            CourseResultsController courseResultsController = null,
            IWorldSaveService worldSaveService = null)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _droneSpawnTarget = droneSpawnTarget;
            _courseGameplayController = courseGameplayController;
            _droneRecoveryController = droneRecoveryController;
            _courseResultsController = courseResultsController;
            _worldSaveService = worldSaveService;
            _controller.StateChanged += HandleStateChanged;
        }

        public Task GenerateWorldAsync(string prompt, int? seed = null, WorldDesignConstraints constraints = null) =>
            _controller.GenerateWorldAsync(prompt, seed, constraints);

        public void Cancel() => _controller.Cancel();

        public void ClearWorld() => _controller.ClearGeneratedWorld();

        /// <summary>
        /// Phase 14: persists the currently generated world's specification (the same
        /// Controller.LastValidSpecification every other Ready-consumer already reads) — not a
        /// second generation/course pipeline, just a thin forward into IWorldSaveService.
        /// Returns a short, UI-displayable message; never throws.
        /// </summary>
        public string SaveWorld()
        {
            if (_worldSaveService == null) return "Save is not available.";

            WorldSpecification specification = _controller.LastValidSpecification;
            if (specification == null) return "No generated world to save yet.";

            WorldSaveOperationResult result = _worldSaveService.Save(WorldSaveData.FromSpecification(specification));
            return result.Success ? "World saved." : $"Save failed: {result.ErrorMessage}";
        }

        /// <summary>
        /// Phase 14: loads and validates a saved WorldSpecification (IWorldSaveService.Load
        /// already runs it through the same WorldSpecificationValidator every generated
        /// specification goes through), then hands it to
        /// WorldGenerationController.LoadWorld — the exact same Validating -&gt; Generating -&gt;
        /// Ready/Failed path a fresh generation uses, skipping only the design/LLM step. No
        /// second generation pipeline; no LLM/network call happens on this path at all.
        ///
        /// Returns null when the load was handed off to the controller successfully — from that
        /// point on, the existing StateChanged-driven status text (WorldGenerationStatusFormatter)
        /// already reports the outcome (Ready or Failed), exactly as it does for a fresh
        /// generation, so the UI does not need a second status message for that part. Returns a
        /// non-null message only for a failure that happens *before* the controller is ever
        /// involved (no save file, corrupted/invalid save data) — a case StateChanged can't
        /// report because the controller's state never changes at all.
        /// </summary>
        public string LoadWorld()
        {
            if (_worldSaveService == null) return "Load is not available.";

            WorldLoadResult result = _worldSaveService.Load();
            if (!result.Success) return result.ErrorMessage;

            _controller.LoadWorld(result.Data.Specification);
            return null;
        }

        private void HandleStateChanged(WorldGenerationState state)
        {
            // Anything other than Ready means "no valid generated world right now" — including
            // the Designing/Validating/Generating states a fresh GenerateWorldAsync call passes
            // through on its way to a *new* Ready. Unbinding here (not only on Idle/Failed) is
            // what guarantees the old course's CheckpointManager subscription is dropped before
            // the old GeneratedWorld's GameObjects are destroyed by WorldGenerator.Generate()'s
            // own Clear() — never a stale reference, never a subscription left on a destroyed
            // object. Idempotent and cheap to call on every non-Ready transition.
            if (state != WorldGenerationState.Ready)
            {
                _courseGameplayController?.Unbind();
                _droneRecoveryController?.Unbind();
                return;
            }

            GeneratedWorldResult result = _controller.LastGeneratedWorld;
            if (result == null || !result.Success) return; // defensive — Ready should always carry a successful result

            if (_droneSpawnTarget == null)
                Debug.LogWarning("[WorldGeneration] World is ready but no drone spawn target is configured — nothing was placed.");
            else
                _droneSpawnTarget.PlaceAt(result.SpawnPosition, result.SpawnRotation);

            _courseGameplayController?.BindToCourse(result.CheckpointManager, result.SpawnPosition, result.SpawnRotation);
            _droneRecoveryController?.Bind(result.Bounds, result.SpawnPosition, result.SpawnRotation);

            // CourseResultsController needs no explicit bind/unbind call of its own — it clears
            // its stored result reactively off CourseGameplayController.StateChanged (see its own
            // remarks), which BindToCourse/Unbind above already drive. The seed is the one piece
            // of per-generation data it cannot get from CourseGameplayController itself.
            _courseResultsController?.SetWorldSeed(_controller.LastValidSpecification?.Seed ?? 0);

            Debug.Log($"[WorldGeneration] Drone placed at generated spawn {result.SpawnPosition}.");
        }

        /// <summary>Unsubscribes from the controller. Call when this service is being torn down (e.g. scene unload) so it never reacts to a controller it no longer owns.</summary>
        public void Dispose()
        {
            _controller.StateChanged -= HandleStateChanged;
        }
    }
}
