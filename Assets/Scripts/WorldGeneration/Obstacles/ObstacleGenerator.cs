using System;
using System.Collections.Generic;
using Sim.Gameplay;
using Sim.WorldGeneration.Environment;
using Sim.WorldGeneration.Models;
using Sim.WorldGeneration.Terrain;
using Sim.WorldGeneration.Validation;
using UnityEngine;

namespace Sim.WorldGeneration.Obstacles
{
    /// <summary>
    /// Places every ObstacleSpecification entry under Obstacles/{Gates,Rings,Tunnels,
    /// Checkpoints,Other}, respecting explicit positions exactly, then — if
    /// CourseSpecification.GateCount calls for more gates than were explicitly specified —
    /// auto-generates the remainder along a deterministic path shaped by Course.Style. This is
    /// what makes course *intent* (not just a flat "create N gates") influence the result: a
    /// "technical_then_high_speed" style produces tight, sharply-turning, low-clearance gates
    /// for the portion of the auto-generated sequence identified as technical, and long,
    /// straight, higher-clearance gates for the high-speed portion.
    ///
    /// Only ADDS to what's explicit — never moves, removes, or reinterprets an
    /// already-positioned obstacle from the specification.
    ///
    /// Collision: gates/rings/walls/poles get real (non-trigger) colliders on their visual
    /// geometry from IWorldPrefabRegistry, so missing one means colliding with it — never
    /// trigger-only geometry standing in for something that should physically block. Every
    /// obstacle with a CheckpointIndex additionally gets a small trigger volume placed in its
    /// opening for checkpoint *sensing* (Sim.Gameplay.CheckpointManager reads this) —
    /// deliberately a separate, smaller collider from the obstacle's own blocking geometry.
    /// </summary>
    public sealed class ObstacleGenerator
    {
        private const float CheckpointTriggerHalfExtentXY = 1.25f;
        private const float CheckpointTriggerDepth = 1f;

        private readonly IWorldPrefabRegistry _registry;

        public ObstacleGenerator(IWorldPrefabRegistry registry = null)
        {
            _registry = registry ?? new PrimitiveWorldPrefabRegistry();
        }

        public ObstacleGenerationResult Generate(WorldSpecification specification, Transform obstacleRoot, TerrainGenerationResult terrain, WorldSeedManager seedManager)
        {
            Transform gates = CreateGroup(obstacleRoot, "Gates");
            Transform rings = CreateGroup(obstacleRoot, "Rings");
            Transform tunnels = CreateGroup(obstacleRoot, "Tunnels");
            Transform checkpointsGroup = CreateGroup(obstacleRoot, "Checkpoints");
            Transform other = CreateGroup(obstacleRoot, "Other");

            var placedSpecs = new List<ObstacleSpecification>();
            var placedInstances = new List<GameObject>();

            List<ObstacleSpecification> explicitObstacles = specification.Obstacles ?? new List<ObstacleSpecification>();
            foreach (ObstacleSpecification spec in explicitObstacles)
            {
                if (spec == null) continue;

                Transform group = ResolveGroup(spec.Type, gates, rings, tunnels, checkpointsGroup, other);
                GameObject instance = _registry.CreateInstance(spec.Type ?? "gate", group);
                instance.transform.position = spec.Position;
                instance.transform.rotation = Quaternion.Euler(spec.RotationEuler);
                instance.transform.localScale = spec.Scale == Vector3.zero ? Vector3.one : spec.Scale;

                placedSpecs.Add(spec);
                placedInstances.Add(instance);
            }

            int explicitGateCount = CountGates(explicitObstacles);
            int intendedGateCount = specification.Course?.GateCount ?? 0;
            int additionalGatesNeeded = Mathf.Clamp(intendedGateCount - explicitGateCount, 0, WorldGenerationLimits.MaxObstacleCount);

            if (additionalGatesNeeded > 0)
            {
                GenerateAutoLayoutGates(specification.Course, additionalGatesNeeded, explicitGateCount, gates, terrain, seedManager, placedSpecs, placedInstances);
            }

            var checkpoints = new List<CheckpointDefinition>();
            for (int i = 0; i < placedSpecs.Count; i++)
            {
                ObstacleSpecification spec = placedSpecs[i];
                if (!spec.CheckpointIndex.HasValue) continue;

                AttachCheckpointTrigger(placedInstances[i], spec.CheckpointIndex.Value);
                checkpoints.Add(new CheckpointDefinition(spec.CheckpointIndex.Value, spec.Id, placedInstances[i].transform.position));
            }

            checkpoints.Sort((a, b) => a.Index.CompareTo(b.Index));

            return new ObstacleGenerationResult(obstacleRoot.gameObject, checkpoints);
        }

        private static Transform CreateGroup(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static Transform ResolveGroup(string type, Transform gates, Transform rings, Transform tunnels, Transform checkpoints, Transform other)
        {
            string normalized = (type ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("gate")) return gates;
            if (normalized.Contains("ring")) return rings;
            if (normalized.Contains("tunnel")) return tunnels;
            if (normalized.Contains("checkpoint")) return checkpoints;
            return other; // walls, poles, landing pads, start/finish lines, anything unrecognized
        }

        private static int CountGates(List<ObstacleSpecification> obstacles)
        {
            int count = 0;
            foreach (ObstacleSpecification o in obstacles)
                if (o != null && !string.IsNullOrEmpty(o.Type) && o.Type.ToLowerInvariant().Contains("gate"))
                    count++;
            return count;
        }

        private void AttachCheckpointTrigger(GameObject instance, int checkpointIndex)
        {
            var triggerObject = new GameObject("CheckpointTriggerVolume");
            triggerObject.transform.SetParent(instance.transform, false);
            triggerObject.transform.localPosition = Vector3.zero;

            var collider = triggerObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(CheckpointTriggerHalfExtentXY * 2f, CheckpointTriggerHalfExtentXY * 2f, CheckpointTriggerDepth);

            CheckpointTrigger trigger = triggerObject.AddComponent<CheckpointTrigger>();
            trigger.Configure(checkpointIndex);
        }

        /// <summary>
        /// Lays out the gates needed beyond what was explicitly specified along a deterministic
        /// procedural path, shaped by Course.Style. A style mentioning both "technical" and
        /// "high_speed"/"high-speed" transitions from tight to open partway through the
        /// sequence (matching a "technical then high speed" narrative); mentioning only one
        /// applies it throughout; mentioning neither is a moderate freestyle default.
        /// </summary>
        private void GenerateAutoLayoutGates(
            CourseSpecification course,
            int count,
            int startIndex,
            Transform gatesGroup,
            TerrainGenerationResult terrain,
            WorldSeedManager seedManager,
            List<ObstacleSpecification> placedSpecs,
            List<GameObject> placedInstances)
        {
            Random rng = seedManager.GetRandomForStage("course_gates");
            string style = (course?.Style ?? "freestyle").ToLowerInvariant();
            bool mentionsTechnical = style.Contains("technical");
            bool mentionsHighSpeed = style.Contains("high_speed") || style.Contains("high-speed") || style.Contains("speed");

            Vector3 position = new Vector3(
                terrain.Origin.x + terrain.Width * 0.5f + (float)(rng.NextDouble() - 0.5) * terrain.Width * 0.2f,
                0f,
                terrain.Origin.z + terrain.Depth * 0.15f);
            float headingDeg = 0f; // starts heading toward +Z

            for (int i = 0; i < count; i++)
            {
                float progress01 = count <= 1 ? 0f : (float)i / (count - 1);
                bool technicalPhase = mentionsTechnical && (!mentionsHighSpeed || progress01 < 0.5f);

                float spacing = technicalPhase
                    ? Mathf.Lerp(20f, 35f, (float)rng.NextDouble())
                    : Mathf.Lerp(55f, 90f, (float)rng.NextDouble());
                float turnDeg = technicalPhase
                    ? Mathf.Lerp(-50f, 50f, (float)rng.NextDouble())
                    : Mathf.Lerp(-15f, 15f, (float)rng.NextDouble());
                float clearance = technicalPhase
                    ? Mathf.Lerp(3f, 7f, (float)rng.NextDouble())
                    : Mathf.Lerp(8f, 18f, (float)rng.NextDouble());

                headingDeg += turnDeg;
                Vector3 direction = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;
                position += direction * spacing;
                position.x = Mathf.Clamp(position.x, terrain.Origin.x + 10f, terrain.Origin.x + terrain.Width - 10f);
                position.z = Mathf.Clamp(position.z, terrain.Origin.z + 10f, terrain.Origin.z + terrain.Depth - 10f);
                position.y = terrain.SampleHeight(position.x, position.z) + clearance;

                int checkpointIndex = startIndex + i;
                var spec = new ObstacleSpecification
                {
                    Id = $"gate_auto_{checkpointIndex:D2}",
                    Type = "gate",
                    Position = position,
                    RotationEuler = new Vector3(0f, headingDeg, 0f),
                    Scale = Vector3.one,
                    CheckpointIndex = checkpointIndex
                };

                GameObject instance = _registry.CreateInstance(spec.Type, gatesGroup);
                instance.transform.position = spec.Position;
                instance.transform.rotation = Quaternion.Euler(spec.RotationEuler);

                placedSpecs.Add(spec);
                placedInstances.Add(instance);
            }
        }
    }
}
