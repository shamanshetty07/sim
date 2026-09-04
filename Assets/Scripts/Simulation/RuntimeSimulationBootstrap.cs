using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.Drone;
using Sim.UI;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.Simulation
{
    /// <summary>
    /// The runtime composition root: constructs the IWorldDesigner/validator/WorldGenerator/
    /// WorldGenerationController/WorldGenerationRuntimeService chain, finds (or is given) the
    /// drone, and wires the UI to the resulting service. This is the only place in the runtime
    /// layer that knows how to build all of that from scratch — everything it constructs is an
    /// existing class from an earlier phase; this script only decides how they're wired
    /// together, per this phase's "keep construction/configuration separate from business
    /// logic" instruction.
    ///
    /// Does NOT construct the drone rig, FPV camera, or OSD itself — those require Editor-only
    /// APIs (AssetDatabase/SerializedObject, for the DroneConfig asset and field wiring) that
    /// don't exist in a Player build, so duplicating that construction logic here would either
    /// not compile in a build or silently diverge from DroneRigBuilder's real implementation.
    /// Instead this expects the drone to already exist in the scene (built once via the Editor
    /// tooling — WorldGenerationTestTool's "Build Runtime Scene" command — and saved into the
    /// committed scene file, exactly how Phase 3/4's DroneTestScene already worked) and fails
    /// gracefully with a clear, actionable log message if it doesn't, rather than crashing or
    /// silently doing nothing. World generation itself still works with no drone present.
    /// </summary>
    public sealed class RuntimeSimulationBootstrap : MonoBehaviour
    {
        [Tooltip("Mock requires zero external configuration and always works offline. LLM requires a real provider to actually be configured — none are yet (see docs/AI_WORLD_DESIGNER.md) — selecting it will fail honestly rather than pretend to succeed.")]
        [SerializeField] private WorldDesignerMode _mode = WorldDesignerMode.Mock;

        [SerializeField] private LLMProviderKind _llmProvider = LLMProviderKind.OpenAI;

        [Tooltip("Assign in the Inspector, or leave empty to auto-find one via FindObjectOfType at startup.")]
        [SerializeField] private DroneController _droneController;

        [SerializeField] private WorldGenerationUI _ui;

        /// <summary>Exposed for tests/editor tooling that want to drive the same pipeline this bootstrap wires up, without needing a second copy of this construction logic.</summary>
        public WorldGenerationRuntimeService Service { get; private set; }

        private void Awake()
        {
            IWorldDesigner designer = CreateDesigner();
            var validator = new WorldSpecificationValidator();
            var worldGenerator = new WorldGenerator();
            var controller = new WorldGenerationController(designer, validator, worldGenerator);

            DroneController drone = _droneController != null ? _droneController : FindObjectOfType<DroneController>();
            IDroneSpawnTarget spawnTarget = null;

            if (drone != null)
            {
                spawnTarget = new DroneControllerSpawnTarget(drone);
            }
            else
            {
                Debug.LogWarning(
                    "[Bootstrap] No DroneController found in the scene — world generation will " +
                    "still work, but no drone will be placed. Build the runtime scene via " +
                    "'FPV Sim > World > Build Runtime Scene (Save To Disk)', or assign a drone " +
                    "in the Inspector.");
            }

            Service = new WorldGenerationRuntimeService(controller, spawnTarget);

            if (_ui != null)
                _ui.Initialize(Service);
            else
                Debug.LogWarning("[Bootstrap] No WorldGenerationUI assigned — nothing will drive the pipeline until one is wired.");
        }

        private void OnDestroy() => Service?.Dispose();

        private IWorldDesigner CreateDesigner()
        {
            if (_mode == WorldDesignerMode.Mock)
                return new MockWorldDesigner();

            // LLM mode: every provider is currently an honest, unconfigured stub (Phase 7) — no
            // real API key/endpoint exists in this project. Selecting any of them here does not
            // fake a working AI request; GenerateWorldAsync will reach Failed with a clear
            // "not configured" message, exactly as this phase requires.
            ILLMClient client = _llmProvider switch
            {
                LLMProviderKind.Anthropic => new AnthropicLLMClient(),
                LLMProviderKind.Local => new LocalLLMClient(),
                _ => new OpenAiLLMClient()
            };
            return new LLMWorldDesigner(client);
        }
    }
}
