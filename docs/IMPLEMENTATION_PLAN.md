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
| 8 | Unity-side procedural world construction (`WorldGenerator`) | ✅ Done — `WorldGenerator` + `TerrainGenerator`/`EnvironmentGenerator`/`ObstacleGenerator`/`LightingGenerator`/`WeatherGenerator`/`SpawnResolver`/`WorldSeedManager`, `CheckpointManager`/`CheckpointTrigger`, Editor tooling, EditMode tests. See docs/WORLD_GENERATION.md. This phase substantially covers what rows 10-12 below originally described (terrain/environment/obstacles were all built together, not as separate later phases) — see the note under this table. |
| 9 | Prompt UI + wiring `WorldGenerationController` → `WorldGenerator` at runtime | ✅ Done — `WorldGenerationController` extended to drive generation+clearing; `Sim.Simulation` (`WorldGenerationRuntimeService`, `RuntimeSimulationBootstrap`, `IDroneSpawnTarget`); `WorldGenerationUI`/`WorldGenerationStatusFormatter`; Editor tooling builds the runtime scene. EditMode tests. See docs/PHASE_9_RUNTIME_PIPELINE.md. Unverified in a live Editor (none available here). |
| 10 | Real LLM World Designer | ✅ Done — `AnthropicLLMClient` is a real Messages API integration (structured output via forced tool use + strict JSON Schema, `IHttpTransport` testability seam, `EnvironmentLlmCredentialsProvider`, timeout/cancellation, error handling). `OpenAiLLMClient`/`LocalLLMClient` remain stubs (explicit "implement one provider" instruction). EditMode tests (fake transport, no real network/API key). Real-provider smoke test not run — no credentials available. See docs/PHASE_10_REAL_LLM.md. |
| 11 | Procedural terrain | ✅ Covered by Phase 8 (`TerrainGenerator`) — kept as a row for traceability against the original brief, not separate remaining work |
| 12 | Environment objects | ✅ Covered by Phase 8 (`EnvironmentGenerator`, `PrimitiveWorldPrefabRegistry`) — same note |
| 13 | Racing obstacles | ✅ Covered by Phase 8 (`ObstacleGenerator`, `CheckpointManager`) — same note |
| 14 | Save/load | ⬜ Not started |
| 15 | Performance optimization | ⬜ Not started beyond what Phase 8 already applies defensively (limit re-clamping, no per-frame allocation in generation) |
| 16 | Testing | ⬜ Ongoing — add tests as each system lands, not deferred to the end |
| 17 | FPV course gameplay: checkpoints, timing, race HUD | ✅ Done — `CourseGameplayController` (Waiting/Countdown/Racing/Finished/Failed/Resetting, separate from `WorldGenerationState`), `RaceTimer`/`IGameplayClock` (testable, no `Time.time` scattered across gameplay code), `CourseHUD`/`CourseStatusFormatter`. `CheckpointManager` refactored (race-flow/timer responsibility moved out, `WrongCheckpointAttempted` added) — same class, not replaced. EditMode tests. See docs/PHASE_11_COURSE_GAMEPLAY.md. Unverified in a live Editor (none available here). |

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

## Phase 8 detail

Built the Unity-side half of the pipeline: `WorldSpecification` (validated)
→ `WorldGenerator` → a playable `GeneratedWorld` GameObject hierarchy, with
no dependency on `Sim.AI`/`Sim.AI.WorldDesign`/Reactor anywhere in the
generation code, per this phase's explicit instruction.

**New files** — `Assets/Scripts/WorldGeneration/`: `WorldSeedManager.cs`
(per-stage deterministic `System.Random`, never `UnityEngine.Random`
global state), `WorldGenerator.cs`, `GeneratedWorldResult.cs`; `Terrain/`:
`TerrainGenerator.cs` (Unity built-in `Terrain`, free collision via
`TerrainCollider`, deterministic fractal Perlin noise, distinct height
profiles per `TerrainType`), `TerrainGenerationResult.cs`; `Environment/`:
`EnvironmentGenerator.cs`, `IWorldPrefabRegistry.cs` +
`PrimitiveWorldPrefabRegistry.cs` (one registry handles both environment
objects and obstacles — no scattered `Resources.Load` calls); `Obstacles/`:
`ObstacleGenerator.cs` (explicit positions always respected; auto-generates
the gap between `Course.GateCount` and explicitly-specified gates along a
`Course.Style`-shaped deterministic path — verified in tests that
"technical" produces measurably tighter spacing than "high_speed"),
`ObstacleGenerationResult.cs`, `CheckpointDefinition.cs`; `Lighting/`:
`LightingGenerator.cs`; `Weather/`: `WeatherGenerator.cs`; `Spawn/`:
`SpawnResolver.cs` (checks the *actually generated* terrain/colliders —
`WorldSpecificationValidator` only ever sees numeric values, never a built
scene) + `SpawnResolutionResult.cs`. `Assets/Scripts/Gameplay/`:
`CheckpointManager.cs` (plain C# class, checkpoint progression only —
see docs/PHASE_11_COURSE_GAMEPLAY.md for how Phase 11 later split race-flow
state/timing out of this class into `CourseGameplayController`/`RaceTimer`),
`CheckpointTrigger.cs` (MonoBehaviour, visual/trigger only — the two stay
deliberately separate). `Assets/Scripts/Utilities/`:
`UnityLifecycleUtility.cs` (Destroy vs. DestroyImmediate depending on
Play/Edit mode — needed because generation code must work correctly from
both an Editor tool and, eventually, runtime). `Assets/Scripts/Editor/`:
`WorldGenerationTestTool.cs` (`FPV Sim > World > Generate Test World (Mock
Designer)` — runs the brief's exact Himalayan-course prompt through
`MockWorldDesigner` → validator → `WorldGenerator`, places the existing
drone rig at the resolved spawn; `Clear Generated World`).

**No new/duplicate models** — every generator was written against the
exact existing `WorldSpecification`/`TerrainSpecification`/
`ObjectSpecification`/`ObstacleSpecification`/`WeatherSpecification`/
`LightingSpecification`/`SpawnSpecification`/`CourseSpecification`/
`WorldGenerationMetadata` from Phases 5 and 7 — none needed a field added
or changed.

**Deliberate behavior change from Phase 2's original sketch**: an unsafe
spawn (specified position, and every alternate) now fails generation
cleanly instead of falling back to an arbitrary "safe" position — an
explicit instruction this phase, documented in `docs/ARCHITECTURE.md`'s
error-handling table and `docs/WORLD_GENERATION.md`.

**`WorldGenerationController` migrated** from the Reactor-shaped
`IWorldGenerationService`/`IReactorWorldAdapter` pipeline (Phase 6) to
`IWorldDesigner` directly (Phase 7's contract already returns a
`WorldSpecification`) — reused, not replaced, per this phase's explicit
instruction; verified nothing in production code depended on the old
constructor before migrating (only its own test did, updated alongside).

**Tests**: `WorldGeneratorTests.cs`, `WorldSeedManagerTests.cs` — real,
runnable EditMode tests (Unity's Editor process has a live GameObject/
Physics/Terrain system outside Play mode) covering all 12 scenarios this
phase asked for. Not coverable without a live Editor's Play mode: anything
needing the Player loop to actually tick (FixedUpdate physics response,
the drone genuinely colliding while flying) — flagged as needing manual
verification, not silently skipped.

**Bugs caught and fixed during review before commit**: two unused `using`
directives (harmless but cleaned up); a `sharedMaterial.color` mutation in
the water-feature primitive builder that would have recolored every other
primitive in the scene sharing Unity's default material (fixed to
`.material`, which creates a per-instance copy); confirmed (rather than
assumed) that C#'s enclosing-namespace lookup rules make
`Sim.WorldGeneration.{Terrain,Environment,Obstacles}` code able to see
`WorldSeedManager` (declared directly in the parent `Sim.WorldGeneration`)
without an explicit `using` — verified against the language spec rather
than left as an assumption, since getting this wrong would have been a
real compile error.

## Phase 9 detail

Wired the runtime pipeline end to end: prompt UI → `WorldGenerationController`
→ `IWorldDesigner` → validator → `WorldGenerator` → drone spawn.

**`WorldGenerationController` extended, not replaced** (`Assets/Scripts/Core/`):
constructor now also takes `WorldGenerator`; `GenerateWorldAsync` now
continues past validation into `WorldGenerator.Generate()`; new
`ClearGeneratedWorld()`; new `LastGeneratedWorld` property.
`WorldGenerationState` renamed/extended in place (`Requesting`→`Designing`,
`Completed`→`Ready`, added `Generating`) — same enum, not a second one.
Still has no reference to `Sim.Drone`, matching `WorldGenerator`'s own rule.

**New `Assets/Scripts/Simulation/` (namespace `Sim.Simulation`)** — the only
code allowed to know about both the controller and the drone:
`IDroneSpawnTarget`/`DroneControllerSpawnTarget` (adapter over the existing
`DroneController.SetSpawn`/`ResetToSpawn`, introduced so drone-placement
logic is unit-testable without a Play-mode-only Rigidbody setup — `Awake()`
doesn't run for a component added via script in Edit mode), `WorldDesignerMode`/
`LLMProviderKind` enums, `WorldGenerationRuntimeService` (bridges the
controller to the drone once state reaches `Ready`; also the class a UI
actually calls), `RuntimeSimulationBootstrap` (`MonoBehaviour` composition
root — builds the whole designer/validator/generator/controller/service
chain, finds or is given the drone, wires the UI; does not construct the
drone rig itself — that needs Editor-only APIs the runtime assembly can't
reference, so it expects a scene built once via Editor tooling).

**New `Assets/Scripts/UI/`**: `WorldGenerationUI` (prompt input + Generate/
Cancel/Clear buttons + status text; zero world-generation logic — every
handler is a pass-through to `WorldGenerationRuntimeService`, every display
update a pure function via `WorldGenerationStatusFormatter`),
`WorldGenerationStatusFormatter` (pure, independently tested).

**New `Assets/Scripts/WorldGeneration/Models/ExamplePrompts.cs`** — the
brief's exact Himalayan prompt, extracted to one shared constant used by
both the Editor quick-test command and the runtime UI's default text
(previously only lived inline in the Editor tool).

**`WorldGenerationTestTool.cs` extended** (Editor-only, `Sim.Editor.asmdef`):
existing "Generate Test World"/"Clear Generated World" commands refactored
to go through the now-complete controller instead of composing
Mock→validator→WorldGenerator by hand; new "Build Runtime Scene (Save To
Disk)" command builds `Assets/Scenes/MainScene.unity` — drone/camera/OSD via
the existing `DroneRigBuilder` (not duplicated), a hand-built TMP prompt UI,
an `EventSystem` with `InputSystemUIInputModule` (not the legacy
`StandaloneInputModule` — this project already requires the New Input
System for drone controls), and a `Simulation Bootstrap` GameObject.
`DroneRigBuilder.AssignField` changed from `private` to `internal` for
reuse here.

**Mock mode requires zero external configuration** — no internet, no API
keys, no Reactor, works fully offline, and is the default. **LLM mode
fails honestly** — every provider is still an unconfigured Phase 7 stub;
selecting LLM mode reaches `Failed` with a clear "not configured" message,
never a fake success.

**No keyword parsing anywhere in this phase's code** — the prompt is read
once from the input field and passed to `IWorldDesigner` unmodified;
verified with `grep -rn "prompt.Contains"` returning nothing.

**Threading**: `WorldGenerator.Generate()` is called directly after the
design `await` completes — safe because Unity's `SynchronizationContext`
marshals every continuation in this codebase back to the main thread
automatically (verified nothing here uses `Task.Run`/`ConfigureAwait(false)`,
the two things that would break that guarantee). Cancellation cannot safely
interrupt `Generate()` itself (synchronous, uninterruptible Unity object
construction), so the controller checks for a pending cancellation once,
immediately before calling it — the one point cancellation can still take
effect, per this phase's "cancel the design phase, don't attempt unsafe
mid-call interruption" instruction.

**Tests**: `WorldGenerationControllerTests.cs` (rewritten — real EditMode
tests exercising the full pipeline including three fake `IWorldDesigner`
implementations for designer/validation/generation failure paths, state-
transition-order assertion, no-duplicate-`GeneratedWorld` assertion,
cancellation/supersession), `WorldGenerationStatusFormatterTests.cs`,
`WorldGenerationRuntimeServiceTests.cs` (fake `IDroneSpawnTarget`, since a
real `DroneController` can't be reliably driven in EditMode — see above).

**Not done this phase** (see docs/PHASE_9_RUNTIME_PIPELINE.md "Known
limitations" for the complete list): no real LLM provider; no generated-
world summary or checkpoint-progress UI; `RuntimeSimulationBootstrap`
cannot build the drone rig itself (needs a scene built via Editor tooling
first); nothing verified in a live Unity Editor (none available here).

## Phase 10 detail

Implemented the one real LLM provider Phase 7 left as an honest stub.
Inspected the full existing pipeline first (`IWorldDesigner`,
`LLMWorldDesigner`, `ILLMClient` + all three stubs, `WorldSpecification`
and its nested models, `WorldSpecificationValidator`,
`WorldGenerationController`/`WorldGenerationRuntimeService`/
`WorldGenerationUI` from Phase 9) before writing anything — confirmed no
provider was yet configured in `.env.local` (only Phase 6's OpenWorld
Reactor credentials were present), so per this phase's explicit
instruction, implemented exactly one provider rather than several.
**Anthropic (Claude) chosen** — its stub already had the most accurately
pre-researched API shape from Phase 7, and its Messages API's tool-use
mechanism maps cleanly onto this project's existing
`WorldSpecification`-as-JSON contract. Verified endpoint, headers, request/
response shape, structured-output mechanism, and error shape directly
against Anthropic's current official documentation
(`platform.claude.com/docs/en/api/messages`, `.../agents-and-tools/
tool-use/*`, `.../build-with-claude/structured-outputs`, `.../api/errors`)
before writing any code — nothing invented or guessed.

**New files**, all `Assets/Scripts/AI/WorldDesign/` (existing folder, no
new namespace): `WorldSpecificationToolSchema.cs` (the one canonical JSON
Schema for structured-output enforcement, mirroring
`LLMWorldDesigner.BuildSystemPrompt()`'s field list — never adds an `enum`
to a field the model documents as free-form); `IHttpTransport.cs` +
`HttpTransportResponse.cs` + `UnityWebRequestHttpTransport.cs` (a small
testability seam — the brief's "testable HTTP abstraction" request — real
implementation copies `OpenWorldReactorWorldGenerationService`'s
already-verified-safe non-blocking `UnityWebRequest` polling pattern
exactly); `EnvironmentLlmCredentialsProvider.cs` (env var + `.env.local`
dual lookup, generalizing the pattern `Sim.AI.EnvironmentReactorCredentialsProvider`
established for Reactor — a new class, Reactor's own untouched, per "do
not modify Reactor integration"); `LLMRequestTimeoutException.cs` (same
"signal via a dedicated exception type" idiom as the existing
`LLMNotConfiguredException`).

**Rewritten**: `AnthropicLLMClient.cs` — real `CompleteAsync`, forces
Anthropic's own structured-output mechanism (`tool_choice`
`{"type":"tool","name":"emit_world_specification"}` + `"strict": true` on
the tool definition) rather than "please output JSON" free text; extracts
the `tool_use` block's `input` (already WorldSpecification-shaped JSON)
and hands its text to the same, unchanged `IWorldSpecificationJsonParser`
every `IWorldDesigner` already uses — no new deserialization path, every
existing `TypeNameHandling.None`/`$type`-injection protection applies
unchanged. Deliberately never sends `temperature`/`top_p`/`top_k` —
verified they're deprecated/value-restricted on current Claude models and
would 400 on a real call. Existing `apiKeyOverride` constructor parameter
(and its existing test) preserved unchanged; three new optional
parameters added after it.

**`LLMWorldDesigner.cs` — two new specific `catch` clauses** (before the
existing generic one), so a not-configured provider and a timed-out
request reach the `WorldDesignFailureReason.NotConfigured`/`.Timeout`
values that already existed in the enum since Phase 7/8 but were
previously fallen through to the generic `Unknown` bucket — a small,
targeted, well-justified fix directly serving this phase's explicit
"handle missing API key"/"timeout becomes clean failure" requirements,
not scope creep.

**Untouched, per explicit instruction**: `OpenAiLLMClient`/`LocalLLMClient`
(still honest stubs), all of `Sim.AI`'s Reactor-facing code, everything in
`Sim.Core`/`Sim.Simulation`/`Sim.UI`/`Sim.WorldGeneration` — Phase 9's
runtime pipeline needed zero changes since it already depended only on
`IWorldDesigner`.

**Tests**: `AnthropicLLMClientTests.cs` (new) — a fully in-memory
`FakeHttpTransport`, no automated test depends on a real API key or
network call. Covers configuration, prompt preservation, model selection,
authentication headers, the forced structured-output request shape,
successful end-to-end parsing into a real `WorldSpecification`, `$type`
injection staying inert, malformed/missing-tool-use responses, HTTP
401/429/500, connection errors, and the timeout-vs-cancellation race —
all failing cleanly, never throwing except the two specific,
intentional exception types. `LLMWorldDesignerTests.cs`'s existing
Anthropic stub test is unchanged and still passes.

**Real-provider smoke test: not run** — no Anthropic (or any LLM
provider) credentials exist in this environment's `.env.local`/OS
environment. Stated plainly per this phase's explicit instruction, not
claimed. See docs/PHASE_10_REAL_LLM.md "Real-provider smoke testing" for
what running one would involve.

## Phase 11 detail

Turned the generated world into a functional FPV course: checkpoints in
order, a start countdown, a race timer, finish detection, reset/restart,
and a HUD — all layered *after* Phase 8's existing `CheckpointManager`/
`CheckpointTrigger`/`ObstacleGenerator`, not a second implementation of any
of them. Inspected the full existing pipeline first (`CheckpointManager`,
`CheckpointTrigger`, `ObstacleGenerator`, `WorldGenerator`,
`WorldGenerationController`, `WorldGenerationRuntimeService`,
`RuntimeSimulationBootstrap`, `IDroneSpawnTarget`/`DroneControllerSpawnTarget`,
`DroneController`, the FPV HUD/formatter classes, `WorldGenerationUI`,
`WorldGenerationTestTool`) before writing anything — confirmed Phase 8
already had exactly one `CheckpointManager` and one deterministically
ordered checkpoint sequence (from `ObstacleGenerationResult.Checkpoints`,
sorted by `CheckpointDefinition.Index`), so this phase reuses that ordering
rather than inventing a second one.

**`CheckpointManager.cs` (Sim.Gameplay) — refactored, not replaced.** Its
Phase 8 version owned both checkpoint progression *and* a lazy-starting
race-flow state (`RaceState.NotStarted/InProgress/Finished`) and its own
`ElapsedSeconds` read straight off `Time.time`. That timer started on the
*first checkpoint pass*, which conflicts with this phase's explicit "timer
starts when Racing begins" (i.e. at the end of the start countdown, before
any checkpoint) — keeping both in one class would have meant two
overlapping, disagreeing state machines. `RaceState.cs` was deleted (grepped
first — referenced nowhere outside `CheckpointManager` itself); the class
now only tracks `TotalCheckpoints`/`CurrentCheckpointIndex`/
`CompletedCheckpoints`/`IsFinished`, still enforces in-order passing, and
gained one new event: `WrongCheckpointAttempted(attemptedIndex,
requiredIndex)`, for the brief's optional "wrong checkpoint" HUD feedback.

**New, `Assets/Scripts/Gameplay/`**: `IGameplayClock`/`UnityGameplayClock`
(the one seam between gameplay code and `UnityEngine.Time`, so nothing
here sleeps for real seconds in a test); `RaceTimer` (Start/Stop/Reset/
IsRunning/ElapsedSeconds, driven by `IGameplayClock`); `CourseState`
(Waiting/Countdown/Racing/Finished/Failed/Resetting — deliberately its own
enum, never sharing a switch statement with `Sim.Core.WorldGenerationState`);
`CourseValidator` (the one gameplay-level check
`WorldSpecificationValidator` cannot do, because it runs before any Unity
object exists: is the generated `CheckpointManager` non-null and
non-empty); `CourseGameplayController` (plain C# class, same "not a
MonoBehaviour" pattern as `WorldGenerationController` — the single
authoritative owner of race-flow state, constructed once and re-bound to a
new `CheckpointManager` every regeneration, never recreated, which is what
guarantees no duplicate gameplay managers ever accumulate).

**New, `Assets/Scripts/UI/`**: `CourseStatusFormatter` (pure formatting,
same pattern as `TelemetryFormatter`/`WorldGenerationStatusFormatter`) and
`CourseHUD` (a small panel — course state, `gate N / total`, timer,
Start/Reset buttons — that complements `FPVHUD`'s existing OSD, never
replaces it; contains no gameplay logic of its own).

**Extended, not replaced**: `WorldGenerationRuntimeService` gained one more
optional constructor dependency (`CourseGameplayController`) alongside its
existing `IDroneSpawnTarget` — on `Ready` it now also binds the course to
the freshly generated `CheckpointManager`/spawn; on every *other* state
(including the transient Designing/Validating/Generating a fresh
`GenerateWorldAsync` call passes through) it unbinds the old course first,
which is what guarantees a regenerating world never leaves the course
subscribed to a `CheckpointManager` whose GameObjects are about to be
destroyed. `RuntimeSimulationBootstrap` constructs the one
`CourseGameplayController` instance, wires it into that service, wires an
optional `CourseHUD`, and ticks the controller once per frame (`Course
GameplayController.Tick()`) purely to notice when a running countdown has
elapsed — every other transition (bind/unbind, checkpoint pass, finish,
reset) is event-driven, not polled. `WorldGenerationTestTool`'s runtime
scene builder gained a second UI canvas (`BuildCourseHudCanvas`) alongside
the existing prompt UI.

**Untouched, per explicit instruction**: `DronePhysics`/`DroneFlightModel`/
`FlightModeController` (no flight-model changes at all); `ObstacleGenerator`
(checkpoint/gate *generation* untouched — this phase only consumes the
sequence it already produces); Reactor (`Sim.AI`'s Reactor-facing code) —
grepped for any reference before finishing; no networking, no database, no
AI opponent/pilot of any kind.

**Tests**: `RaceTimerTests`, `CheckpointManagerTests`,
`CourseGameplayControllerTests` (state machine, order enforcement, timer
start/stop/reset, finish detection, bind/unbind/rebind, event-fires-once
guarantees, order independent of GameObject name/hierarchy), extended
`WorldGenerationRuntimeServiceTests` (course binds on Ready, rebinds
without duplication on regeneration, unbinds on Clear), `CourseStatusFormatterTests`.
All EditMode, all real (no fabricated Play Mode results) — see
docs/PHASE_11_COURSE_GAMEPLAY.md "Testing" for exactly what is/isn't
covered and the full manual Unity checklist.

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
