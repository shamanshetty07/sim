using System.IO;
using Sim.Camera;
using Sim.Drone;
using Sim.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sim.EditorTools
{
    /// <summary>
    /// One-click Editor tools to build a flyable drone rig, FPV camera, and OSD entirely
    /// from code. This exists so each phase can be verified in the Editor without
    /// hand-authoring Unity .scene/.prefab files outside the Editor — those are fragile to
    /// write by hand (GUID/fileID references) and easy to corrupt; building the hierarchy
    /// with the Editor API and letting Unity itself serialize it (via EditorSceneManager)
    /// guarantees a valid result instead.
    ///
    /// Also doubles as the reference implementation of "primitive fallback when no art
    /// assets exist" (per the project's asset-placement rule), applied to both the drone
    /// (cubes/cylinders/spheres) and the OSD (plain UI rectangles for the crosshair/horizon —
    /// no sprite assets required).
    ///
    /// The lookups here (FindObjectOfType, GetComponentInChildren) run once, at Editor-tool-
    /// click time, never per-frame at runtime — they are not the "expensive search in Update"
    /// the project's performance rules warn about.
    /// </summary>
    public static class DroneRigBuilder
    {
        private const string DefaultConfigPath = "Assets/Settings/DefaultDroneConfig.asset";
        private const string TestScenePath = "Assets/Scenes/DroneTestScene.unity";

        private const string ControlsHelpText =
            "Drone test rig ready. Press Play, then Backspace/gamepad Start to arm, " +
            "Space/right trigger for throttle, WASD/right stick for pitch+roll, Q/E/left stick X for yaw, " +
            "Tab/gamepad Y to cycle flight mode, R/gamepad Select to reset.";

        // ----------------------------------------------------------------------------
        // Menu commands
        // ----------------------------------------------------------------------------

        [MenuItem("FPV Sim/Create Drone Rig")]
        public static GameObject CreateDroneRigMenuItem() => CreateDroneRig();

        [MenuItem("FPV Sim/Create FPV Camera")]
        public static GameObject CreateFpvCameraMenuItem() => BuildFpvCamera(FindMountForNewCamera());

        [MenuItem("FPV Sim/Create OSD Canvas")]
        public static GameObject CreateOsdCanvasMenuItem() => BuildOsdCanvas(Object.FindObjectOfType<DroneController>());

        [MenuItem("FPV Sim/Build Test Rig In Current Scene")]
        public static void BuildTestRigInCurrentScene()
        {
            BuildGroundAndLight();
            GameObject drone = CreateDroneRig();
            BuildFpvCamera(GetMount(drone));
            BuildOsdCanvas(drone.GetComponent<DroneController>());

            Debug.Log(ControlsHelpText);
        }

        [MenuItem("FPV Sim/Build Drone Test Scene (Save To Disk)")]
        public static void BuildDroneTestSceneToDisk()
        {
            if (File.Exists(TestScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Overwrite Drone Test Scene?",
                    $"{TestScenePath} already exists. Rebuilding it will discard any manual edits made directly in that scene file.",
                    "Overwrite",
                    "Cancel");
                if (!overwrite) return;
            }

            // NewScene below discards the currently open scene's unsaved changes with no
            // prompt of its own — ask first, and abort entirely if the user cancels, rather
            // than silently losing whatever they had open.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildGroundAndLight();
            GameObject drone = CreateDroneRig();
            BuildFpvCamera(GetMount(drone));
            BuildOsdCanvas(drone.GetComponent<DroneController>());

            Directory.CreateDirectory(Path.GetDirectoryName(TestScenePath)!);
            bool saved = EditorSceneManager.SaveScene(scene, TestScenePath);

            if (saved)
                Debug.Log($"Saved {TestScenePath}. {ControlsHelpText}");
            else
                Debug.LogError($"Failed to save {TestScenePath}.");
        }

        // ----------------------------------------------------------------------------
        // Drone rig (Phase 3, extended in Phase 4 with a CameraMount child)
        // ----------------------------------------------------------------------------

        public static GameObject CreateDroneRig()
        {
            DroneConfig config = LoadOrCreateDefaultConfig();

            var root = new GameObject("Drone");
            Undo.RegisterCreatedObjectUndo(root, "Create Drone Rig");
            root.transform.position = new Vector3(0f, 2f, 0f);

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = config.Mass;

            var collider = root.AddComponent<SphereCollider>();
            collider.radius = 0.18f;

            BuildPrimitiveVisual(root.transform);
            BuildCameraMount(root.transform);

            var physics = root.AddComponent<DronePhysics>();
            var input = root.AddComponent<DroneInput>();
            var controller = root.AddComponent<DroneController>();

            AssignField(physics, "_config", config);
            AssignField(input, "_config", config);
            AssignField(controller, "_config", config);

            Selection.activeGameObject = root;
            return root;
        }

        /// <summary>
        /// Front/top attachment point for the FPV camera, matching where a real FPV drone's
        /// camera sits. Local rotation stays identity — the configurable downward tilt lives
        /// entirely on FPVCameraController so it has one single source of truth.
        /// </summary>
        private static void BuildCameraMount(Transform droneRoot)
        {
            var mountGO = new GameObject("CameraMount");
            Undo.RegisterCreatedObjectUndo(mountGO, "Create Drone Rig");
            mountGO.transform.SetParent(droneRoot, false);
            mountGO.transform.localPosition = new Vector3(0f, 0.02f, 0.09f);
            mountGO.transform.localRotation = Quaternion.identity;
            mountGO.AddComponent<CameraMount>();
        }

        private static Transform GetMount(GameObject drone)
        {
            var mount = drone.GetComponentInChildren<CameraMount>();
            if (mount == null)
                Debug.LogWarning("DroneRigBuilder: drone rig has no CameraMount — was it built by an older version of CreateDroneRig?");
            return mount != null ? mount.transform : null;
        }

        private static Transform FindMountForNewCamera()
        {
            // Prefer a mount under the current selection if one exists, otherwise fall back
            // to the first CameraMount anywhere in the scene. One-shot Editor-time lookup.
            if (Selection.activeGameObject != null)
            {
                var onSelection = Selection.activeGameObject.GetComponentInChildren<CameraMount>();
                if (onSelection != null) return onSelection.transform;
            }

            var anyMount = Object.FindObjectOfType<CameraMount>();
            return anyMount != null ? anyMount.transform : null;
        }

        private static DroneConfig LoadOrCreateDefaultConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<DroneConfig>(DefaultConfigPath);
            if (existing != null) return existing;

            var config = ScriptableObject.CreateInstance<DroneConfig>();
            AssetDatabase.CreateAsset(config, DefaultConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created default drone config at {DefaultConfigPath}");
            return config;
        }

        /// <summary>Cube body + four cylinder arms + four small sphere "motors" — no external assets required.</summary>
        private static void BuildPrimitiveVisual(Transform parent)
        {
            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(parent, false);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(visualRoot.transform, false);
            body.transform.localScale = new Vector3(0.16f, 0.05f, 0.16f);

            Vector3[] armDirections =
            {
                new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
                new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f)
            };

            foreach (Vector3 dir in armDirections)
            {
                Vector3 normalized = dir.normalized;

                GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                arm.name = "Arm";
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.transform.SetParent(visualRoot.transform, false);
                arm.transform.localPosition = normalized * 0.12f;
                arm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normalized);
                arm.transform.localScale = new Vector3(0.02f, 0.12f, 0.02f);

                GameObject motor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                motor.name = "Motor";
                Object.DestroyImmediate(motor.GetComponent<Collider>());
                motor.transform.SetParent(visualRoot.transform, false);
                motor.transform.localPosition = normalized * 0.22f;
                motor.transform.localScale = new Vector3(0.04f, 0.04f, 0.04f);
            }
        }

        // ----------------------------------------------------------------------------
        // Ground + lighting
        // ----------------------------------------------------------------------------

        private static void BuildGroundAndLight()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            Undo.RegisterCreatedObjectUndo(ground, "Build Test Rig");

            var lightGO = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(lightGO, "Build Test Rig");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // ----------------------------------------------------------------------------
        // FPV camera (Phase 4)
        // ----------------------------------------------------------------------------

        private static GameObject BuildFpvCamera(Transform mount)
        {
            var cameraGO = new GameObject("FPV Camera");
            Undo.RegisterCreatedObjectUndo(cameraGO, "Create FPV Camera");
            cameraGO.tag = "MainCamera";

            var camera = cameraGO.AddComponent<UnityEngine.Camera>();
            // Small near-clip: the camera mount sits close to the drone's own primitive
            // visual, and the default 0.3 near plane can clip into it.
            camera.nearClipPlane = 0.05f;
            cameraGO.AddComponent<AudioListener>();

            var controller = cameraGO.AddComponent<FPVCameraController>();
            if (mount != null)
                AssignField(controller, "_mount", mount);
            else
                Debug.LogWarning("DroneRigBuilder: created an FPV Camera with no CameraMount to follow — assign one manually.");

            Selection.activeGameObject = cameraGO;
            return cameraGO;
        }

        // ----------------------------------------------------------------------------
        // OSD canvas (Phase 4)
        // ----------------------------------------------------------------------------

        private static GameObject BuildOsdCanvas(DroneController controller)
        {
            if (controller == null)
                Debug.LogWarning("DroneRigBuilder: created an OSD Canvas with no DroneController to read telemetry from — assign one manually.");

            var canvasGO = new GameObject("FPV HUD", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create OSD Canvas");

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            RectTransform horizonBar = CreateHorizonBar(canvasGO.transform);
            CreateCrosshair(canvasGO.transform);

            TextMeshProUGUI[] left = CreateTextStack(canvasGO.transform, "TelemetryLeft", new Vector2(0f, 1f), TextAlignmentOptions.TopLeft,
                new[]
                {
                    "MODE: ANGLE", "DISARMED", "Altitude: 0.0 m", "Speed: 0.0 m/s",
                    "Vertical: +0.0 m/s", "Throttle: 0%", "Pitch: 0°", "Roll: 0°", "Yaw: 0°", "Angular: 0 °/s"
                });
            TextMeshProUGUI[] right = CreateTextStack(canvasGO.transform, "TelemetryRight", new Vector2(1f, 1f), TextAlignmentOptions.TopRight,
                new[] { "0 FPS" });

            var telemetryUI = canvasGO.AddComponent<TelemetryUI>();
            AssignField(telemetryUI, "_modeText", left[0]);
            AssignField(telemetryUI, "_armedText", left[1]);
            AssignField(telemetryUI, "_altitudeText", left[2]);
            AssignField(telemetryUI, "_speedText", left[3]);
            AssignField(telemetryUI, "_verticalSpeedText", left[4]);
            AssignField(telemetryUI, "_throttleText", left[5]);
            AssignField(telemetryUI, "_pitchText", left[6]);
            AssignField(telemetryUI, "_rollText", left[7]);
            AssignField(telemetryUI, "_yawText", left[8]);
            AssignField(telemetryUI, "_angularSpeedText", left[9]);
            AssignField(telemetryUI, "_fpsText", right[0]);
            AssignField(telemetryUI, "_horizonBar", horizonBar);

            var hud = canvasGO.AddComponent<FPVHUD>();
            AssignField(hud, "_telemetryUI", telemetryUI);
            AssignField(hud, "_droneController", controller);

            Selection.activeGameObject = canvasGO;
            return canvasGO;
        }

        private static TextMeshProUGUI[] CreateTextStack(
            Transform parent, string groupName, Vector2 anchorCorner, TextAlignmentOptions alignment, string[] initialLines)
        {
            var group = new GameObject(groupName, typeof(RectTransform));
            group.transform.SetParent(parent, false);

            var groupRect = group.GetComponent<RectTransform>();
            groupRect.anchorMin = anchorCorner;
            groupRect.anchorMax = anchorCorner;
            groupRect.pivot = anchorCorner;
            groupRect.anchoredPosition = Vector2.zero;

            var layout = group.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = anchorCorner.x < 0.5f ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            layout.spacing = 4f;
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = group.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var result = new TextMeshProUGUI[initialLines.Length];
            for (int i = 0; i < initialLines.Length; i++)
            {
                var textGO = new GameObject(initialLines[i], typeof(RectTransform));
                textGO.transform.SetParent(group.transform, false);

                var tmp = textGO.AddComponent<TextMeshProUGUI>();
                tmp.text = initialLines[i];
                tmp.fontSize = 28f;
                tmp.color = Color.white;
                tmp.alignment = alignment;
                tmp.raycastTarget = false;

                var rt = textGO.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(340f, 32f);

                result[i] = tmp;
            }

            return result;
        }

        /// <summary>Two thin rectangles forming a "+" — a primitive fallback crosshair, no sprite asset required.</summary>
        private static void CreateCrosshair(Transform parent)
        {
            var crosshair = new GameObject("Crosshair", typeof(RectTransform));
            crosshair.transform.SetParent(parent, false);
            var rt = crosshair.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;

            CreateUiBar(crosshair.transform, new Vector2(14f, 2f), new Color(1f, 1f, 1f, 0.85f));
            CreateUiBar(crosshair.transform, new Vector2(2f, 14f), new Color(1f, 1f, 1f, 0.85f));
        }

        /// <summary>A single thin bar TelemetryUI rotates by roll and offsets vertically by pitch — the minimal artificial horizon.</summary>
        private static RectTransform CreateHorizonBar(Transform parent)
        {
            GameObject bar = CreateUiBar(parent, new Vector2(220f, 3f), new Color(0.15f, 1f, 0.4f, 0.9f));
            bar.name = "HorizonBar";
            return bar.GetComponent<RectTransform>();
        }

        private static GameObject CreateUiBar(Transform parent, Vector2 size, Color color)
        {
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);

            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            var image = bar.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            return bar;
        }

        // ----------------------------------------------------------------------------
        // Shared helper
        // ----------------------------------------------------------------------------

        /// <summary>
        /// Assigns a serialized Object-reference field by name via SerializedObject, so the
        /// assignment goes through Unity's serialization system and survives a scene save —
        /// calling a public runtime method directly from an Editor script would not persist,
        /// since neither a plain (non-[SerializeField]) field nor a C# event subscription is
        /// ever serialized.
        /// </summary>
        private static void AssignField(Object component, string fieldName, Object value)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"DroneRigBuilder: could not find serialized field '{fieldName}' on {component.GetType().Name}.");
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
