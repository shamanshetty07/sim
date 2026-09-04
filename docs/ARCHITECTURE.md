# Architecture — AI-Generated FPV Drone Simulator

Status: **Phase 6 — real OpenWorld Reactor auth + validation + state model.**
See `docs/OPENWORLD_REACTOR_INTEGRATION.md` for the full Phase 6 findings —
OpenWorld Reactor is confirmed to be Reactor (reactor.inc)/LingBot (Ant
Group), real authentication was verified against the live API, but the
product's actual shape (a live steerable video session, not a one-shot
world-description generator) means full generation integration is a
deliberately deferred, separately-scoped decision, not implemented yet.
This document is the
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
      Validation/                Agent 6 — limits + repair (real logic since Phase 6)
      Terrain/                   Agent 8 — terrain algorithms
      Environment/                Agent 9 — prefab placement, PrefabRegistry
      Obstacles/                  Agent 10 — gates/rings/checkpoints
      Persistence/                Agent 13 — WorldSaveData, save/load
      (root)                      Agent 7 — WorldGenerator orchestrator
    Gameplay/                   RaceManager, CrashDetector
    Core/                       Agent 1 — WorldGenerationController + WorldGenerationState
                                 (implemented Phase 6); GameEvents/ServiceLocator still pending
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
  OPENWORLD_REACTOR_INTEGRATION.md  Phase 6: real Reactor identification/auth findings,
                                 what's verified vs. deferred, credential handling
  REACTOR_TO_UNITY_ARCHITECTURE.md  Phase 6.5: can any Reactor model give Unity a
                                 flyable 3D world? (No.) Options A-D, recommendation.
  WORLD_GENERATION.md           spec schema + validation limits (Phase 6+)
  AI_INTEGRATION.md             provider contract, superseded by OPENWORLD_REACTOR_INTEGRATION.md
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

## 7. Error handling strategy

| Failure point | Behaviour |
|---|---|
| Backend request fails (network/timeout/not configured/etc.) | `WorldGenerationOutcome.Success = false` with a `WorldGenerationFailureReason` (Phase 6: implemented for real in `OpenWorldReactorWorldGenerationService` and driven end-to-end by `WorldGenerationController`, see §6a); UI shows "World generation failed." with **Retry / Use last valid world / Use example world**. Never surfaces raw exception text as the primary message. |
| `ReactorWorldResult`'s structured payload can't be parsed, or its native asset reference can't be resolved | **Still not applicable** — real OpenWorld Reactor output turns out to be neither of those (see §6a) for the one model researched; `ReactorWorldAdapter` still only maps the fields it can populate safely. Revisit if/when a payload shape actually needs parsing. |
| Adapted `WorldSpecification` fails validation | Phase 6: `WorldSpecificationValidator` is now real logic (`Assets/Scripts/WorldGeneration/Validation/WorldSpecificationValidator.cs`) — repairs what's safely repairable, rejects only what genuinely can't be (null spec, missing prompt). `WorldGenerationController` surfaces this as `WorldGenerationFailureReason.ValidationFailed`. |
| Spec fails validation with unrecoverable errors (e.g. negative terrain size) | Pipeline stops before any Unity object is created; `ValidationResult.Errors` surfaced in the debug panel; UI falls back to the same three options as above. |
| Spec has recoverable issues (missing seed, tree count over cap, vague/empty prompt) | Validator repairs in place (generate seed, clamp count, substitute sane defaults) and generation proceeds — "make something cool" must produce a world, not an error. |
| Terrain/environment/obstacle generation throws mid-pipeline | Caught by `WorldGenerator` per stage; partial world is torn down, error surfaced, simulator does not crash. |
| Generated spawn is unsafe (inside terrain/collider) | `SpawnGenerator` retries within bounds, then falls back to a known-safe default (world origin, above terrain) rather than failing the whole world. |
| Save/load reads a spec from a newer/older schema version | `WorldSaveData.GenerationVersion` is checked; a mismatch is reported, not silently misapplied. |

`WorldGenerationController` (`Assets/Scripts/Core/WorldGenerationController.cs`,
implemented Phase 6) is the single state machine (`Idle → Requesting →
Validating → Completed/Failed/Cancelled` — `WorldGenerationState`) that all
of the above funnels through, so a future UI only ever needs to react to
one state enum plus `LastErrorMessage`/`LastFailureReason` — it never talks
to `IWorldGenerationService`, the adapter, or the validator directly. It
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
| 4 | AI World Designer | `Scripts/WorldGeneration/Models/*`, `Scripts/WorldGeneration/Adapters/*` |
| 5 | AI Integration Engineer | `Scripts/AI/*` |
| 6 | World Validation Engineer | `Scripts/WorldGeneration/Validation/*` (real logic since Phase 6 — `WorldSpecificationValidator`, `WorldGenerationLimits`) |
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
