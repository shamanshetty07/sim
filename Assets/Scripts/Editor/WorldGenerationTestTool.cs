using System.Threading.Tasks;
using Sim.AI.WorldDesign;
using Sim.Camera;
using Sim.Drone;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Validation;
using UnityEditor;
using UnityEngine;

namespace Sim.EditorTools
{
    /// <summary>
    /// One-click Editor command exercising the complete Phase 5-8 pipeline end to end:
    /// MockWorldDesigner -&gt; WorldSpecificationValidator -&gt; WorldGenerator -&gt; a playable Unity
    /// scene, with the existing drone rig (Phase 3-4, via DroneRigBuilder — not duplicated
    /// here) placed at the resolved spawn. Requires no OpenWorld Reactor, OpenAI, Anthropic,
    /// network access, or API credentials — MockWorldDesigner is entirely local/synchronous.
    ///
    /// A separate file from DroneRigBuilder.cs (per this phase's "extend existing tooling, or
    /// create appropriate world-generation tooling" instruction) — DroneRigBuilder stays
    /// focused on drone/camera/OSD; this stays focused on world generation, reusing
    /// DroneRigBuilder's camera/OSD builders (made <c>internal</c> for this) rather than
    /// duplicating that construction logic.
    /// </summary>
    public static class WorldGenerationTestTool
    {
        // The example prompt from this phase's brief, used verbatim as the actual test input —
        // not paraphrased or reduced. MockWorldDesigner does not parse this text (see its own
        // remarks); it always returns the same rich example. That example's content (mountain
        // terrain, pine forest, waterfalls, abandoned cabins, cliffs, a technical-then-high-
        // speed 15-gate course) was deliberately authored in Phase 7 to match this exact kind
        // of prompt, so this is a genuine, non-duplicated demonstration that the
        // WorldSpecification schema can represent it — not a coincidence.
        private const string HimalayanTestPrompt =
            "Create a cinematic Himalayan FPV racing course with steep mountains, pine forests, " +
            "waterfalls, cliffs, abandoned cabins, narrow tunnels and 15 racing gates. Make the " +
            "first section technical and tight, then transition into a high-speed valley section.";

        private const int TestWorldSeed = 20260904;

        private static readonly WorldGenerator Generator = new WorldGenerator();

        [MenuItem("FPV Sim/World/Generate Test World (Mock Designer)")]
        public static void GenerateTestWorld()
        {
            WorldSpecification specification = BuildValidatedTestSpecification();
            if (specification == null) return; // BuildValidatedTestSpecification already logged why

            GeneratedWorldResult result = Generator.Generate(specification);
            if (!result.Success)
            {
                Debug.LogError($"[WorldGeneration] Test world generation failed: {result.ErrorMessage}");
                return;
            }

            Debug.Log($"[WorldGeneration] Test world '{specification.WorldName}' generated — " +
                      $"{result.CheckpointManager.TotalCheckpoints} checkpoints, spawn at {result.SpawnPosition}.");

            PlaceDroneAtSpawn(result);
        }

        [MenuItem("FPV Sim/World/Clear Generated World")]
        public static void ClearGeneratedWorld() => Generator.Clear();

        private static WorldSpecification BuildValidatedTestSpecification()
        {
            var designer = new MockWorldDesigner();
            var request = new WorldDesignRequest(HimalayanTestPrompt, seed: TestWorldSeed);

            // Safe to block synchronously here: MockWorldDesigner's default
            // SimulatedDelayMilliseconds is 0, meaning DesignWorldAsync never actually awaits
            // anything and returns an already-completed Task — there is no async continuation
            // that could deadlock. This is specific to Mock, not a general "blocking on async
            // is fine" pattern — never do this against a real IWorldDesigner/ILLMClient call.
            Task<WorldDesignOutcome> designTask = designer.DesignWorldAsync(request);
            WorldDesignOutcome outcome = designTask.GetAwaiter().GetResult();

            if (!outcome.Success)
            {
                Debug.LogError($"[WorldGeneration] Mock designer failed unexpectedly: {outcome.ErrorMessage}");
                return null;
            }

            var validator = new WorldSpecificationValidator();
            ValidationResult validation = validator.Validate(outcome.Specification);

            if (!validation.IsValid)
            {
                Debug.LogError("[WorldGeneration] Test world specification failed validation:");
                foreach (ValidationError error in validation.Errors)
                    Debug.LogError($"  {error.Severity}: {error.Field} — {error.Message}");
                return null;
            }

            return validation.RepairedSpecification;
        }

        private static void PlaceDroneAtSpawn(GeneratedWorldResult result)
        {
            DroneController controller = Object.FindObjectOfType<DroneController>();
            GameObject droneRoot;

            if (controller == null)
            {
                droneRoot = DroneRigBuilder.CreateDroneRig();
                controller = droneRoot.GetComponent<DroneController>();

                Transform mount = droneRoot.GetComponentInChildren<CameraMount>()?.transform;
                DroneRigBuilder.BuildFpvCamera(mount);
                DroneRigBuilder.BuildOsdCanvas(controller);

                Debug.Log("[WorldGeneration] No drone found in the scene — created one (with FPV camera + OSD) via DroneRigBuilder.");
            }
            else
            {
                droneRoot = controller.gameObject;
            }

            // Only integrates with the existing drone system (SetSpawn/ResetToSpawn, Phase 3) —
            // no drone physics/camera/OSD logic is touched or duplicated here.
            controller.SetSpawn(result.SpawnPosition, result.SpawnRotation);
            controller.ResetToSpawn();

            Selection.activeGameObject = droneRoot;
        }
    }
}
