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
| 6 | Real OpenWorld Reactor auth + validation logic + state model | ✅ Done — see below. Real API key provided; identified OpenWorld Reactor as Reactor (reactor.inc)/LingBot (Ant Group) via public docs; verified real authentication with a live, successful API call. Full generation integration deferred (deliberate, user-confirmed) — see docs/OPENWORLD_REACTOR_INTEGRATION.md. |
| 6.5 | Investigate whether any Reactor model can give Unity a usable 3D world | ✅ Done — checked all 8 hosted models. None export mesh/point-cloud/depth/GLTF/USD/FBX or structured scene state; video-only across the platform. See docs/REACTOR_TO_UNITY_ARCHITECTURE.md. Recommendation given (Option D diagnosis → Option C path); implementation not started, awaiting user direction. |
| 7 | AI World Designer + WorldSpecification generation | ✅ Done — `IWorldDesigner`/`WorldDesignRequest`/`WorldDesignOutcome`, `MockWorldDesigner` (rich, deterministic, non-interpretive), `LLMWorldDesigner` + `ILLMClient` (OpenAI/Anthropic/Local — all honest stubs, none configured), `WorldSpecificationJsonParser` (Newtonsoft.Json, `TypeNameHandling.None`), new `CourseSpecification` model. Answers 6.5's "where does the intelligence come from" question. See docs/AI_WORLD_DESIGNER.md. |
| 8 | Unity-side procedural world construction (`WorldGenerator`) | ⬜ Not started — this is what phase 7 was provisionally labeled before the architecture pivot; renumbered here to make room for the AI World Designer phase the pivot required first |
| 9 | Prompt UI | ⬜ Not started |
| 10 | Procedural terrain | ⬜ Not started |
| 11 | Environment objects | ⬜ Not started |
| 12 | Racing obstacles | ⬜ Not started |
| 13 | Save/load | ⬜ Not started |
| 14 | Performance optimization | ⬜ Not started |
| 15 | Testing | ⬜ Ongoing — add tests as each system lands, not deferred to the end |

Numbering has diverged from the original 14-phase brief (the 6.5
investigation and this phase's architecture pivot both required insertions
the original plan didn't anticipate) — treat the descriptions as
authoritative, not the specific numbers.

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

## Phase 6 detail

User provided a real OpenWorld Reactor API key mid-session. Before touching
it: searched for real public docs (WebSearch/WebFetch, not guessing) and
identified OpenWorld Reactor as **Reactor (reactor.inc)**, hosting
**LingBot**/**LingBot World 2** (Ant Group models) — an exact match for the
project's original "Reactor Lingbot" naming. Fetched real API docs
(docs.reactor.inc), confirmed the real auth schema (`POST
https://api.reactor.inc/tokens`, `Reactor-API-Key` header, documented
request/response JSON), and ran one real, minimal, scoped test call via
curl — **HTTP 200, valid JWT returned** — before writing any Unity code
against it. Discovered the real product is a live steerable video session
(LingBot World 2: upload image, `set_prompt`, `start`, then WASD/camera-
steered real-time video at 48fps) with no Unity/C# SDK — fundamentally
incompatible with the project's one-shot "prompt in, world description
out" interface shape. Presented this to the user and got an explicit
decision: defer the live-session integration, ship everything else. Full
detail: `docs/OPENWORLD_REACTOR_INTEGRATION.md`.

Credential handling: API key stored only in `.env.local` at the repo root
(never under `Assets/`, mode 600, already covered by the Phase 2
`.gitignore` pattern — verified with `git check-ignore` before writing
anything else). `IReactorCredentialsProvider` made injectable specifically
so automated tests never depend on (or accidentally exercise) whatever's
really configured on the machine running them — the one real network call
this phase made was manual (curl), not part of the test suite. `git
status`/`git diff` and a full-history grep for the key were run before
every commit and push this phase.

Files added: `Assets/Scripts/AI/{IReactorCredentialsProvider,
EnvironmentReactorCredentialsProvider,ReactorApiException,ReactorTokenResult}.cs`;
`Assets/Scripts/WorldGeneration/Validation/{IWorldSpecificationValidator,
WorldSpecificationValidator,WorldGenerationLimits}.cs` (first real
validation logic — Phase 5 shipped only data contracts);
`Assets/Scripts/Core/{WorldGenerationState,WorldGenerationController}.cs`
(the `GenerateWorld(prompt)`/`Cancel()` entry point + state machine
documented since Phase 2 but not built until now); 6 new/rewritten
EditMode test files; `docs/OPENWORLD_REACTOR_INTEGRATION.md`.

Files modified: `Assets/Scripts/AI/OpenWorldReactorWorldGenerationService.cs`
(real `MintSessionTokenAsync` via `UnityWebRequest` against the verified
endpoint/schema; `GenerateWorldAsync` now returns structured
`WorldGenerationOutcome`s instead of throwing), `WorldGenerationFailureReason.cs`
(added `Unavailable`/`ValidationFailed`/`NotImplemented`),
`MockWorldGenerationService.cs` (deterministic seed derivation, simulated
delay, cancellation), `docs/ARCHITECTURE.md`, `.env.local` created (not
committed).

Bugs caught and fixed during review before commit: a dead ternary in
`WorldSpecificationValidator.RepairDimension` where both branches evaluated
to the same value (leftover from an edit); `WorldGenerationController`
guarded cancellation against a stale/superseded call overwriting a newer
one's state, but the same protection was missing from the
success/failure/validation branches — fixed by applying one consistent
`IsCurrent(token)` guard everywhere shared state is mutated.

## Phase 7 detail

User accepted Phase 6.5's Option D finding and directed an architecture
change: OpenWorld Reactor/LingBot is not, and will not become, the source
of world geometry. Instead, a general-purpose LLM interprets the prompt
directly into `WorldSpecification`. Explicit instruction: keep the
Reactor code, isolated, not deleted; do not make the Unity generator
depend on it.

Files added under `Assets/Scripts/AI/WorldDesign/` (new folder, deliberately
separate from `Assets/Scripts/AI/`'s existing Reactor-facing types —
neither namespace references the other): `IWorldDesigner.cs`,
`WorldDesignRequest.cs`, `WorldDesignConstraints.cs`, `WorldDesignOutcome.cs`,
`WorldDesignFailureReason.cs`, `MockWorldDesigner.cs` (rich fully-populated
example, deterministic, still honestly non-interpretive — same reasoning
as `MockWorldGenerationService`), `ILLMClient.cs` +
`LLMCompletionRequest/Result.cs` (the actual provider-swap abstraction —
one `LLMWorldDesigner` with all prompt-engineering/JSON-handling logic
written once, rather than duplicating it across parallel
OpenAI/Claude/LocalWorldDesigner classes; see docs/AI_WORLD_DESIGNER.md for
why), `LLMWorldDesigner.cs`, `IWorldSpecificationJsonParser.cs` +
`WorldSpecificationJsonParser.cs` (Newtonsoft.Json with explicit
`TypeNameHandling.None` — the concrete "never execute AI-generated code"
boundary), `LLMNotConfiguredException.cs`, and three `ILLMClient` stubs
(`OpenAiLLMClient`, `AnthropicLLMClient`, `LocalLLMClient`) — none make a
real network call; no credentials for any of the three exist in this
environment, per this phase's explicit "only implement once configured"
instruction.

Added `CourseSpecification.cs` (`WorldSpecification.Course`) — style,
difficulty, gate count, ordered section narrative — directly answering the
brief's own example of intent a flat object-count model can't express.
Extended `WorldSpecificationValidator` with a matching `ValidateCourse`.
Extracted `Sim.Utilities.StableHash` (FNV-1a) from
`MockWorldGenerationService` so `MockWorldDesigner` doesn't duplicate the
same hashing logic. Added `com.unity.nuget.newtonsoft-json` to
`Packages/manifest.json` (justified: `WorldSpecification`'s model classes
use C# auto-properties, which Unity's built-in `JsonUtility` cannot
deserialize into — only public fields).

Tests: `WorldDesignRequestTests`, `MockWorldDesignerTests`,
`WorldSpecificationJsonParserTests` (including a `$type`-injection attempt
and script/SQL-injection-shaped string content, both confirmed to end up
as inert data), `LLMWorldDesignerTests` (fake in-memory `ILLMClient` — no
network — covering success/failure/cancellation/malformed-response, plus
the three provider stubs' not-configured behavior).

Full design reasoning, the request/response flow, the security boundary,
and Reactor's now-optional future role: `docs/AI_WORLD_DESIGNER.md`.

## Phase 6.5 detail

User paused the planned Phase 7 (Unity world generator) to first answer:
"can OpenWorld Reactor give Unity anything better than video to build a
flyable world from?" Checked the platform's full 63-page documentation
site map, not just the one model examined in Phase 6. Fetched and read:
`concepts/tracks.md`, `concepts/frame-metadata.md`, `concepts/recordings.md`,
`models/overview.md` (the shared wire protocol), the full
`lingbot-world-2/schema.md` command/event list, `lingbot/overview.md`,
`happy-oyster/overview.md` + `schema.md` (checked specifically because
"permanent explorable worlds" sounded like it might mean an exportable
world), `resources/faq.md`, and `changelog/overview.md` (checked for a
recently-shipped export feature). No undocumented transport was reverse-
engineered; no API was invented.

**Finding:** every model is video-only. No mesh/point-cloud/depth (in
practice, despite the generic docs mentioning depth as a *possible* track
kind for *some* model)/GLTF/USD/FBX export exists anywhere on the
platform, and no model returns structured scene/object-state JSON — only
generation-progress and input-echo events. HappyOyster's "permanent"
worlds are session-resumable (an `encrypted_world_id` you `attachWorld()`
back into), not exported/persisted as data. Recommendation: Option D is
the accurate diagnosis (Reactor cannot support the desired physics-based
simulator's world content today, in any format), and Option C — Unity's
own procedural generation, already scaffolded in Phases 5-6
(`WorldSpecification`, `WorldSpecificationValidator`,
`WorldGenerationController`) — is the resulting practical path. Also
surfaced a second-order finding: Reactor can't hand back structured
*intent* either, not just 3D data, so a Unity-native generator's content
decisions need a different "intelligence" source than Reactor — raised as
an open decision, not resolved here. Full detail and citations:
`docs/REACTOR_TO_UNITY_ARCHITECTURE.md`.

No implementation this phase — investigation and documentation only, per
explicit instruction.

## Notes / decisions carried forward

- No Unity Editor in this environment — every phase's "compile" step is a
  careful manual review, not an actual Editor compile. Flag this at each
  phase summary; ask the user to open the project in their Editor at
  natural checkpoints (end of Phase 3 is the first one worth doing).
- OpenWorld Reactor (the world-generation backend, formerly "Reactor
  Lingbot") is **identified for real as of Phase 6**: Reactor (reactor.inc),
  LingBot/LingBot World 2 (Ant Group). Real authentication verified working
  with a live API key. Full generation integration deferred — see
  `docs/OPENWORLD_REACTOR_INTEGRATION.md` for exactly why (it's a live
  video session, not a description-generator) and what's needed to
  proceed (a decision between a companion bridge process and a native
  client — not yet made). Credentials live only in a local, gitignored
  `.env.local` — never committed, never logged.
- Mock service (`MockWorldGenerationService`) is the working end-to-end
  path for everything downstream of world generation — deterministic,
  supports simulated delay/cancellation, still deliberately non-
  interpretive (doesn't parse the prompt to decide content).
- Target packages pinned in `Packages/manifest.json`: Input System 1.7.0,
  TextMeshPro 3.0.6, Test Framework 1.1.33, UnityWebRequest module (for the
  future OpenWorld Reactor client, transport mechanism still unknown).
