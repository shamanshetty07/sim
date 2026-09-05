# Architecture — AI-Generated FPV Drone Simulator

Status: **Phase 10 — `AnthropicLLMClient` is a real, working LLM provider.**
Selecting `LLM`/`Anthropic` mode in `RuntimeSimulationBootstrap` (Phase 9)
now reaches a real Anthropic Messages API call (structured output via
forced tool use — never free-text "please output JSON"), not an honest
stub — see `docs/PHASE_10_REAL_LLM.md`. `OpenAiLLMClient`/`LocalLLMClient`
remain stubs. Previously (Phase 9 status below): **the full
prompt-to-playable-world pipeline is wired end to end at runtime.** `WorldGenerationController` (Sim.Core) now drives
`IWorldDesigner` → `IWorldSpecificationValidator` → `WorldGenerator` in one
place (extended, not replaced, from Phase 8's design→validate-only
version — see §6c and `docs/PHASE_9_RUNTIME_PIPELINE.md`). A new thin
composition layer, `Sim.Simulation` (`WorldGenerationRuntimeService`,
`RuntimeSimulationBootstrap`, `IDroneSpawnTarget`), bridges that controller
to the drone and to a runtime prompt UI (`Sim.UI.WorldGenerationUI`) — see
`docs/PHASE_9_RUNTIME_PIPELINE.md` for the full Phase 9 architecture
(state machine, mock vs. LLM mode, dependency injection, cancellation,
threading, drone spawn integration, runtime scene setup). Previously
(Phase 8 status below): **Unity-side WorldGenerator builds a playable world
from a validated WorldSpecification.** `WorldGenerationController`
(Sim.Core) was migrated to drive `IWorldDesigner` directly (see §6b) — see
`docs/WORLD_GENERATION.md` for the full Phase 8 architecture (terrain,
environment, obstacles/course, checkpoints, spawn safety, lighting,
weather, determinism, regeneration). Previously (Phase 7 status below):
world content.** Following the Phase 6.5 investigation
(`docs/REACTOR_TO_UNITY_ARCHITECTURE.md`) confirming OpenWorld Reactor
cannot supply structured/3D world data in any form, the architecture
changed: a general-purpose LLM (`IWorldDesigner`) interprets the prompt
directly into `WorldSpecification`. OpenWorld Reactor's integration code is
kept, isolated, and demoted to an optional, non-authoritative future visual
layer — see `docs/AI_WORLD_DESIGNER.md` for the full reasoning and
`docs/OPENWORLD_REACTOR_INTEGRATION.md` for what was verified about
Reactor itself. This document is the
contract the rest of the project is built against. Update it when a phase
changes a decision made here; don't let code and doc drift apart.

> **User natural-language prompts are the primary input to world
> generation.** Every generated world traces back to the exact prompt text
> the user typed — the prompt is never reduced to a fixed set of parameters
> (e.g. `biome = mountain`) before being handed to the world-generation
> backend. See §6 and `docs/WORLD_SPECIFICATION.md` for how the prompt is
> carried through the pipeline unmodified.

## 0. Inspection summary (Phase 1)

The project starts from an empty, non-git directory — there is no existing
Unity project, no existing drone implementation, and no existing input
configuration to preserve. This is a greenfield build, targeting **Unity
2022.3 LTS** with the **Input System** package (confirmed with the user).
`docs/DRONE_PHYSICS.md` credits the specific ideas taken from the reference
repo (`Venkatesan-M/UnityFPVDroneSimulator`) rather than copying its code.

No Unity Editor is installed in the environment these files are authored in,
so nothing here has been opened or compiled by Unity itself. Every script is
written to compile cleanly against stock Unity/Input System APIs, but the
first real compile happens in the user's own Editor — treat the first `Open
Project` there as part of Phase 3's acceptance check, not an afterthought.

## 1. Guiding principle

> **AI decides *what* the world should contain. Unity decides *how* to build
> it.**

The AI never emits executable code, never touches `GameObject.Instantiate`,
`Rigidbody`, or `SceneManagement` directly. Unity's generator only ever
builds from a *validated* `WorldSpecification` — plain data (POCOs,
JSON-serializable, no behaviour). This boundary is enforced structurally:
nothing in `Assets/Scripts/WorldGeneration` depends on `Assets/Scripts/AI`,
and nothing in `AI` depends on `UnityEngine` types that create objects.

**Revised in Phase 5** — `WorldSpecification` is *Unity's* internal,
normalized contract, not a claim about what the world-generation backend
(OpenWorld Reactor) natively produces. Earlier phases implicitly assumed the
AI layer's job was "return JSON that already looks like WorldSpecification."
That assumption is now explicit and separated into two steps: the backend
returns a `ReactorWorldResult` — a richer, mostly-opaque envelope that can
carry either structured data, a reference to a native scene/asset
representation, or both — and a `ReactorWorldAdapter` converts that into
`WorldSpecification`. This is what keeps the normalized contract from
becoming "a restrictive replacement for the actual generated world" (see
`docs/WORLD_SPECIFICATION.md` for the full reasoning and the open question
this leaves pending real OpenWorld Reactor access).

## 2. System architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                              UI Layer                                │
│  Assets/Scripts/UI                                                   │
│  PromptInputUI · GenerationUI · FPVHUD · TelemetryUI                 │
└───────────────┬────────────────────────────────────────┬────────────┘
                │ WorldDesignRequest (full prompt)       │ reads telemetry
                ▼                                          │
┌─────────────────────────────────┐                        │
│      AI World Design Layer         │                     │
│  Assets/Scripts/AI/WorldDesign      │  ← authoritative     │
│  IWorldDesigner                     │    since Phase 7     │
│  Mock / LLMWorldDesigner(ILLMClient)│                     │
└───────────────┬─────────────────────┘                     │
                │ WorldSpecification (raw, unvalidated)      │
                ▼                                            │
┌─────────────────────────────────────────────────────────┐ │
│              Validation Layer                             │ │
│  Assets/Scripts/WorldGeneration/Validation                │ │
│  WorldSpecificationValidator → ValidationResult            │ │
└───────────────┬───────────────────────────────────────────┘ │
                │ WorldSpecification (validated, clamped)      │
                ▼                                              │
┌─────────────────────────────────────────────────────────┐   │
│           Procedural World Generation Layer                │   │
│  Assets/Scripts/WorldGeneration                            │   │
│  WorldGenerator (orchestrator)                              │   │
│    ├─ TerrainGenerator                                      │   │
│    ├─ EnvironmentGenerator (trees/rocks/buildings)           │   │
│    ├─ ObstacleGenerator (gates/rings/walls/checkpoints)       │   │
│    ├─ SpawnGenerator                                         │   │
│    ├─ LightingGenerator                                       │   │
│    ├─ WeatherGenerator                                        │   │
│    └─ WorldSeedManager (deterministic System.Random per stage)│   │
└───────────────┬───────────────────────────────────────────┘   │
                │ places drone at spawn                          │
                ▼                                                │
┌─────────────────────────────────────────────────────────┐     │
│                 Drone / Gameplay Layer                     │     │
│  Assets/Scripts/Drone, Assets/Scripts/Camera,               │     │
│  Assets/Scripts/Gameplay                                    │     │
│  DroneController → DronePhysics (Rigidbody, FixedUpdate)     │     │
│  DroneInput (Input System + keyboard fallback)                │     │
│  FlightModeController (Angle/Acro/Horizon)                    │     │
│  FlightTelemetry ───────────────────────────────────────────►┼─────┘
│  FPVCameraRig · RaceManager (checkpoints/laps)                │
└─────────────────────────────────────────────────────────┘

Cross-cutting:
  Assets/Scripts/WorldGeneration/Persistence  — WorldSaveData, save/load
  Assets/Scripts/Core                          — WorldGenerationController (Reactor-pipeline
                                                  state machine, Phase 6) + still-pending
                                                  GameEvents/ServiceLocator
  Assets/Scripts/Utilities                     — StableHash, deterministic RNG, math helpers

Isolated, optional, non-authoritative (kept per explicit instruction — not
deleted, not wired into anything above):
  Assets/Scripts/AI                            — IWorldGenerationService, OpenWorldReactor*,
                                                  ReactorWorldResult/Adapter (Phases 5-6).
                                                  Nothing above depends on this; it does not
                                                  depend on Sim.AI.WorldDesign either. See
                                                  docs/AI_WORLD_DESIGNER.md "Future Reactor
                                                  video integration".
```

## 3. Data flow (end to end)

1. User types a prompt in `PromptInputUI` and clicks **Generate World**.
2. A future orchestration point (analogous to `WorldGenerationController`,
   Phase 6, but not yet built for this pipeline — see
   docs/AI_WORLD_DESIGNER.md "What Phase 7 deliberately does not include")
   will keep the UI responsive (async, see §7).
3. The raw prompt (+ optional seed/constraints) is wrapped, **unmodified**,
   into a `WorldDesignRequest`. No step before the designer call reduces
   the prompt to a fixed parameter set.
4. The active `IWorldDesigner` (`MockWorldDesigner`, or `LLMWorldDesigner`
   backed by an `ILLMClient` — OpenAI/Claude/local, Phase 7) sends the
   request and returns a `WorldDesignOutcome` wrapping either a raw
   `WorldSpecification` or a failure reason. For `LLMWorldDesigner`, the
   LLM is instructed to interpret the *entire* prompt directly into
   `WorldSpecification`-shaped JSON, parsed via
   `WorldSpecificationJsonParser` — see docs/AI_WORLD_DESIGNER.md for the
   full request/response flow and its security boundary (never executes
   AI-generated code — deserializes into known data types only).
5. *(Historical, Phases 5-6, not part of the current pipeline)*
   `ReactorWorldAdapter` converted a `ReactorWorldResult` →
   `WorldSpecification` for the OpenWorld Reactor backend. Superseded as
   the authoritative path once Phase 6.5 established Reactor cannot supply
   structured world data — see docs/AI_WORLD_DESIGNER.md "Future Reactor
   video integration". The code remains, isolated and optional.
6. `WorldSpecificationValidator` checks the resulting spec against hard limits
   (`docs/WORLD_GENERATION.md` §Limits) and clamps/repairs recoverable
   issues (e.g. missing seed → generate one; tree count over max → clamp).
   Returns a `ValidationResult` (list of `ValidationError` + the
   possibly-repaired spec). Unrecoverable errors stop the pipeline before
   any Unity object is created.
7. `WorldGenerator.Generate(spec)` runs the deterministic pipeline in §5.
8. `FlightTelemetry` and `GenerationUI` are notified of progress/completion
   via `Core/GameEvents` (a small pub/sub, not direct references), so UI
   stays decoupled from generation internals.
9. Player flies. `RaceManager` tracks checkpoints/laps independently of
   rendering.
10. Optionally, `WorldSaveData` (prompt + seed + spec + metadata) is
    serialized to disk. Reload replays step 7 against the saved spec — no
    mesh data is persisted.

## 4. Folder structure

```
Assets/
  Scenes/
  Scripts/
    AI/                         Agent 5 — Reactor-facing client (Phases 5-6), now isolated
                                 and optional (IWorldGenerationService, WorldGenerationOutcome,
                                 MockWorldGenerationService, OpenWorldReactorWorldGenerationService)
      WorldDesign/                 Agent 5, Phase 7 — the AUTHORITATIVE AI world-content
                                 pipeline (IWorldDesigner, WorldDesignRequest/Outcome,
                                 MockWorldDesigner, LLMWorldDesigner, ILLMClient +
                                 OpenAi/Anthropic/LocalLLMClient, WorldSpecificationJsonParser).
                                 Phase 10: AnthropicLLMClient is a real integration (structured
                                 output via forced tool use — WorldSpecificationToolSchema,
                                 IHttpTransport/UnityWebRequestHttpTransport,
                                 EnvironmentLlmCredentialsProvider, LLMRequestTimeoutException) —
                                 see docs/PHASE_10_REAL_LLM.md. OpenAi/LocalLLMClient still stubs.
    Drone/                      Agent 2 — Rigidbody flight
    Camera/                     Agent 3 — FPV camera rig
    UI/                         Agent 3 + 11 — HUD, OSD; WorldGenerationUI +
                                 WorldGenerationStatusFormatter (Phase 9 prompt UI — zero
                                 world-generation logic, pure display of controller state)
    Simulation/                 Phase 9 — runtime composition only: RuntimeSimulationBootstrap
                                 (builds IWorldDesigner/validator/WorldGenerator/
                                 WorldGenerationController, finds/wires the drone, wires the UI),
                                 WorldGenerationRuntimeService (bridges the controller to
                                 IDroneSpawnTarget once Ready), IDroneSpawnTarget +
                                 DroneControllerSpawnTarget, WorldDesignerMode/LLMProviderKind
    WorldGeneration/
      Models/                   Agent 4 — WorldSpecification and its sub-models, including
                                 CourseSpecification (Phase 7); ExamplePrompts (Phase 9 — shared
                                 default prompt for Editor tooling and runtime UI);
                                 ReactorWorldResult (Phase 5,
                                 now only used by the isolated Assets/Scripts/AI/ Reactor path)
                                 — see docs/AI_WORLD_DESIGNER.md, docs/WORLD_SPECIFICATION.md
      Adapters/                  Agent 4/5 — ReactorWorldAdapter (Phase 5, isolated Reactor
                                 path only — see docs/AI_WORLD_DESIGNER.md)
      Validation/                Agent 6 — limits + repair (real logic since Phase 6)
      Terrain/                   Agent 8 — terrain algorithms
      Environment/                Agent 9 — prefab placement, PrefabRegistry
      Obstacles/                  Agent 10 — gates/rings/checkpoints
      Persistence/                Agent 13 — WorldSaveData, save/load
      (root)                      Agent 7 — WorldGenerator orchestrator
    Gameplay/                   RaceManager, CrashDetector
    Core/                       Agent 1 — WorldGenerationController + WorldGenerationState
                                 (implemented Phase 6; extended Phase 9 to drive generation and
                                 clearing too, states now Idle/Designing/Validating/Generating/
                                 Ready/Failed/Cancelled); GameEvents/ServiceLocator still pending
    Utilities/                  DeterministicRandom, MathX, CurveUtility
  Prefabs/
  Materials/
  Resources/
  Settings/                     ScriptableObject configs (DroneConfig, PrefabRegistry assets)
  Tests/
    EditMode/                   Agent 14 — validator, parser, seed determinism, pure flight math
    PlayMode/                   Agent 14 — drone controls, generation end-to-end
docs/
  ARCHITECTURE.md               this file
  AI_WORLD_DESIGNER.md          Phase 7 — current pipeline: LLM World Designer is now
                                 authoritative; WorldSpecification/Course additions; provider
                                 abstraction; JSON validation/security; Reactor's optional role
  WORLD_SPECIFICATION.md        Phase 5 pipeline design (historical framing — see
                                 AI_WORLD_DESIGNER.md for what's current); what Unity owns
  OPENWORLD_REACTOR_INTEGRATION.md  Phase 6: real Reactor identification/auth findings,
                                 what's verified vs. deferred, credential handling
  REACTOR_TO_UNITY_ARCHITECTURE.md  Phase 6.5: can any Reactor model give Unity a
                                 flyable 3D world? (No.) Options A-D, recommendation.
  WORLD_GENERATION.md           Phase 8: WorldGenerator architecture — terrain, environment,
                                 obstacles/course, checkpoints, spawn safety, lighting, weather,
                                 collision, determinism, regeneration, performance, limitations
  PHASE_9_RUNTIME_PIPELINE.md   Phase 9: the runtime prompt-to-playable-world pipeline — UI,
                                 WorldGenerationRuntimeService, RuntimeSimulationBootstrap,
                                 mock vs. LLM mode, cancellation, threading, drone spawn
                                 integration, runtime scene setup, manual test checklist
  PHASE_10_REAL_LLM.md          Phase 10: AnthropicLLMClient — the real provider, structured
                                 output via forced tool use, configuration, security, testing,
                                 cancellation/timeout, known limitations, future providers
  AI_INTEGRATION.md             superseded by OPENWORLD_REACTOR_INTEGRATION.md / AI_WORLD_DESIGNER.md
  DRONE_PHYSICS.md              flight model, credits to reference repo
  FPV_CAMERA_AND_OSD.md         camera/HUD architecture (Phase 4)
```

Namespaces mirror folders (`Sim.Drone`, `Sim.WorldGeneration.Models`,
`Sim.AI`, ...) so assembly definitions (added once script count justifies
them, to cut Editor recompile time) can be introduced without renaming.

## 5. World-generation lifecycle (Agent 7 pipeline, in order)

1. Clear previously generated world (destroy the `GeneratedWorldRoot`
   container; nothing else in the scene is touched).
2. Initialize `WorldSeedManager` from `spec.seed` — every stage below draws
   from a *stage-specific* `System.Random` derived from the master seed
   (`seed, "terrain"`, `seed, "environment"`, ...) so adding/removing one
   stage's object count doesn't reshuffle another stage's layout.
3. `TerrainGenerator` builds the heightmap/mesh for `spec.terrain`.
4. `EnvironmentGenerator` scatters trees/rocks/vegetation, terrain-snapped.
5. `EnvironmentGenerator` places structures (buildings, bridges, tunnels).
6. `ObstacleGenerator` places gates/rings/walls/poles from `spec.obstacles`,
   in checkpoint order.
7. `ObstacleGenerator` builds the checkpoint sequence used by `RaceManager`.
8. `SpawnGenerator` resolves `spec.spawn`, or derives a safe fallback (clear
   of terrain and obstacles) if the AI omitted or gave an unsafe one.
9. `LightingGenerator` configures sun/ambient/fog from `spec.lighting`.
10. `WeatherGenerator` configures fog density/wind/sky from `spec.weather`.
11. Post-generation validation pass: spawn not inside terrain/colliders,
    obstacle sequence reachable. Logged, not silently ignored.
12. Drone is (re)placed at the resolved spawn transform, velocity zeroed.
13. `GameEvents.WorldGenerationCompleted` fires with a `WorldGenerationResult`
    (counts, timings, warnings) — UI and debug overlay subscribe to it.
14. Pipeline state → `Complete` (or `Failed`, see §7).

Each stage is a small class with one job, so terrain/environment/obstacle
algorithms are swappable without touching the orchestrator (`WorldGenerator`
depends on interfaces — `ITerrainGenerator`, etc. — not concrete classes).

## 6. AI ↔ Unity communication (historical — Reactor pipeline, isolated since Phase 7)

**This section describes the Phase 5-6 Reactor-facing pipeline
(`Assets/Scripts/AI/`), kept in the codebase but no longer authoritative or
wired into anything — see §6a and `docs/AI_WORLD_DESIGNER.md` for the
current pipeline (`Assets/Scripts/AI/WorldDesign/`).** Left as-is below
rather than rewritten, since it remains an accurate description of that
isolated code.

**OpenWorld Reactor is the intended world-generation backend.** Phase 5
looked for an actual OpenWorld Reactor SDK, API, configuration, or
documentation in this development environment — environment variables,
installed CLI tools, packages (npm/pip/gem), and common config
locations — and found **none**. Nothing about its real capabilities (prompt
submission format, local vs. remote execution, whether it returns
structured data vs. scene/asset data, streaming, seed support, determinism
guarantees, or its Unity integration mechanism) is currently known. Nothing
below is invented to fill that gap — where a real answer is needed, it's
called out as an open question in `docs/WORLD_SPECIFICATION.md` instead of
guessed at.

- **The prompt is preserved, not reduced.** `WorldGenerationRequest.Prompt`
  carries the user's complete natural-language text unmodified through the
  entire pipeline — no step between the UI and the backend call parses it
  down to a fixed parameter set (e.g. `biome = mountain`). This is enforced
  by there being no code path that constructs a `WorldGenerationRequest`
  without a prompt string, and by `WorldGenerationMetadata` echoing the
  originating request's ID back through `ReactorWorldResult` so the prompt
  that produced a given world is always traceable.
- **Provider abstraction.** `IWorldGenerationService.GenerateWorldAsync
  (WorldGenerationRequest) → Task<WorldGenerationOutcome>`, where a
  successful outcome carries a `ReactorWorldResult` — the backend's own
  result envelope, not yet Unity's normalized shape. Concrete
  implementations: `MockWorldGenerationService` (hand-authored example,
  clearly documented as non-interpretive — it does not parse the prompt,
  it exists only to prove the contract is usable end-to-end; Phase 6 is
  where a mock actually worth developing against gets built),
  `OpenWorldReactorWorldGenerationService` (stubbed — throws
  `ReactorNotConfiguredException` until real SDK/API access exists; see
  below), with other providers (a local LLM, a different hosted service)
  documented as future drop-ins behind the same interface. Selection is a
  `Settings/AIServiceConfig` ScriptableObject, not a compile-time switch.
- **Result envelope, not an assumed shape.** `ReactorWorldResult` does not
  assume OpenWorld Reactor returns JSON that already looks like
  `WorldSpecification`. It carries a `PayloadKind` (`StructuredData` /
  `NativeSceneReference` / `Unknown`) plus either a raw structured payload
  or a native asset/scene reference, so the architecture doesn't foreclose
  Reactor turning out to generate actual scene/mesh data rather than a
  description of one. `ReactorWorldAdapter` is the only place that
  translates this into `WorldSpecification` — see
  `docs/WORLD_SPECIFICATION.md` for the full reasoning, since this was the
  central open design question this phase had to make a provisional call on
  without real Reactor access.
- **Secrets.** Never committed. `OpenWorldReactorWorldGenerationService`
  is written to read `REACTOR_API_KEY` / `REACTOR_ENDPOINT` /
  `REACTOR_MODEL` from environment variables (or a local, gitignored
  `.env`-style file for Editor testing) once real integration work starts —
  these are placeholder names, not confirmed against any real Reactor
  configuration contract, since none was available to inspect. Until real
  credentials/SDK access exist, the service throws `ReactorNotConfiguredException`
  rather than silently falling back — the caller decides whether to fall
  back to Mock.
- **Transport.** Unknown until Reactor's real integration mechanism is
  known — could be `UnityWebRequest` (already in the package manifest) for
  a REST API, a native SDK/plugin, or something else entirely. The
  interface is written so this is fully encapsulated inside
  `OpenWorldReactorWorldGenerationService`; nothing else in the codebase
  assumes a transport.
- **Untrusted-input handling.** Every request downstream (Adapter →
  Validation → WorldGenerator) treats backend output as untrusted input:
  parse defensively, validate before use, never `eval`/reflection-invoke on
  it, never let a backend-supplied string become a type name, path, or
  shell/console command. This applies whether the payload is structured
  data or a native asset reference.

## 6a. Phase 6 finding: what OpenWorld Reactor actually is

**Update (Phase 6.5):** a full investigation of whether *any* Reactor model
can hand Unity a usable 3D/structured world is in
`docs/REACTOR_TO_UNITY_ARCHITECTURE.md`. Conclusion: no — every model on
the platform is video-only, with no mesh/point-cloud/depth/GLTF/USD/FBX
export and no structured scene-state API, on any of the 8 hosted models
checked (including the "permanent worlds" one). Recommended path: Unity's
own procedural generation is the actual, physical, collidable world
(Option C in that document); Reactor's role is deferred pending either new
Reactor capabilities or a decision to use it as a non-authoritative
decorative layer. This also surfaces a second-order finding worth reading
in full: Reactor cannot hand back *any* structured description of prompt
intent either (not just 3D data) — so a Unity-native generator's content
decisions need a different source of "intelligence" than Reactor/LingBot,
a decision not made in that document, only raised.

Phase 6 identified OpenWorld Reactor as **Reactor (reactor.inc)**, hosting
**LingBot**/**LingBot World 2** (Ant Group models) — confirmed via public
documentation and a real, successful authenticated API call. Full detail,
including exactly what was and wasn't verified, is in
`docs/OPENWORLD_REACTOR_INTEGRATION.md`. The one architecturally important
fact for this document: **LingBot World 2 is a live, steerable video
session, not a one-shot "prompt in, world description out" service, and has
no Unity/C# SDK.** §1/§6's "AI decides what, Unity decides how" and the
`ReactorWorldResult`/adapter design (§1, revised Phase 5) anticipated
Reactor might return rich scene/asset data instead of clean structured
data — the reality is a further step beyond even that: continuous video,
not a retrievable representation at all. Completing real generation
integration is therefore a deliberately deferred, separately-scoped
decision (bridge process vs. native client — see the integration doc), not
attempted blind in Phase 6. What Phase 6 *did* implement for real:
authentication (`OpenWorldReactorWorldGenerationService.MintSessionTokenAsync`,
a genuine `UnityWebRequest` call against the verified real endpoint/schema),
real `WorldSpecificationValidator` logic, and the
`WorldGenerationController` state machine described in §7 below (previously
documented as a design intent, not yet built until this phase).

## 6b. Phase 7: the architecture pivot — AI World Designer is now authoritative

Following the user's explicit acceptance of Phase 6.5's Option D finding,
the architecture changed: **OpenWorld Reactor is not, and will not become,
the source of world geometry.** A general-purpose LLM
(`IWorldDesigner` — `Assets/Scripts/AI/WorldDesign/`) interprets the prompt
directly into `WorldSpecification`, replacing `IWorldGenerationService`/
`ReactorWorldAdapter` as the authoritative path. This is the concrete
"different intelligence source" §6a's second-order finding said would be
needed.

The Reactor-facing code described in §6/§6a is **not deleted** — it remains
in the codebase, fully isolated (`Sim.AI.WorldDesign` has no reference to
it, and it has none to `Sim.AI.WorldDesign`), documented as a possible
future non-authoritative visual layer only. Full reasoning, the new
provider abstraction (`ILLMClient` — OpenAI/Claude/local, none configured
yet), the `WorldSpecification.Course` addition, and the JSON-deserialization
security boundary are in `docs/AI_WORLD_DESIGNER.md`.

## 6c. Phase 8: WorldGenerator — WorldSpecification becomes a Unity scene

`WorldGenerator` (`Assets/Scripts/WorldGeneration/WorldGenerator.cs`) is
implemented: it deterministically builds a `GeneratedWorld` GameObject
hierarchy (terrain, environment, obstacles/course, lighting, weather,
spawn) from a validated `WorldSpecification`, with no dependency on
`Sim.AI`, `Sim.AI.WorldDesign`, or anything Reactor-shaped — per this
phase's explicit instruction, the dependency chain stops at
`WorldSpecification`. Full architecture (terrain implementation choice,
per-generator responsibilities, checkpoint system, spawn-safety rules,
determinism, regeneration) is in `docs/WORLD_GENERATION.md`.

`WorldGenerationController` (§7 below) was migrated from driving the
Reactor-shaped `IWorldGenerationService`/`IReactorWorldAdapter` pipeline
(Phase 6) to driving `IWorldDesigner` directly (Phase 7's contract already
returns a `WorldSpecification`, no separate adapter stage needed) — the
`WorldGenerationState` enum and overall state-machine shape are unchanged,
per this phase's explicit "reuse it, don't build a competing one"
instruction. `WorldGenerator` itself is a separate, later stage this
controller does not yet call — an Editor tool
(`WorldGenerationTestTool.cs`) composes `MockWorldDesigner` → validator →
`WorldGenerator` manually for now; wiring a runtime UI to do the same is
Phase 9 work.

## 6d. Phase 9: the runtime prompt pipeline

`WorldGenerationController` now calls `WorldGenerator.Generate()` itself
after validation succeeds — the "later stage this controller does not yet
call" from §6c is now called, closing the loop from §6c's own forward
reference. This was an in-place extension (new constructor parameter, one
more state, one more method) of the same class, not a second controller.

A new, deliberately thin layer, `Sim.Simulation`, is the only code in the
runtime that is allowed to know about both `WorldGenerationController`
(Sim.Core, still with no reference to `Sim.Drone`) and the drone:
`WorldGenerationRuntimeService` places the drone once the controller
reaches `Ready` (via `IDroneSpawnTarget`, not a direct `DroneController`
reference, so it stays unit-testable without a Play-mode-only Rigidbody
setup), and `RuntimeSimulationBootstrap` is the composition root that
builds the whole chain — `IWorldDesigner` (Mock, or an honest-failure LLM
stub — see §6b, no real provider exists yet), validator, `WorldGenerator`,
controller, service — and wires it to a runtime prompt UI
(`Sim.UI.WorldGenerationUI`, which contains no world-generation logic of
its own; every button press is a pass-through to the service, every status
display a pure function of the controller's own state).

Full reasoning (state machine, cancellation-before-generation semantics,
why Unity's `SynchronizationContext` makes the post-`await` synchronous
tail main-thread-safe, mock vs. LLM mode, how the runtime scene is built,
and everything not yet done) is in `docs/PHASE_9_RUNTIME_PIPELINE.md`.

## 6e. Phase 10: AnthropicLLMClient becomes real

`LLM`/`Anthropic` mode (§6d) now reaches a genuine Anthropic Messages API
call instead of an honest "not yet implemented" stub. Nothing about the
pipeline shape changed — `IWorldDesigner` is still the only abstraction
anything outside `Sim.AI.WorldDesign` depends on, `WorldSpecification` is
still the only contract, validation is still mandatory and unconditional.
What's new is entirely inside `Sim.AI.WorldDesign`: real structured output
(forced tool use with a strict JSON Schema, not "please output JSON" free
text), a small `IHttpTransport` seam so the request/response logic is
unit-testable without a real network call, and a generalized credentials
lookup (`EnvironmentLlmCredentialsProvider`) that any future real provider
in this namespace can reuse. Full detail in `docs/PHASE_10_REAL_LLM.md`.

## 7. Error handling strategy

| Failure point | Behaviour |
|---|---|
| Backend request fails (network/timeout/not configured/etc.) | `WorldGenerationOutcome.Success = false` with a `WorldGenerationFailureReason` (Phase 6: implemented for real in `OpenWorldReactorWorldGenerationService` and driven end-to-end by `WorldGenerationController`, see §6a); UI shows "World generation failed." with **Retry / Use last valid world / Use example world**. Never surfaces raw exception text as the primary message. |
| `ReactorWorldResult`'s structured payload can't be parsed, or its native asset reference can't be resolved | **Still not applicable** — real OpenWorld Reactor output turns out to be neither of those (see §6a) for the one model researched; `ReactorWorldAdapter` still only maps the fields it can populate safely. Revisit if/when a payload shape actually needs parsing. |
| Adapted `WorldSpecification` fails validation | Phase 6: `WorldSpecificationValidator` is now real logic (`Assets/Scripts/WorldGeneration/Validation/WorldSpecificationValidator.cs`) — repairs what's safely repairable, rejects only what genuinely can't be (null spec, missing prompt). `WorldGenerationController` surfaces this as `WorldGenerationFailureReason.ValidationFailed`. |
| Spec fails validation with unrecoverable errors (e.g. negative terrain size) | Pipeline stops before any Unity object is created; `ValidationResult.Errors` surfaced in the debug panel; UI falls back to the same three options as above. |
| Spec has recoverable issues (missing seed, tree count over cap, vague/empty prompt) | Validator repairs in place (generate seed, clamp count, substitute sane defaults) and generation proceeds — "make something cool" must produce a world, not an error. |
| Terrain/environment/obstacle generation throws mid-pipeline | Phase 8: `WorldGenerator.Generate` wraps the whole pipeline in one try/catch; partial world is torn down (`Clear()`), error surfaced, simulator does not crash. |
| Generated spawn is unsafe (inside terrain/collider) | **Revised Phase 8** (supersedes the "falls back to a known-safe default" behavior this row originally described): `SpawnResolver` tries the specified position, then each `AlternateSpawnPoints` entry in order, and if none are safe **fails generation cleanly** rather than silently relocating the drone — an explicit instruction for this phase. `WorldGenerator` tears down the partial world before returning that failure. See docs/WORLD_GENERATION.md "Spawn resolution". |
| Save/load reads a spec from a newer/older schema version | `WorldSaveData.GenerationVersion` is checked; a mismatch is reported, not silently misapplied. |

`WorldGenerationController` (`Assets/Scripts/Core/WorldGenerationController.cs`,
implemented Phase 6, migrated Phase 8 to drive `IWorldDesigner` — see §6c,
extended Phase 9 to also drive `WorldGenerator` — see §6d)
is the single state machine (`Idle → Designing →
Validating → Generating → Ready/Failed/Cancelled` — `WorldGenerationState`,
renamed/extended from Phase 8's `Requesting`/`Completed` this phase) that
all of the above funnels through, so a UI only ever needs to react to
one state enum plus `LastErrorMessage`/`LastFailureReason` — it never talks
to `IWorldDesigner`, the validator, or `WorldGenerator` directly. It
also guards against a stale, already-superseded generation attempt (the
user clicking Generate again before a previous attempt finished)
overwriting a newer attempt's result — see its class remarks.

## 8. Agent responsibilities (reference)

This mirrors the 14-agent breakdown given in the project brief; kept here so
"who owns this file" is answerable without re-reading the whole brief.

| # | Agent | Owns |
|---|---|---|
| 1 | Architect | This document, folder structure, cross-cutting interfaces |
| 2 | FPV Flight Engineer | `Scripts/Drone/*` |
| 3 | FPV Camera + OSD Engineer | `Scripts/Camera/*`, `Scripts/UI/FPVHUD.cs`, `TelemetryUI.cs` |
| 4 | AI World Designer | `Scripts/WorldGeneration/Models/*` (incl. `CourseSpecification`, Phase 7), `Scripts/WorldGeneration/Adapters/*` (Reactor-only, isolated) |
| 5 | AI Integration Engineer | `Scripts/AI/WorldDesign/*` (authoritative, Phase 7) — `Scripts/AI/*` (Reactor client, Phases 5-6) is isolated/optional |
| 6 | World Validation Engineer | `Scripts/WorldGeneration/Validation/*` (real logic since Phase 6 — `WorldSpecificationValidator`, `WorldGenerationLimits`) |
| 7 | Procedural World Engineer | `Scripts/WorldGeneration/WorldGenerator.cs`, `WorldSeedManager.cs`, `GeneratedWorldResult.cs`, `Spawn/*` (implemented Phase 8) |
| 8 | Procedural Terrain Engineer | `Scripts/WorldGeneration/Terrain/*` (implemented Phase 8 — Unity `Terrain`, see docs/WORLD_GENERATION.md) |
| 9 | Environment/Asset Engineer | `Scripts/WorldGeneration/Environment/*` (`EnvironmentGenerator`, `IWorldPrefabRegistry`/`PrimitiveWorldPrefabRegistry` — implemented Phase 8) |
| 10 | Obstacle/Racing Engineer | `Scripts/WorldGeneration/Obstacles/*` (implemented Phase 8); `Scripts/Gameplay/*` — checkpoint generation glue (`CheckpointManager`, `CheckpointTrigger`, Phase 8) plus course flow/timing (`CourseGameplayController`, `RaceTimer`, `IGameplayClock`, `CourseState`, `CourseValidator`, Phase 11) plus crash/fall recovery (`DroneRecoveryController`, `DroneRecoveryConfig`, `DroneRecoveryState`, `Scripts/WorldGeneration/WorldRuntimeBounds.cs`, Phase 12 — see docs/PHASE_12_RECOVERY.md) |
| 11 | UI/UX Engineer | `Scripts/UI/GenerationUI.cs`, prompt UI, scene layout; `Scripts/UI/CourseHUD.cs`/`CourseStatusFormatter.cs` (race HUD, Phase 11) |
| 12 | Performance Engineer | Pooling/LOD/async-generation concerns embedded across §5 |
| 13 | Persistence Engineer | `Scripts/WorldGeneration/Persistence/*` |
| 14 | QA Engineer | `Assets/Tests/EditMode/*`, `Assets/Tests/PlayMode/*` |

In practice these are implemented sequentially by one engineer following the
phase order in `docs/IMPLEMENTATION_PLAN.md`, not as 14 independently running
agents — the files above are tightly coupled (models ↔ validation ↔
generator ↔ persistence all share the same data contract), so one continuous
context is what keeps them consistent.

## 9. Known constraints going in

- No Unity Editor available in the authoring environment — first real
  compile/play happens in the user's Editor. Flagged explicitly at every
  phase boundary rather than claimed as verified.
- No external asset packs assumed. Every generated object has a primitive
  fallback (cube/cylinder/sphere) per `docs/WORLD_GENERATION.md`, with the
  prefab slot in `PrefabRegistry` left open for real assets later.
- OpenWorld Reactor's actual API/SDK shape is unknown — Phase 5 searched this
  environment (env vars, CLI tools, packages, common config paths) and found
  no trace of it. `OpenWorldReactorWorldGenerationService` is written
  against the same `IWorldGenerationService` contract as Mock so completing
  it later is a matter of filling in a stub, not a rewrite — but until real
  access exists, nothing about its transport, auth, or payload shape is
  more than a documented placeholder. See `docs/WORLD_SPECIFICATION.md`
  "Open questions."
- Tests live at `Assets/Tests/{EditMode,PlayMode}`, not a top-level
  `Tests/`. Unity only compiles/discovers scripts under `Assets/` (and
  `Packages/`) — a `Tests/` folder outside `Assets/` would never be picked
  up by the Test Runner. The brief's illustrative layout listed it as a
  sibling of `Assets/`; this project deviates from that specifically for it
  to actually work.
