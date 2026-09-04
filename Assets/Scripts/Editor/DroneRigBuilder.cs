using Sim.Drone;
using UnityEditor;
using UnityEngine;

namespace Sim.EditorTools
{
    /// <summary>
    /// One-click Editor tools to build a flyable drone rig (and a minimal test scene around
    /// it) entirely from code. This exists so Phase 3 can be verified in the Editor without
    /// hand-authoring a Unity .scene/.prefab file outside the Editor — those are fragile to
    /// write by hand (GUID/fileID references) and easy to corrupt; building the hierarchy
    /// with the Editor API guarantees a valid result instead.
    ///
    /// Also doubles as the reference implementation of "primitive fallback when no art
    /// assets exist" (per the project's asset-placement rule) applied to the drone itself:
    /// a small cross of scaled cubes/spheres stands in for a drone model until a real one
    /// is imported.
    /// </summary>
    public static class DroneRigBuilder
    {
        private const string DefaultConfigPath = "Assets/Settings/DefaultDroneConfig.asset";

        [MenuItem("FPV Sim/Create Drone Rig")]
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

            var physics = root.AddComponent<DronePhysics>();
            var input = root.AddComponent<DroneInput>();
            var controller = root.AddComponent<DroneController>();

            AssignConfig(physics, config);
            AssignConfig(input, config);
            AssignConfig(controller, config);

            Selection.activeGameObject = root;
            return root;
        }

        [MenuItem("FPV Sim/Create Minimal Test Scene")]
        public static void CreateMinimalTestScene()
        {
            // Ground plane so throttle/gravity/drag are actually observable instead of an
            // infinite fall, and a directional light so the primitive visual is visible.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            Undo.RegisterCreatedObjectUndo(ground, "Create Minimal Test Scene");

            var lightGO = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(lightGO, "Create Minimal Test Scene");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            CreateDroneRig();

            Debug.Log("Minimal test scene created. Press Play, then Backspace/gamepad Start to arm, " +
                      "Space/right trigger for throttle, WASD/right stick for pitch+roll, Q/E/left stick X for yaw, " +
                      "Tab/gamepad Y to cycle flight mode, R/gamepad Select to reset.");
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

        private static void AssignConfig(Object component, DroneConfig config)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty("_config");
            if (prop == null) return;
            prop.objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
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
    }
}
