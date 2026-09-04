# Architecture — AI-Generated FPV Drone Simulator

Status: **Phase 5 — world-generation data contracts.** This document is the
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
                │ WorldGenerationRequest (full prompt)   │ reads telemetry
                ▼                                          │
┌─────────────────────────────┐                            │
│           AI Layer            │                           │
│  Assets/Scripts/AI            │                           │
│  IWorldGenerationService      │                           │
│  Mock / OpenWorldReactor / ...│                           │
└───────────────┬───────────────┘                           │
                │ ReactorWorldResult (backend-native — structured          │
                │ data and/or a native scene/asset reference)   │
                ▼                                            │
┌─────────────────────────────┐                            │
│         Adapter Layer         │                           │
│  Assets/Scripts/WorldGeneration/Adapters                   │
│  ReactorWorldAdapter → WorldSpecification                   │
└───────────────┬───────────────┘                            │
                │ WorldSpecification (untrusted)             │
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
  Assets/Scripts/Core                          — GameEvents, ServiceLocator, GenerationPipeline
  Assets/Scripts/Utilities                     — deterministic RNG, math helpers
```

## 3. Data flow (end to end)

1. User types a prompt in `PromptInputUI` and clicks **Generate World**.
2. `GenerationUI` calls `WorldGenerationController` (Core), which owns the
   pipeline state machine and keeps the UI responsive (async, see §7).
3. The raw prompt (+ seed/scale/optional prior spec, for "regenerate with a
   tweak") is wrapped, **unmodified**, into a `WorldGenerationRequest`. No
   step before the backend call reduces the prompt to a fixed parameter set.
4. The active `IWorldGenerationService` (Mock, OpenWorldReactor, ...) sends
   the request and returns a `WorldGenerationOutcome` wrapping either a
   `ReactorWorldResult` or a failure reason.
5. `ReactorWorldAdapter` converts `ReactorWorldResult` → `WorldSpecification`.
   This is the only place the backend's native representation is translated
   into Unity's normalized contract — see `docs/WORLD_SPECIFICATION.md` for
   why this is a separate step rather than the backend returning
   `WorldSpecification` directly.
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
    AI/                         Agent 5 — provider-agnostic AI client
                                 (IWorldGenerationService, WorldGenerationOutcome,
                                 MockWorldGenerationService, OpenWorldReactorWorldGenerationService)
    Drone/                      Agent 2 — Rigidbody flight
    Camera/                     Agent 3 — FPV camera rig
    UI/                         Agent 3 + 11 — HUD, OSD, prompt/generation UI
    WorldGeneration/
      Models/                   Agent 4 — WorldGenerationRequest, ReactorWorldResult,
                                 WorldSpecification and its sub-models (see
                                 docs/WORLD_SPECIFICATION.md)
      Adapters/                  Agent 4/5 — ReactorWorldAdapter: ReactorWorldResult -> WorldSpecification
      Validation/                Agent 6 — limits + repair
      Terrain/                   Agent 8 — terrain algorithms
      Environment/                Agent 9 — prefab placement, PrefabRegistry
      Obstacles/                  Agent 10 — gates/rings/checkpoints
      Persistence/                Agent 13 — WorldSaveData, save/load
      (root)                      Agent 7 — WorldGenerator orchestrator
    Gameplay/                   RaceManager, CrashDetector
    Core/                       Agent 1 — GameEvents, ServiceLocator, pipeline state
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
  WORLD_SPECIFICATION.md        prompt -> OpenWorld Reactor -> adapter -> WorldSpecification
                                 pipeline, what Unity owns vs. what Reactor owns
  WORLD_GENERATION.md           spec schema + validation limits (Phase 6+)
  AI_INTEGRATION.md             provider contract, OpenWorld Reactor notes (Phase 6/7)
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

## 6. AI ↔ Unity communication

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

## 7. Error handling strategy

| Failure point | Behaviour |
|---|---|
| Backend request fails (network/timeout/not configured/etc.) | `WorldGenerationOutcome.Success = false` with a `WorldGenerationFailureReason`; UI shows "World generation failed." with **Retry / Use last valid world / Use example world**. Never surfaces raw exception text as the primary message. |
| `ReactorWorldResult`'s structured payload can't be parsed, or its native asset reference can't be resolved | **Not yet applicable** — Phase 5's `ReactorWorldAdapter` only maps the fields it can populate safely (name/description/seed/metadata/prompt) and does not attempt to parse `StructuredPayloadJson` or resolve `NativeAssetReference` at all yet (there's no real payload shape to parse against). Once that parsing is added (Phase 6/7), it must fail closed — reject rather than pass a partial/best-guess `WorldSpecification` downstream — logged with the offending payload (truncated), not shown to the player. |
| Spec fails validation with unrecoverable errors (e.g. negative terrain size) | Pipeline stops before any Unity object is created; `ValidationResult.Errors` surfaced in the debug panel; UI falls back to the same three options as above. |
| Spec has recoverable issues (missing seed, tree count over cap, vague/empty prompt) | Validator repairs in place (generate seed, clamp count, substitute sane defaults) and generation proceeds — "make something cool" must produce a world, not an error. |
| Terrain/environment/obstacle generation throws mid-pipeline | Caught by `WorldGenerator` per stage; partial world is torn down, error surfaced, simulator does not crash. |
| Generated spawn is unsafe (inside terrain/collider) | `SpawnGenerator` retries within bounds, then falls back to a known-safe default (world origin, above terrain) rather than failing the whole world. |
| Save/load reads a spec from a newer/older schema version | `WorldSaveData.GenerationVersion` is checked; a mismatch is reported, not silently misapplied. |

The `WorldGenerationController` (Core) is the single state machine
(`Idle → Requesting → Validating → Generating → Complete/Failed`) that all of
the above funnels through, so the UI only ever needs to react to one state
enum plus an optional error message — it never talks to the AI or generator
layers directly.

## 8. Agent responsibilities (reference)

This mirrors the 14-agent breakdown given in the project brief; kept here so
"who owns this file" is answerable without re-reading the whole brief.

| # | Agent | Owns |
|---|---|---|
| 1 | Architect | This document, folder structure, cross-cutting interfaces |
| 2 | FPV Flight Engineer | `Scripts/Drone/*` |
| 3 | FPV Camera + OSD Engineer | `Scripts/Camera/*`, `Scripts/UI/FPVHUD.cs`, `TelemetryUI.cs` |
| 4 | AI World Designer | `Scripts/WorldGeneration/Models/*`, `Scripts/WorldGeneration/Adapters/*` |
| 5 | AI Integration Engineer | `Scripts/AI/*` |
| 6 | World Validation Engineer | `Scripts/WorldGeneration/Validation/*` (data contracts only as of Phase 5 — `ValidationResult`/`ValidationError`; validation logic itself is not yet implemented) |
| 7 | Procedural World Engineer | `Scripts/WorldGeneration/WorldGenerator.cs`, `WorldSeedManager.cs` |
| 8 | Procedural Terrain Engineer | `Scripts/WorldGeneration/Terrain/*` |
| 9 | Environment/Asset Engineer | `Scripts/WorldGeneration/Environment/*`, `PrefabRegistry` |
| 10 | Obstacle/Racing Engineer | `Scripts/WorldGeneration/Obstacles/*`, `Scripts/Gameplay/RaceManager.cs` |
| 11 | UI/UX Engineer | `Scripts/UI/GenerationUI.cs`, prompt UI, scene layout |
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
