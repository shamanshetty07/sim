# AI-Generated FPV Drone Simulator

A Unity project where a natural-language prompt is turned into a structured
`WorldSpecification` by an AI world-generation service, then deterministically
built into a playable FPV drone course by Unity.

```
prompt → AI world specification → validated spec → procedural world → flight
```

## Status

The runtime prompt pipeline is wired end to end (prompt UI → world design →
validation → Unity world generation → drone spawn), with a Mock world
designer that works fully offline (no API keys, no network). Selecting
`LLM`/`Anthropic` mode now reaches a real Anthropic (Claude) integration —
set `ANTHROPIC_API_KEY` (see `docs/PHASE_10_REAL_LLM.md`) to use it;
without one, it fails honestly rather than faking a result. OpenAI/local-LLM
modes remain unconfigured stubs. A generated world is now a functional FPV
course: ordered checkpoints, a start countdown, a race timer, finish
detection, and reset/restart, with a HUD panel alongside the existing FPV
telemetry (see `docs/PHASE_11_COURSE_GAMEPLAY.md`). See
`docs/IMPLEMENTATION_PLAN.md` for phase-by-phase progress and
`docs/ARCHITECTURE.md` for the system design.

## Requirements

- Unity **2022.3 LTS** (see `ProjectSettings/ProjectVersion.txt`)
- Input System package (pulled in via `Packages/manifest.json`)

## Opening the project

1. Add this folder as a project in Unity Hub, or open it directly with a
   2022.3 LTS Editor install.
2. Let Unity resolve packages on first open (Input System, TextMeshPro,
   Test Framework).
3. No scene is committed yet — use **FPV Sim > World > Build Runtime Scene
   (Save To Disk)** in the Editor to generate `Assets/Scenes/MainScene.unity`
   (drone, camera, OSD, prompt UI, runtime bootstrap), then press Play. See
   `docs/PHASE_9_RUNTIME_PIPELINE.md` for the full setup and test walkthrough.

## Docs

- `docs/ARCHITECTURE.md` — system architecture, data flow, folder layout, error handling
- `docs/IMPLEMENTATION_PLAN.md` — phase tracker
- `docs/PHASE_9_RUNTIME_PIPELINE.md` — the runtime prompt-to-playable-world pipeline, mock vs. LLM mode, how to test without API keys
- `docs/PHASE_10_REAL_LLM.md` — the real Anthropic LLM integration: structured output, configuration, security, testing
- `docs/PHASE_11_COURSE_GAMEPLAY.md` — course gameplay: checkpoints/ordering, race timer, start countdown, finish, reset/restart, HUD
- `docs/WORLD_GENERATION.md` — Phase 8 Unity-side world construction (terrain, environment, obstacles, checkpoints)
- `docs/AI_WORLD_DESIGNER.md` — Phase 7 AI world-design pipeline (current, authoritative)
- `docs/WORLD_SPECIFICATION.md` — prompt -> OpenWorld Reactor -> adapter -> WorldSpecification pipeline (Phase 5, historical framing)
- `docs/OPENWORLD_REACTOR_INTEGRATION.md` / `docs/REACTOR_TO_UNITY_ARCHITECTURE.md` — why Reactor is isolated and non-authoritative
- `docs/DRONE_PHYSICS.md` — flight model + credit to the reference project (added in Phase 3)

## Reference

Flight-mechanics concepts (Rigidbody physics, Angle/Acro/Horizon modes,
expo/deadzone handling, yaw damping) are studied from
[Venkatesan-M/UnityFPVDroneSimulator](https://github.com/Venkatesan-M/UnityFPVDroneSimulator)
for inspiration — not copied. See `docs/DRONE_PHYSICS.md` for specifics.
