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
| 5 | World-generation data contracts (prompt-driven, OpenWorld Reactor-shaped) | ✅ Done — `WorldGenerationRequest`, `IWorldGenerationService`, `ReactorWorldResult`, `ReactorWorldAdapter`, re-scoped `WorldSpecification`, EditMode tests. No real Reactor integration (none available to inspect). Unverified in a live Editor. |
| 6 | Real Mock service (multiple examples, prompt-aware for dev/testing) + `WorldSpecificationValidator` logic | ⬜ Not started |
| 7 | Connect real AI service (OpenWorld Reactor) | ⬜ Blocked — no SDK/API/docs found in this environment; awaiting access from user |
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

## Phase 5 detail

Prompted by an explicit architecture clarification: world generation must
be prompt-driven end to end, with OpenWorld Reactor as the intended
backend — not a hardcoded biome parser dressed up as AI. Searched this
environment for any OpenWorld Reactor SDK/API/docs/config (env vars, CLI
tools, npm/pip/gem packages, common config paths) and found **none** — see
`docs/WORLD_SPECIFICATION.md` "Open questions" for exactly what's still
needed.

Files added under `Assets/Scripts/WorldGeneration/Models/`:
`WorldGenerationRequest.cs` (prompt-preserving request — constructor throws
on empty prompt), `WorldGenerationMetadata.cs`, `ReactorWorldResult.cs` +
`ReactorWorldPayloadKind.cs` (backend-native result envelope — not assumed
to already match `WorldSpecification`'s shape), `WorldSpecification.cs`
(re-scoped: Unity's own normalized contract, explicitly documented as not a
replacement for whatever Reactor natively generates) and its sub-models
(`TerrainSpecification`, `ObjectSpecification`, `ObstacleSpecification`,
`WeatherSpecification`, `LightingSpecification`, `SpawnSpecification`,
`FlightCharacteristics`/`FlightStyle`, `WorldScale`). Several fields
(`TerrainType`, `Weather.Type`, `ObjectSpecification.Category`,
`ObstacleSpecification.Type`) are free-form strings, not enums —
deliberate, so the model can't foreclose whatever OpenWorld Reactor/the
prompt wants to express; see `docs/WORLD_SPECIFICATION.md`.
`FlightCharacteristics` is new and directly encodes the FPV-specific
requirement that "a tight technical forest race" and "an open desert
cruise" must not resolve to the same template.

Under `Assets/Scripts/WorldGeneration/Validation/`: `ValidationResult.cs`,
`ValidationError.cs`, `ValidationSeverity.cs` — data contracts only, no
validation logic yet (deferred to Phase 6, per the explicit Phase 5 scope
of "establish the correct data contracts and interfaces").

Under `Assets/Scripts/WorldGeneration/Adapters/`: `IReactorWorldAdapter.cs`,
`ReactorWorldAdapter.cs` — maps only the fields safe to interpret regardless
of backend (name/description/seed/metadata/prompt); does not parse a
structured payload or resolve a native asset reference yet (no real shape
to write that against).

Under `Assets/Scripts/AI/`: `IWorldGenerationService.cs`,
`WorldGenerationOutcome.cs`, `WorldGenerationFailureReason.cs`,
`ReactorNotConfiguredException.cs`, `MockWorldGenerationService.cs`
(intentionally non-interpretive — one static example, does not parse the
prompt, exists to prove the contract compiles and is usable end to end;
Phase 6 builds a mock actually worth developing against),
`OpenWorldReactorWorldGenerationService.cs` (documented stub — throws
`ReactorNotConfiguredException`).

Tests: `WorldGenerationRequestTests`, `ReactorWorldAdapterTests`,
`MockWorldGenerationServiceTests`, `OpenWorldReactorWorldGenerationServiceTests`,
`ValidationResultTests`, `WorldGenerationOutcomeTests` under
`Assets/Tests/EditMode/`.

`docs/ARCHITECTURE.md` and `docs/WORLD_SPECIFICATION.md` (new) document the
full pipeline, the Option-A adapter decision (and why B/C weren't chosen
without real Reactor access to confirm), and what Unity owns vs. what
OpenWorld Reactor owns.

No `WorldGenerator`/terrain/environment/obstacle generation yet (Phase
7+ — unchanged from before). No real OpenWorld Reactor integration
(blocked on access). Not started this phase, per explicit scope.

## Notes / decisions carried forward

- No Unity Editor in this environment — every phase's "compile" step is a
  careful manual review, not an actual Editor compile. Flag this at each
  phase summary; ask the user to open the project in their Editor at
  natural checkpoints (end of Phase 3 is the first one worth doing).
- OpenWorld Reactor (the world-generation backend, formerly referred to as
  "Reactor Lingbot" — renamed/clarified by the user in Phase 5): no
  SDK/API/docs found in this environment as of Phase 5. Access needed to
  complete `OpenWorldReactorWorldGenerationService`, currently a stub that
  throws `ReactorNotConfiguredException`. See `docs/WORLD_SPECIFICATION.md`
  "Open questions" for the exact checklist. Mock service is the working
  path in the meantime — Phase 5's `MockWorldGenerationService` is
  intentionally minimal/non-interpretive; Phase 6 builds a version worth
  developing against.
- Target packages pinned in `Packages/manifest.json`: Input System 1.7.0,
  TextMeshPro 3.0.6, Test Framework 1.1.33, UnityWebRequest module (for the
  future OpenWorld Reactor client, transport mechanism still unknown).
