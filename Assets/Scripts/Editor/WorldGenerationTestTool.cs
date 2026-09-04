using System.IO;
using System.Threading.Tasks;
using Sim.AI.WorldDesign;
using Sim.Camera;
using Sim.Core;
using Sim.Drone;
using Sim.Simulation;
using Sim.UI;
using Sim.WorldGeneration;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Validation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sim.EditorTools
{
    /// <summary>
    /// Editor commands for the world-generation pipeline: a one-click Mock-driven test
    /// (unchanged purpose from Phase 8, refactored Phase 9 to go through the now-complete
    /// WorldGenerationController instead of duplicating its design/validate/generate
    /// sequencing), and building the Phase 9 runtime scene — drone/camera/OSD (all reused from
    /// DroneRigBuilder, not duplicated), the prompt UI, an EventSystem, and
    /// RuntimeSimulationBootstrap — saved to disk exactly like DroneRigBuilder's own
    /// "Build ... Test Scene (Save To Disk)" commands already do.
    /// </summary>
    public static class WorldGenerationTestTool
    {
        private const int TestWorldSeed = 20260904;
        private const string RuntimeScenePath = "Assets/Scenes/MainScene.unity";

        private static WorldGenerationController _testController;

        // ----------------------------------------------------------------------------
        // Quick Mock-driven test (Phase 8, refactored Phase 9)
        // ----------------------------------------------------------------------------

        [MenuItem("FPV Sim/World/Generate Test World (Mock Designer)")]
        public static void GenerateTestWorld()
        {
            WorldGenerationController controller = GetOrCreateTestController();

            // Safe to block synchronously here: MockWorldDesigner completes with no real await
            // suspension (SimulatedDelayMilliseconds is 0 by default), and WorldGenerator.Generate
            // is a plain synchronous call — the whole pipeline runs to completion before this
            // Task is ever returned, so GetAwaiter().GetResult() cannot deadlock. Specific to
            // this Mock-only Editor tool — never do this against a real IWorldDesigner.
            Task task = controller.GenerateWorldAsync(ExamplePrompts.Himalayan, seed: TestWorldSeed);
            task.GetAwaiter().GetResult();

            if (controller.State != WorldGenerationState.Ready)
            {
                Debug.LogError($"[WorldGeneration] Test world did not reach Ready (state={controller.State}): {controller.LastErrorMessage}");
                return;
            }

            GeneratedWorldResult result = controller.LastGeneratedWorld;
            Debug.Log($"[WorldGeneration] Test world '{controller.LastValidSpecification.WorldName}' generated — " +
                      $"{result.CheckpointManager.TotalCheckpoints} checkpoints, spawn at {result.SpawnPosition}.");

            PlaceDroneAtSpawn(result);
        }

        [MenuItem("FPV Sim/World/Clear Generated World")]
        public static void ClearGeneratedWorld() => GetOrCreateTestController().ClearGeneratedWorld();

        private static WorldGenerationController GetOrCreateTestController() =>
            _testController ??= new WorldGenerationController(new MockWorldDesigner(), new WorldSpecificationValidator(), new WorldGenerator());

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

            controller.SetSpawn(result.SpawnPosition, result.SpawnRotation);
            controller.ResetToSpawn();

            Selection.activeGameObject = droneRoot;
        }

        // ----------------------------------------------------------------------------
        // Runtime scene (Phase 9)
        // ----------------------------------------------------------------------------

        [MenuItem("FPV Sim/World/Build Runtime Scene (Save To Disk)")]
        public static void BuildRuntimeSceneToDisk()
        {
            if (File.Exists(RuntimeScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Overwrite Runtime Scene?",
                    $"{RuntimeScenePath} already exists. Rebuilding it will discard any manual edits made directly in that scene file.",
                    "Overwrite",
                    "Cancel");
                if (!overwrite) return;
            }

            // NewScene below discards the currently open scene's unsaved changes with no
            // prompt of its own — ask first, matching DroneRigBuilder's identical safeguard.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            DroneRigBuilder.BuildGroundAndLight();
            GameObject drone = DroneRigBuilder.CreateDroneRig();
            var droneController = drone.GetComponent<DroneController>();
            Transform mount = drone.GetComponentInChildren<CameraMount>()?.transform;
            DroneRigBuilder.BuildFpvCamera(mount);
            DroneRigBuilder.BuildOsdCanvas(droneController);

            WorldGenerationUI ui = BuildWorldGenerationCanvas();
            BuildEventSystem();
            BuildBootstrap(droneController, ui);

            Directory.CreateDirectory(Path.GetDirectoryName(RuntimeScenePath)!);
            bool saved = EditorSceneManager.SaveScene(scene, RuntimeScenePath);

            if (saved)
                Debug.Log($"[WorldGeneration] Saved {RuntimeScenePath}. Open it and press Play — " +
                          "the prompt UI starts in Mock mode (no API keys/network needed).");
            else
                Debug.LogError($"[WorldGeneration] Failed to save {RuntimeScenePath}.");
        }

        private static RuntimeSimulationBootstrap BuildBootstrap(DroneController drone, WorldGenerationUI ui)
        {
            var bootstrapGO = new GameObject("Simulation Bootstrap");
            Undo.RegisterCreatedObjectUndo(bootstrapGO, "Create Simulation Bootstrap");
            var bootstrap = bootstrapGO.AddComponent<RuntimeSimulationBootstrap>();

            DroneRigBuilder.AssignField(bootstrap, "_droneController", drone);
            DroneRigBuilder.AssignField(bootstrap, "_ui", ui);

            return bootstrap;
        }

        /// <summary>Unity's own New-Input-System UI module — this project already requires the Input System package to be active for drone controls (Phase 3), so the EventSystem's input module must match, not the legacy Input-Manager-based StandaloneInputModule.</summary>
        private static void BuildEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;

            var eventSystemGO = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<InputSystemUIInputModule>();
        }

        private static WorldGenerationUI BuildWorldGenerationCanvas()
        {
            var canvasGO = new GameObject("World Generation UI", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create World Generation UI");

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasGO.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGO.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(20f, 20f);
            panelRect.sizeDelta = new Vector2(560f, 0f);

            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateLabel(panel.transform, "Title", "AI FPV World Generator", 22f, 32f);
            TMP_InputField promptField = CreatePromptInputField(panel.transform);
            (Button generateButton, Button cancelButton, Button clearButton) = CreateButtonRow(panel.transform);
            TextMeshProUGUI statusText = CreateLabel(panel.transform, "StatusText", "Enter a world description.", 16f, 60f);

            var ui = canvasGO.AddComponent<WorldGenerationUI>();
            DroneRigBuilder.AssignField(ui, "_promptInput", promptField);
            DroneRigBuilder.AssignField(ui, "_generateButton", generateButton);
            DroneRigBuilder.AssignField(ui, "_cancelButton", cancelButton);
            DroneRigBuilder.AssignField(ui, "_clearButton", clearButton);
            DroneRigBuilder.AssignField(ui, "_statusText", statusText);

            return ui;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float fontSize, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;

            return tmp;
        }

        /// <summary>Builds the standard TMP_InputField sub-hierarchy (background + masked Text Area + Placeholder + Text) by hand — the same structure Unity's own "GameObject > UI > Input Field (TMP)" menu command produces.</summary>
        private static TMP_InputField CreatePromptInputField(Transform parent)
        {
            var fieldGO = new GameObject("PromptInputField", typeof(RectTransform));
            fieldGO.transform.SetParent(parent, false);
            fieldGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 120f);

            var fieldImage = fieldGO.AddComponent<Image>();
            fieldImage.color = new Color(1f, 1f, 1f, 0.9f);

            var inputField = fieldGO.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;

            var textArea = new GameObject("Text Area", typeof(RectTransform));
            textArea.transform.SetParent(fieldGO.transform, false);
            var textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(8f, 8f);
            textAreaRect.offsetMax = new Vector2(-8f, -8f);
            textArea.AddComponent<RectMask2D>();

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(textArea.transform, false);
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholder.text = "Describe the FPV world you want...";
            placeholder.fontSize = 20f;
            placeholder.color = new Color(0f, 0f, 0f, 0.4f);
            placeholder.enableWordWrapping = true;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(textArea.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = 20f;
            text.color = Color.black;
            text.enableWordWrapping = true;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.placeholder = placeholder;

            return inputField;
        }

        private static (Button generate, Button cancel, Button clear) CreateButtonRow(Transform parent)
        {
            var row = new GameObject("ButtonRow", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            Button generate = CreateButton(row.transform, "GenerateButton", "Generate");
            Button cancel = CreateButton(row.transform, "CancelButton", "Cancel");
            Button clear = CreateButton(row.transform, "ClearButton", "Clear World");

            return (generate, cancel, clear);
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            var buttonGO = new GameObject(name, typeof(RectTransform));
            buttonGO.transform.SetParent(parent, false);
            buttonGO.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 40f);

            var image = buttonGO.AddComponent<Image>();
            image.color = new Color(0.2f, 0.5f, 0.9f, 0.9f);

            var button = buttonGO.AddComponent<Button>();

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(buttonGO.transform, false);
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 18f;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Center;

            return button;
        }
    }
}
