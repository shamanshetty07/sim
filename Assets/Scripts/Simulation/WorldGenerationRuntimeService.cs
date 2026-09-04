using System;
using System.Threading.Tasks;
using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.WorldGeneration;
using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>
    /// The thin bridge between WorldGenerationController (prompt -&gt; design -&gt; validate ->
    /// generate, Sim.Core — no knowledge of the drone) and the drone (via IDroneSpawnTarget).
    /// This is what a UI actually calls, not WorldGenerationController directly — but it does
    /// not duplicate or shadow the controller's state machine: <see cref="Controller"/> exposes
    /// it directly, and this class's own job is exactly one thing beyond forwarding
    /// Generate/Cancel/Clear — placing the drone once the controller reaches
    /// <see cref="WorldGenerationState.Ready"/>. Keeping this as a separate, small class rather
    /// than folding drone-placement into WorldGenerationController itself preserves that
    /// controller's existing "no reference to Sim.Drone" boundary (matching WorldGenerator's
    /// own "world construction and drone control stay cleanly separate" rule from Phase 8) — it
    /// is also the one place in the runtime layer allowed to know about both.
    /// </summary>
    public sealed class WorldGenerationRuntimeService : IDisposable
    {
        private readonly WorldGenerationController _controller;
        private readonly IDroneSpawnTarget _droneSpawnTarget;

        /// <summary>The single source of truth for pipeline state — a UI reads State/StateChanged/LastErrorMessage from here, not from this service.</summary>
        public WorldGenerationController Controller => _controller;

        /// <summary>
        /// <paramref name="droneSpawnTarget"/> may be null (e.g. Editor tooling generating a
        /// world with no drone in the scene) — in that case Ready is reached normally, just
        /// without a drone being placed (logged once as a warning, not a silent no-op).
        /// </summary>
        public WorldGenerationRuntimeService(WorldGenerationController controller, IDroneSpawnTarget droneSpawnTarget)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _droneSpawnTarget = droneSpawnTarget;
            _controller.StateChanged += HandleStateChanged;
        }

        public Task GenerateWorldAsync(string prompt, int? seed = null, WorldDesignConstraints constraints = null) =>
            _controller.GenerateWorldAsync(prompt, seed, constraints);

        public void Cancel() => _controller.Cancel();

        public void ClearWorld() => _controller.ClearGeneratedWorld();

        private void HandleStateChanged(WorldGenerationState state)
        {
            if (state != WorldGenerationState.Ready) return;

            GeneratedWorldResult result = _controller.LastGeneratedWorld;
            if (result == null || !result.Success) return; // defensive — Ready should always carry a successful result

            if (_droneSpawnTarget == null)
            {
                Debug.LogWarning("[WorldGeneration] World is ready but no drone spawn target is configured — nothing was placed.");
                return;
            }

            _droneSpawnTarget.PlaceAt(result.SpawnPosition, result.SpawnRotation);
            Debug.Log($"[WorldGeneration] Drone placed at generated spawn {result.SpawnPosition}.");
        }

        /// <summary>Unsubscribes from the controller. Call when this service is being torn down (e.g. scene unload) so it never reacts to a controller it no longer owns.</summary>
        public void Dispose()
        {
            _controller.StateChanged -= HandleStateChanged;
        }
    }
}
