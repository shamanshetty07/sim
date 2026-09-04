using System;
using Sim.Utilities;
using UnityEngine;

namespace Sim.WorldGeneration.Environment
{
    /// <summary>
    /// Default IWorldPrefabRegistry: no external art assets required. Every category
    /// resolves to a small primitive built from Unity's built-in cube/sphere/cylinder shapes —
    /// the same "no art assets available" fallback approach DroneRigBuilder already uses for
    /// the drone itself (Phase 3).
    ///
    /// Category matching here is plain string lookup/Contains against an already-structured
    /// field the AI World Designer produced (ObjectSpecification.Category /
    /// ObstacleSpecification.Type) — not the prohibited "parse the raw prompt with keyword
    /// matching" pattern. This is ordinary asset resolution: turning a category name into a
    /// rendering representation is unavoidable regardless of how the category was decided.
    ///
    /// Real assets can be layered in later by wrapping this registry: check a real prefab
    /// dictionary first, fall back to CreateInstance here for anything missing. Nothing in
    /// EnvironmentGenerator/ObstacleGenerator would need to change.
    /// </summary>
    public sealed class PrimitiveWorldPrefabRegistry : IWorldPrefabRegistry
    {
        private static readonly (string Match, Func<Transform, GameObject> Build)[] Builders =
        {
            ("tree", BuildTree),
            ("rock", BuildRock),
            ("cliff", BuildRock),
            ("boulder", BuildRock),
            ("cabin", BuildBuilding),
            ("building", BuildBuilding),
            ("house", BuildBuilding),
            ("structure", BuildBuilding),
            ("ruin", BuildBuilding),
            ("tower", BuildTower),
            ("bridge", BuildBridge),
            ("tunnel", BuildTunnelSegment),
            ("waterfall", BuildWaterFeature),
            ("river", BuildWaterFeature),
            ("lake", BuildWaterFeature),
            ("bush", BuildVegetation),
            ("shrub", BuildVegetation),
            ("vegetation", BuildVegetation),
            ("grass", BuildVegetation),

            // Obstacle types (Phase 8) — the same registry handles both environment objects
            // and obstacles, per the project's "one clean registry, not scattered lookups" rule.
            ("gate", BuildGate),
            ("ring", BuildRing),
            ("wall", BuildWall),
            ("pole", BuildPole),
            ("landing_pad", BuildLandingPad),
            ("landing pad", BuildLandingPad),
            ("start_line", BuildLineMarker),
            ("start line", BuildLineMarker),
            ("finish_line", BuildLineMarker),
            ("finish line", BuildLineMarker),
            ("checkpoint", BuildCheckpointMarker),
        };

        public GameObject CreateInstance(string category, Transform parent)
        {
            string normalized = (category ?? string.Empty).ToLowerInvariant();

            foreach ((string match, Func<Transform, GameObject> build) in Builders)
            {
                if (normalized.Contains(match))
                    return Parent(build(parent), parent, category);
            }

            return Parent(BuildGenericMarker(parent, category), parent, category);
        }

        private static GameObject Parent(GameObject instance, Transform parent, string category)
        {
            instance.transform.SetParent(parent, false);
            instance.name = string.IsNullOrEmpty(category) ? instance.name : category;
            return instance;
        }

        /// <summary>Cylinder trunk + cone-approximated foliage (a scaled-down cylinder capped with a sphere) — same technique as the drone's primitive arms/motors.</summary>
        private static GameObject BuildTree(Transform parent)
        {
            var root = new GameObject("Tree");

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 1f, 0f);
            trunk.transform.localScale = new Vector3(0.3f, 1f, 0.3f);

            GameObject foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage.name = "Foliage";
            foliage.transform.SetParent(root.transform, false);
            foliage.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            foliage.transform.localScale = new Vector3(1.8f, 2f, 1.8f);

            return root;
        }

        /// <summary>A single scaled sphere — irregular scale variation is applied by the caller (EnvironmentGenerator), not baked in here, so this stays deterministic-free/simple.</summary>
        private static GameObject BuildRock(Transform parent)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock";
            return rock;
        }

        /// <summary>Cube body with a smaller cube "roof" cap — a house-shaped silhouette without needing a custom mesh.</summary>
        private static GameObject BuildBuilding(Transform parent)
        {
            var root = new GameObject("Building");

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 2f, 0f);
            body.transform.localScale = new Vector3(4f, 4f, 4f);

            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "Roof";
            roof.transform.SetParent(root.transform, false);
            roof.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            roof.transform.localScale = new Vector3(4.4f, 1f, 4.4f);

            return root;
        }

        private static GameObject BuildTower(Transform parent)
        {
            GameObject tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tower.name = "Tower";
            tower.transform.localScale = new Vector3(1.5f, 6f, 1.5f);
            tower.transform.localPosition = new Vector3(0f, 6f, 0f);
            return tower;
        }

        /// <summary>Flat deck + four corner support pillars.</summary>
        private static GameObject BuildBridge(Transform parent)
        {
            var root = new GameObject("Bridge");

            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Deck";
            deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = new Vector3(0f, 3f, 0f);
            deck.transform.localScale = new Vector3(4f, 0.4f, 20f);

            Vector3[] pillarOffsets =
            {
                new Vector3(1.6f, 0f, 8f), new Vector3(-1.6f, 0f, 8f),
                new Vector3(1.6f, 0f, -8f), new Vector3(-1.6f, 0f, -8f)
            };
            foreach (Vector3 offset in pillarOffsets)
            {
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar";
                pillar.transform.SetParent(root.transform, false);
                pillar.transform.localPosition = offset + new Vector3(0f, 1.5f, 0f);
                pillar.transform.localScale = new Vector3(0.4f, 1.5f, 0.4f);
            }

            return root;
        }

        /// <summary>Floor + ceiling + two side walls forming a hollow rectangular passage — real flyable geometry with real collision on the walls, not a solid obstruction.</summary>
        private static GameObject BuildTunnelSegment(Transform parent)
        {
            var root = new GameObject("Tunnel");
            const float width = 6f, height = 5f, wallThickness = 0.5f, length = 10f;

            AddTunnelPanel(root.transform, new Vector3(0f, -height / 2f, 0f), new Vector3(width, wallThickness, length));
            AddTunnelPanel(root.transform, new Vector3(0f, height / 2f, 0f), new Vector3(width, wallThickness, length));
            AddTunnelPanel(root.transform, new Vector3(-width / 2f, 0f, 0f), new Vector3(wallThickness, height, length));
            AddTunnelPanel(root.transform, new Vector3(width / 2f, 0f, 0f), new Vector3(wallThickness, height, length));

            return root;
        }

        private static void AddTunnelPanel(Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localScale = localScale;
        }

        private static GameObject BuildWaterFeature(Transform parent)
        {
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "WaterFeature";
            water.transform.localScale = new Vector3(4f, 0.1f, 4f);
            UnityLifecycleUtility.DestroySafely(water.GetComponent<Collider>()); // decorative only, no need to block flight
            var renderer = water.GetComponent<Renderer>();
            // .material (not .sharedMaterial) — accessing it creates a per-instance copy of the
            // default material automatically; mutating .sharedMaterial.color directly would
            // recolor every other primitive in the scene still using Unity's shared default.
            if (renderer != null) renderer.material.color = new Color(0.2f, 0.5f, 0.9f, 0.8f);
            return water;
        }

        private static GameObject BuildVegetation(Transform parent)
        {
            GameObject bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bush.name = "Vegetation";
            bush.transform.localScale = new Vector3(0.8f, 0.6f, 0.8f);
            return bush;
        }

        /// <summary>A rectangular door-frame — bottom/top braces and two side posts, leaving the middle open. Only the frame pieces carry colliders; missing the opening means colliding with the frame, exactly as a real FPV gate behaves.</summary>
        private static GameObject BuildGate(Transform parent)
        {
            var root = new GameObject("Gate");
            const float openWidth = 3f, openHeight = 3f, braceThickness = 0.3f;

            AddFramePiece(root.transform, new Vector3(0f, -openHeight / 2f, 0f), new Vector3(openWidth + braceThickness * 2f, braceThickness, braceThickness)); // bottom
            AddFramePiece(root.transform, new Vector3(0f, openHeight / 2f, 0f), new Vector3(openWidth + braceThickness * 2f, braceThickness, braceThickness)); // top
            AddFramePiece(root.transform, new Vector3(-openWidth / 2f, 0f, 0f), new Vector3(braceThickness, openHeight, braceThickness)); // left post
            AddFramePiece(root.transform, new Vector3(openWidth / 2f, 0f, 0f), new Vector3(braceThickness, openHeight, braceThickness)); // right post

            return root;
        }

        private static void AddFramePiece(Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "Brace";
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = localScale;
        }

        /// <summary>A ring approximated as a polygon of small box segments — Unity has no built-in torus primitive. Flying through the center still means passing between real, collidable segments.</summary>
        private static GameObject BuildRing(Transform parent)
        {
            var root = new GameObject("Ring");
            const int segments = 12;
            const float radius = 2f;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = "RingSegment";
                segment.transform.SetParent(root.transform, false);
                segment.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                segment.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
                segment.transform.localScale = new Vector3(0.3f, radius * 2f * Mathf.PI / segments * 1.1f, 0.3f);
            }

            return root;
        }

        /// <summary>A single large flat slab — solid, meant to block, not to be flown through.</summary>
        private static GameObject BuildWall(Transform parent)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.localScale = new Vector3(6f, 4f, 0.5f);
            wall.transform.localPosition = new Vector3(0f, 2f, 0f);
            return wall;
        }

        private static GameObject BuildPole(Transform parent)
        {
            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.localScale = new Vector3(0.2f, 3f, 0.2f);
            pole.transform.localPosition = new Vector3(0f, 3f, 0f);
            return pole;
        }

        /// <summary>A short, wide platform — solid on purpose (the drone needs to physically rest on it, not fall through).</summary>
        private static GameObject BuildLandingPad(Transform parent)
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "LandingPad";
            pad.transform.localScale = new Vector3(2.5f, 0.1f, 2.5f);
            return pad;
        }

        private static GameObject BuildLineMarker(Transform parent)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "LineMarker";
            line.transform.localScale = new Vector3(8f, 0.05f, 0.5f);
            return line;
        }

        /// <summary>
        /// A thin ring outline with no collider — a pure "checkpoint" obstacle type (distinct
        /// from a gate that happens to also be a checkpoint) is a sensor, not something meant
        /// to physically block the drone if missed. Its actual pass-through detection is a
        /// separate trigger volume ObstacleGenerator adds on top, not this visual.
        /// </summary>
        private static GameObject BuildCheckpointMarker(Transform parent)
        {
            GameObject marker = BuildRing(parent);
            marker.name = "CheckpointMarker";
            foreach (Collider collider in marker.GetComponentsInChildren<Collider>())
                UnityLifecycleUtility.DestroySafely(collider);
            return marker;
        }

        /// <summary>Guaranteed fallback for any category matching none of the above — still visible, still labeled, never a silent no-op.</summary>
        private static GameObject BuildGenericMarker(Transform parent, string category)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = string.IsNullOrEmpty(category) ? "UnknownObject" : category;
            marker.transform.localScale = Vector3.one;
            return marker;
        }
    }
}
