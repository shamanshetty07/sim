# Implementation Plan & Status

Tracks the phase order from the project brief. Update the table after each
phase. This is the source of truth for "what's done" across sessions —
check it before assuming a phase needs to start from scratch.

| Phase | Description | Status |
|---|---|---|
| 1 | Inspect repository | ✅ Done — empty dir, no existing project. Greenfield build, Unity 2022.3 LTS. |
| 2 | Architecture + interfaces | ✅ Done — `docs/ARCHITECTURE.md`, folder structure, git init, Unity project skeleton (`ProjectSettings/`, `Packages/manifest.json`). |
| 3 | Drone flies correctly | ✅ Done — Rigidbody physics, Angle/Acro/Horizon, Input System + keyboard fallback, EditMode tests. Unverified in a live Editor (none available here). |
| 4 | FPV camera + OSD | ✅ Done — camera rig, HUD/telemetry UI, editor tooling extended, EditMode tests. Unverified in a live Editor (none available here). |
| 5 | WorldSpecification models | ⬜ Not started |
| 6 | Mock AI service (hardcoded JSON → world) | ⬜ Not started |
| 7 | Connect real AI service (Reactor/Lingbot) | ⬜ Blocked — awaiting API key/docs from user |
| 8 | Prompt UI | ⬜ Not started |
| 9 | Procedural terrain | ⬜ Not started |
| 10 | Environment objects | ⬜ Not started |
| 11 | Racing obstacles | ⬜ Not started |
| 12 | Save/load | ⬜ Not started |
| 13 | Performance optimization | ⬜ Not started |
| 14 | Testing | ⬜ Ongoing — add tests as each system lands, not deferred to the end |

## Phase 3 detail

Files added under `Assets/Scripts/Drone/`: `DroneConfig.cs`, `FlightMode.cs`,
`FlightTelemetry.cs`, `DroneInput.cs`, `FlightModeController.cs`,
`DroneFlightModel.cs`, `DronePhysics.cs`, `DroneController.cs`. Plus
`Assets/Scripts/Editor/DroneRigBuilder.cs` (menu commands `FPV Sim > Create
Drone Rig` / `Create Minimal Test Scene` — builds a flyable, primitive-visual
drone with no hand-authored scene file), assembly definitions
(`Sim.Runtime`, `Sim.Editor`, `Sim.Tests.EditMode`), and
`Assets/Tests/EditMode/{DroneFlightModelTests,FlightModeControllerTests}.cs`.
Design/control-loop details, axis conventions, and Editor verification steps
are in `docs/DRONE_PHYSICS.md`. Two real bugs were caught and fixed during
review before commit: a W/S pitch-key mapping that contradicted the
documented convention, and a sibling-Awake-ordering issue where Rigidbody
mass could silently never get applied if config was set only on
`DroneController` (fixed by re-applying mass inside `Configure()` itself,
not just in `Awake()`).

## Phase 4 detail

Files added under `Assets/Scripts/Camera/`: `CameraSmoothing.cs` (pure
exponential-decay smoothing math), `CameraMount.cs` (marker component),
`FPVCameraController.cs`. Under `Assets/Scripts/UI/`: `TelemetryFormatter.cs`
(pure string formatting), `TelemetryUI.cs`, `FPVHUD.cs`. Extended
`Assets/Scripts/Editor/DroneRigBuilder.cs` with camera/OSD builders and a
new `Build Drone Test Scene (Save To Disk)` command that saves
`Assets/Scenes/DroneTestScene.unity` via `EditorSceneManager` (not hand-
authored). Extended Phase 3's `FlightTelemetry` with
`LocalAngularVelocityDegPerSec`/`AngularSpeedDegPerSec` (the only Phase 3
change this phase needed — see docs/FPV_CAMERA_AND_OSD.md). Added
`Unity.TextMeshPro`/`UnityEngine.UI` to `Sim.Runtime`/`Sim.Editor` asmdefs.
Tests: `CameraSmoothingTests`, `FPVCameraControllerTests`,
`TelemetryFormatterTests` under `Assets/Tests/EditMode/`. Full design
detail, event-lifecycle reasoning, and the manual verification checklist
are in `docs/FPV_CAMERA_AND_OSD.md`.

One real bug caught and fixed during review before commit:
`FPVCameraController.ApplyLensSettings()` only wrote to a `_camera` field
that was populated in `Awake()`/`OnValidate()` — but `Awake()` never runs
for a component added via script in Edit mode (only in Play mode or with
`[ExecuteInEditMode]`), so calling `ApplyLensSettings()` directly (as a test,
or as Editor tooling might) could silently no-op. Fixed by having the method
lazily resolve `_camera` itself instead of assuming prior initialization.

## Notes / decisions carried forward

- No Unity Editor in this environment — every phase's "compile" step is a
  careful manual review, not an actual Editor compile. Flag this at each
  phase summary; ask the user to open the project in their Editor at
  natural checkpoints (end of Phase 3 is the first one worth doing).
- Reactor Lingbot: user will provide API key later. Phase 7 is scaffolded
  with a `NotConfiguredException` stub until then; Phase 6's Mock service is
  the fully working path in the meantime.
- Target packages pinned in `Packages/manifest.json`: Input System 1.7.0,
  TextMeshPro 3.0.6, Test Framework 1.1.33, UnityWebRequest module (for the
  future Reactor client).
