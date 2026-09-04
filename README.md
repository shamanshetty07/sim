# AI-Generated FPV Drone Simulator

A Unity project where a natural-language prompt is turned into a structured
`WorldSpecification` by an AI world-generation service, then deterministically
built into a playable FPV drone course by Unity.

```
prompt → AI world specification → validated spec → procedural world → flight
```

## Status

Early scaffolding. See `docs/IMPLEMENTATION_PLAN.md` for phase-by-phase
progress and `docs/ARCHITECTURE.md` for the system design.

## Requirements

- Unity **2022.3 LTS** (see `ProjectSettings/ProjectVersion.txt`)
- Input System package (pulled in via `Packages/manifest.json`)

## Opening the project

1. Add this folder as a project in Unity Hub, or open it directly with a
   2022.3 LTS Editor install.
2. Let Unity resolve packages on first open (Input System, TextMeshPro,
   Test Framework).
3. No scenes exist yet until Phase 3/4 land a playable drone + FPV camera —
   check `docs/IMPLEMENTATION_PLAN.md` before expecting a runnable build.

## Docs

- `docs/ARCHITECTURE.md` — system architecture, data flow, folder layout, error handling
- `docs/IMPLEMENTATION_PLAN.md` — phase tracker
- `docs/WORLD_SPECIFICATION.md` — prompt -> OpenWorld Reactor -> adapter -> WorldSpecification pipeline (Phase 5)
- `docs/WORLD_GENERATION.md` — validation limits (added in Phase 6+)
- `docs/AI_INTEGRATION.md` — AI provider contract, OpenWorld Reactor notes (added in Phase 6/7)
- `docs/DRONE_PHYSICS.md` — flight model + credit to the reference project (added in Phase 3)

## Reference

Flight-mechanics concepts (Rigidbody physics, Angle/Acro/Horizon modes,
expo/deadzone handling, yaw damping) are studied from
[Venkatesan-M/UnityFPVDroneSimulator](https://github.com/Venkatesan-M/UnityFPVDroneSimulator)
for inspiration — not copied. See `docs/DRONE_PHYSICS.md` for specifics.
