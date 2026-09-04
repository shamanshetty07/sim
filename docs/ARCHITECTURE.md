# Architecture — AI-Generated FPV Drone Simulator

Status: **Phase 2 — initial design.** This document is the contract the rest of
the project is built against. Update it when a phase changes a decision made
here; don't let code and doc drift apart.

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
`Rigidbody`, or `SceneManagement` directly. It emits a `WorldSpecification` —
plain data (POCOs, JSON-serializable, no behaviour). Unity's generator reads
that data and deterministically builds the scene. This boundary is enforced
structurally: the AI layer's only output type is `WorldSpecification`, and
the world-generation layer's only input type is a *validated*
`WorldSpecification`. Nothing in `Assets/Scripts/WorldGeneration` depends on
`Assets/Scripts/AI`, and nothing in `AI` depends on `UnityEngine` types that
create objects.

## 2. System architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                              UI Layer                                │
│  Assets/Scripts/UI                                                   │
│  PromptInputUI · GenerationUI · FPVHUD · TelemetryUI                 │
└───────────────┬────────────────────────────────────────┬────────────┘
                │ prompt string                          │ reads telemetry
                ▼                                          │
┌─────────────────────────────┐                            │
│           AI Layer            │                           │
│  Assets/Scripts/AI            │                           │
│  IWorldGenerationService      │                           │
│  WorldPromptBuilder           │                           │
│  WorldSpecificationParser     │                           │
│  Mock / ReactorLingbot / ...  │                           │
└───────────────┬───────────────┘                           │
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
3. `WorldPromptBuilder` turns the raw prompt (+ optional prior spec, for
   "regenerate with tweak") into an `AIRequest`.
4. The active `IWorldGenerationService` (Mock, ReactorLingbot, ...) sends the
   request and returns an `AIResponse` wrapping raw JSON text or an error.
5. `WorldSpecificationParser` deserializes JSON → `WorldSpecification`
   *models only* (no Unity types). Malformed JSON is rejected here, not
   downstream.
6. `WorldSpecificationValidator` checks the parsed spec against hard limits
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
    Drone/                      Agent 2 — Rigidbody flight
    Camera/                     Agent 3 — FPV camera rig
    UI/                         Agent 3 + 11 — HUD, OSD, prompt/generation UI
    WorldGeneration/
      Models/                   Agent 4 — WorldSpecification data contract
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
  EditMode/                     Agent 14 — validator, parser, seed determinism
  PlayMode/                     Agent 14 — drone controls, generation end-to-end
docs/
  ARCHITECTURE.md               this file
  WORLD_GENERATION.md           spec schema + validation limits
  AI_INTEGRATION.md             provider contract, Reactor/Lingbot notes
  DRONE_PHYSICS.md              flight model, credits to reference repo
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

- **Contract, not code.** The only thing that crosses the AI boundary is
  JSON text in, `WorldSpecification` (or a validation failure) out. See
  `docs/AI_INTEGRATION.md` for the schema and `docs/WORLD_GENERATION.md` for
  field-by-field limits.
- **Provider abstraction.** `IWorldGenerationService.GenerateWorldAsync
  (AIRequest) → Task<AIResponse>`. Concrete implementations:
  `MockWorldGenerationService` (Phase 6, hand-authored example specs +
  simple keyword rules, no network), `ReactorLingbotWorldService` (Phase 7,
  stubbed pending API credentials — see below), with `OpenAIWorldService` /
  `LocalLLMWorldService` documented as future drop-ins behind the same
  interface. Selection is a `Settings/AIServiceConfig` ScriptableObject, not
  a compile-time switch.
- **Secrets.** Never committed. `ReactorLingbotWorldService` reads
  `REACTOR_API_KEY` / `REACTOR_ENDPOINT` / `REACTOR_MODEL` from environment
  variables (or a local, gitignored `.env`-style file for Editor testing).
  Until real credentials are supplied, this service throws a clear
  `NotConfiguredException` rather than silently falling back — the caller
  decides whether to fall back to Mock.
- **Transport.** `UnityWebRequest` (already in the package manifest) inside
  the real service, wrapped so `WorldSpecificationParser` and everything
  downstream is transport-agnostic and testable with canned JSON.
- **Untrusted-input handling.** Every request downstream (Validation
  → WorldGenerator) treats AI output as untrusted input: parse defensively,
  validate before use, never `eval`/reflection-invoke on AI text, never let
  an AI-supplied string become a type name, path, or shell/console command.

## 7. Error handling strategy

| Failure point | Behaviour |
|---|---|
| AI request fails (network/timeout/HTTP error) | `AIResponse.Success = false` with a reason; UI shows "World generation failed." with **Retry / Use last valid world / Use example world**. Never surfaces raw exception text as the primary message. |
| AI returns invalid JSON | `WorldSpecificationParser` catches deserialization errors, returns a `ParseFailure`; pipeline stops before validation. Logged with the offending payload (truncated) for debugging, not shown to the player. |
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
| 4 | AI World Designer | `Scripts/WorldGeneration/Models/*` |
| 5 | AI Integration Engineer | `Scripts/AI/*` |
| 6 | World Validation Engineer | `Scripts/WorldGeneration/Validation/*` |
| 7 | Procedural World Engineer | `Scripts/WorldGeneration/WorldGenerator.cs`, `WorldSeedManager.cs` |
| 8 | Procedural Terrain Engineer | `Scripts/WorldGeneration/Terrain/*` |
| 9 | Environment/Asset Engineer | `Scripts/WorldGeneration/Environment/*`, `PrefabRegistry` |
| 10 | Obstacle/Racing Engineer | `Scripts/WorldGeneration/Obstacles/*`, `Scripts/Gameplay/RaceManager.cs` |
| 11 | UI/UX Engineer | `Scripts/UI/GenerationUI.cs`, prompt UI, scene layout |
| 12 | Performance Engineer | Pooling/LOD/async-generation concerns embedded across §5 |
| 13 | Persistence Engineer | `Scripts/WorldGeneration/Persistence/*` |
| 14 | QA Engineer | `Tests/EditMode/*`, `Tests/PlayMode/*` |

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
- Reactor/Lingbot's actual API shape is unknown until credentials/docs are
  supplied; `ReactorLingbotWorldService` is written against the same
  `IWorldGenerationService` contract as Mock so swapping it in is a config
  change, not a rewrite.
