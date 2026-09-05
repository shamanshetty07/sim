using System;
using System.Threading.Tasks;
using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.Gameplay;
using Sim.WorldGeneration;
using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>
    /// The thin bridge between WorldGenerationController (prompt -&gt; design -&gt; validate ->
    /// generate, Sim.Core — no knowledge of the drone or of course gameplay) and the two things
    /// that need to react once a world is actually ready: the drone (via IDroneSpawnTarget) and
    /// Phase 11's course gameplay (via CourseGameplayController). This is what a UI actually
    /// calls, not WorldGenerationController directly — but it does not duplicate or shadow the
    /// controller's state machine: <see cref="Controller"/> exposes it directly, and this
    /// class's own job stays exactly "react to Ready/not-Ready," never re-implementing design/
    /// validate/generate. Keeping this as a separate, small class rather than folding
    /// drone-placement/course-binding into WorldGenerationController itself preserves that
    /// controller's existing "no reference to Sim.Drone" boundary (matching WorldGenerator's
    /// own "world construction and drone control stay cleanly separate" rule from Phase 8) — it
    /// is also the one place in the runtime layer allowed to know about all three.
    /// </summary>
    public sealed class WorldGenerationRuntimeService : IDisposable
    {
        private readonly WorldGenerationController _controller;
        private readonly IDroneSpawnTarget _droneSpawnTarget;
        private readonly CourseGameplayController _courseGameplayController;

        /// <summary>The single source of truth for pipeline state — a UI reads State/StateChanged/LastErrorMessage from here, not from this service.</summary>
        public WorldGenerationController Controller => _controller;

        /// <summary>
        /// <paramref name="droneSpawnTarget"/> may be null (e.g. Editor tooling generating a
        /// world with no drone in the scene) — in that case Ready is reached normally, just
        /// without a drone being placed (logged once as a warning, not a silent no-op).
        /// <paramref name="courseGameplayController"/> may likewise be null (e.g. Editor tooling
        /// with no course gameplay in play) — Ready still places the drone normally, just
        /// without any course being bound.
        /// </summary>
        public WorldGenerationRuntimeService(
            WorldGenerationController controller,
            IDroneSpawnTarget droneSpawnTarget,
            CourseGameplayController courseGameplayController = null)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _droneSpawnTarget = droneSpawnTarget;
            _courseGameplayController = courseGameplayController;
            _controller.StateChanged += HandleStateChanged;
        }

        public Task GenerateWorldAsync(string prompt, int? seed = null, WorldDesignConstraints constraints = null) =>
            _controller.GenerateWorldAsync(prompt, seed, constraints);

        public void Cancel() => _controller.Cancel();

        public void ClearWorld() => _controller.ClearGeneratedWorld();

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
                return;
            }

            GeneratedWorldResult result = _controller.LastGeneratedWorld;
            if (result == null || !result.Success) return; // defensive — Ready should always carry a successful result

            if (_droneSpawnTarget == null)
                Debug.LogWarning("[WorldGeneration] World is ready but no drone spawn target is configured — nothing was placed.");
            else
                _droneSpawnTarget.PlaceAt(result.SpawnPosition, result.SpawnRotation);

            _courseGameplayController?.BindToCourse(result.CheckpointManager, result.SpawnPosition, result.SpawnRotation);

            Debug.Log($"[WorldGeneration] Drone placed at generated spawn {result.SpawnPosition}.");
        }

        /// <summary>Unsubscribes from the controller. Call when this service is being torn down (e.g. scene unload) so it never reacts to a controller it no longer owns.</summary>
        public void Dispose()
        {
            _controller.StateChanged -= HandleStateChanged;
        }
    }
}
