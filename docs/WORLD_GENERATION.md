# World Generation — Phase 8

The Unity-side half of the pipeline established in Phase 7:

```
WorldSpecification (validated)
        ↓
WorldGenerator.Generate(specification)
        ↓
GeneratedWorldResult (root GameObject, spawn, CheckpointManager)
        ↓
Playable Unity world — the existing FPV drone (Phase 3-4) flies in it
```

`WorldGenerator` (`Assets/Scripts/WorldGeneration/WorldGenerator.cs`) is the
orchestrator only. Every real generation concern lives in its own class,
injected with sensible defaults:

| Concern | Class | Folder |
|---|---|---|
| Terrain shape | `TerrainGenerator` | `WorldGeneration/Terrain/` |
| Environment object placement | `EnvironmentGenerator` | `WorldGeneration/Environment/` |
| Primitive fallback geometry | `PrimitiveWorldPrefabRegistry` (`IWorldPrefabRegistry`) | `WorldGeneration/Environment/` |
| Obstacles + course layout | `ObstacleGenerator` | `WorldGeneration/Obstacles/` |
| Lighting | `LightingGenerator` | `WorldGeneration/Lighting/` |
| Weather/atmosphere | `WeatherGenerator` | `WorldGeneration/Weather/` |
| Spawn safety | `SpawnResolver` | `WorldGeneration/Spawn/` |
| Deterministic per-stage RNG | `WorldSeedManager` | `WorldGeneration/` (root) |
| Checkpoint race state | `CheckpointManager`/`CheckpointTrigger` | `Gameplay/` |

## Input models — none duplicated

Phase 8 uses the existing Phase 5/7 models exactly as they are:
`WorldSpecification`, `TerrainSpecification`, `ObjectSpecification` (the
brief's "EnvironmentSpecification" — there is no separate class; the list
`WorldSpecification.EnvironmentObjects` of `ObjectSpecification` entries
*is* the environment specification), `ObstacleSpecification`,
`WeatherSpecification`, `LightingSpecification`, `SpawnSpecification`,
`CourseSpecification`, `WorldGenerationMetadata`. No field was changed and
no new model was added — every generator was written to fit the existing
JSON contract, not the other way around.

## Generated hierarchy

```
GeneratedWorld
├── Terrain                    (UnityEngine.Terrain + TerrainCollider)
├── Environment
│   ├── Trees
│   ├── Rocks
│   ├── Buildings
│   ├── Vegetation
│   └── Structures             (bridges, tunnels, water features, anything else)
├── Obstacles
│   ├── Gates
│   ├── Rings
│   ├── Tunnels
│   ├── Checkpoints
│   └── Other                  (walls, poles, landing pads, start/finish lines)
├── Sun                        (directional light, from LightingGenerator)
├── Weather                    (rain particles / WindZone, from WeatherGenerator)
└── Spawn                      (marker at the resolved spawn transform)
```

Every generated object is parented under `GeneratedWorld`, matching the
spec exactly. `WorldGenerator.Clear()` destroys this single root, which
takes the entire hierarchy with it — regeneration safety by construction,
not by remembering to track individual objects (see "Regeneration" below).

## Terrain implementation

Unity's built-in `Terrain` (`UnityEngine.Terrain.CreateTerrainGameObject`),
not a hand-rolled mesh. Reasoning:

- **Collision is free.** `CreateTerrainGameObject` attaches a
  `TerrainCollider` automatically — no custom mesh-collision code to write
  or get wrong.
- **Performance is handled by Unity itself** — its own LOD/culling for
  terrain rendering, without this project needing to implement any.
- **The heightmap API is a well-understood, low-risk path** compared to
  hand-authoring a deformable mesh + matching `MeshCollider`.

Trade-off accepted: a Unity `Terrain` is a single rectangular tile with a
regular grid heightmap — it cannot represent overhangs, caves, or
disconnected landmasses the way an arbitrary mesh could. Acceptable for
this phase's prototype scope; revisit if a future prompt genuinely needs
that (e.g. a true cave system).

**Resolution**: 129×129 (a valid `2^n+1` heightmap size), kept deliberately
small for fast generation. Raising it later is a one-constant change in
`TerrainGenerator`, not a structural one.

**Shape**: deterministic fractal Perlin noise (4 octaves), with distinct
height profiles for `mountain`/`canyon`/`valley`/`island`/`flat`;
`hills`, `desert`, `forest`, and anything unrecognized fall back to a
gentle default (biome/vegetation *density* for desert/forest is
`EnvironmentGenerator`'s job — a terrain "shape" for a desert isn't
meaningfully different from hills, only its object placement is).

**Determinism**: `WorldSeedManager` (see below) hands `TerrainGenerator` a
`System.Random` seeded per-stage — never `UnityEngine.Random`'s global,
shared, mutable state. The noise *offset* (where in noise-space to sample)
is drawn from that RNG; the noise *function itself*
(`Mathf.PerlinNoise`) is a deterministic pure function of its inputs, so
the same offset always produces the same heightmap.

## Environment generation

`EnvironmentGenerator` resolves each `ObjectSpecification.Category` to one
of the five required subgroups (simple `Contains` matching — "tree" → Trees,
"rock"/"cliff"/"boulder" → Rocks, "building"/"cabin"/"house"/"ruin"/"tower"
→ Buildings, "bush"/"shrub"/"vegetation"/"grass" → Vegetation, everything
else → Structures), resolves a placement count (`Count` directly, or
derived from `Density01 × terrain area` when no explicit count is given),
then places that many instances terrain-snapped within bounds, honoring a
placement hint where present:

- `"dense_cluster"` → picks from a handful of seeded cluster centers with
  jitter, rather than uniform-random, so it actually looks clustered.
- `"along_cliffs"`/`"ridge_line"` → biases toward the highest of several
  sampled candidate points (a crude proxy for "cliff-like" terrain).
- `"riverbank"`/`"lowland"` → biases toward the lowest of several sampled
  candidates.
- Anything else (including empty) → uniform random within terrain bounds.

**Fallback geometry** (`PrimitiveWorldPrefabRegistry`): no external asset
packs are assumed. Every category resolves to a small primitive —
tree = cylinder trunk + sphere foliage, rock = scaled sphere, building =
cube body + cube roof cap, bridge = deck + pillar cylinders, tunnel =
four-panel hollow passage (floor/ceiling/two walls, real collidable
geometry a drone flies *through*, not a solid block), waterfall/river/lake
= a thin decorative plane with its collider removed. An unrecognized
category still gets a labeled generic cube marker — never a silent no-op.
Real assets can be layered in later by wrapping `IWorldPrefabRegistry`
(check a real prefab dictionary first, fall back to this registry) without
touching `EnvironmentGenerator`/`ObstacleGenerator` at all.

## Obstacles and course generation

`ObstacleGenerator` places every `ObstacleSpecification` from the
specification **exactly at its given position** — explicit placement is
never overridden. The same registry above resolves obstacle `Type` to a
shape: gate = open rectangular frame (real colliders on the frame pieces
only, the opening stays clear — miss it and you hit the frame, exactly
like a real FPV gate), ring = a 12-segment polygon approximation (Unity has
no built-in torus), wall = a solid blocking slab, pole = a thin cylinder,
landing pad = a short solid platform (solid on purpose — the drone needs
to physically rest on it), start/finish line and checkpoint markers =
thin/collider-free markers (see "Collision" below for why checkpoints are
allowed to be trigger-only).

**Course intent actually changes the result.** If
`CourseSpecification.GateCount` calls for more gates than were explicitly
specified, the *difference* is auto-generated along a deterministic
procedural path, shaped by `Course.Style`:

- A style containing `"technical"` (and not also a speed keyword) uses
  tight spacing (20-35m), sharp turns (±50°), low clearance (3-7m) for
  every auto-generated gate.
- A style containing `"high_speed"`/`"speed"` (without `"technical"`) uses
  long spacing (55-90m), gentle turns (±15°), higher clearance (8-18m).
- A style containing **both** (e.g. `"technical_then_high_speed"`)
  transitions from the tight parameters to the open ones at the sequence's
  midpoint — directly modeling "make the first section technical and
  tight, then transition into a high-speed valley section."
- Neither keyword → a moderate freestyle default (the high-speed
  parameters, as the least constraining default).

This never touches an already-placed explicit obstacle — it only fills the
gap between what was specified and `Course.GateCount`.

## Checkpoints

Deliberately split, per this phase's explicit instruction:

- **`ObstacleGenerator`** builds visuals/colliders and, for every obstacle
  with a `CheckpointIndex`, adds a small separate trigger-only `BoxCollider`
  + `CheckpointTrigger` component in its opening — visual construction
  only, no race-state knowledge.
- **`Sim.Gameplay.CheckpointManager`** (plain C# class, not a
  MonoBehaviour) is constructed *after* generation, given the obstacle
  root; it discovers every `CheckpointTrigger` via
  `GetComponentsInChildren` and wires itself in. It owns
  `TotalCheckpoints`/`CurrentCheckpointIndex`/`CompletedCheckpoints`/
  `RaceState`, enforces passing checkpoints in order (an out-of-sequence
  trigger is ignored — standard FPV racing convention), and exposes
  `CheckpointPassed`/`RaceFinished` events.

## Spawn resolution

`SpawnResolver` is a check `WorldSpecificationValidator` cannot do — the
validator only ever sees numeric field values, never a built scene. It
verifies, against the **actually generated** terrain and colliders: not
NaN/Infinite, within terrain bounds, above ground with clearance, and not
overlapping any obstacle or environment object's collider
(`Physics.OverlapSphere`, explicitly excluding the terrain's own collider
so proximity to the ground itself never counts as a collision). It tries
the specified `Position`, then each `AlternateSpawnPoints` entry in order.

**If none are safe, generation fails cleanly with a structured error — it
does not silently relocate the drone to an arbitrary fallback position.**
This is a deliberate, explicit instruction for this phase, and it
overrides what `docs/ARCHITECTURE.md` originally sketched in Phase 2
("falls back to a known-safe default... rather than failing the whole
world") — noted here as the authoritative current behavior. `WorldGenerator`
tears down the partial world it had already built before returning that
failure, so a failed generation never leaves stale geometry behind.

## Lighting

`LightingGenerator` configures one directional light (the sun) from
`LightingSpecification.TimeOfDayHours` (mapped to an elevation angle via a
single sine curve — a visual approximation, not astronomically accurate)
and `SunIntensity`, plus `RenderSettings.ambientLight` from
`AmbientColor`. Unity's built-in Standard pipeline lighting only —
deliberately not URP/HDRP, matching the project's unmodified render
pipeline configuration.

## Weather

`WeatherGenerator` configures `RenderSettings.fog`/`fogDensity` from
`WeatherSpecification` (boosted for `"fog"`/`"rain"`/`"cloud"` types even
if `FogDensity01` wasn't explicitly set), a simple downward-falling
particle system for `"rain"`, and a `WindZone` when `WindStrength01 > 0`.
Deliberately simple — not a claim of AAA weather fidelity, per this
phase's explicit scope.

## Collision

Every solid primitive `PrimitiveWorldPrefabRegistry` builds via
`GameObject.CreatePrimitive` carries Unity's default collider for that
shape automatically (`BoxCollider` on cubes, `SphereCollider` on spheres,
`CapsuleCollider` on cylinders) — nothing extra was needed for trees,
rocks, buildings, gates, walls, or poles to physically block the drone.
Terrain collision comes from `TerrainCollider`, attached automatically by
`CreateTerrainGameObject`. The only deliberate collider removals are
decorative water features (never meant to block flight) and pure
"checkpoint" marker obstacles (sensors, not physical obstacles — a gate
that *also* happens to be a checkpoint keeps its normal blocking frame
colliders; only the *separate* trigger volume added for checkpoint sensing
is non-solid). This satisfies "do not use trigger-only geometry for
objects that should physically block the drone" precisely: nothing that's
meant to block ever loses its solid collider.

## Determinism

`WorldSeedManager` derives an independent `System.Random` per named stage
("terrain", "environment", "course_gates", ...) from one master seed
(`WorldSpecification.Seed`), via `Sim.Utilities.StableHash` (FNV-1a — not
`string.GetHashCode()`, which .NET does not guarantee stable across
processes). Every generator takes its `Random` from here; none touch
`UnityEngine.Random`'s global state. The same specification + seed
reproduces the same terrain heightmap, the same environment placement, and
the same auto-generated gate layout — verified directly in
`WorldGeneratorTests`/`WorldSeedManagerTests`.

## Regeneration

`WorldGenerator` tracks the single `GeneratedWorld` root it last built.
Calling `Generate()` again always calls `Clear()` first — the previous
world (every child object, every `CheckpointTrigger`, everything) is
destroyed before the new one starts building, so there is never a stale
GameObject, a leftover trigger, or two `GeneratedWorld` roots coexisting.
`Clear()` also runs on any failure path, so a failed regeneration attempt
never leaves a half-built world sitting in the scene either.
`CheckpointManager` is a fresh plain-C#-object instance per successful
generation (owned by the caller via `GeneratedWorldResult`, not cached
inside `WorldGenerator`) — no stale race state survives a regeneration
either, since nothing holds onto the old instance once its `GeneratedWorld`
is destroyed. `UnityLifecycleUtility.DestroySafely` picks
`Object.Destroy`/`Object.DestroyImmediate` correctly depending on whether
this runs at Play time or Edit time (a raw `Destroy` call errors outside
Play mode; `DestroyImmediate` is discouraged during normal Play-mode
gameplay) — this generator code is written to work correctly from either
context.

## Performance

Respects `WorldGenerationLimits` (Phase 6) — `EnvironmentGenerator`
defensively re-clamps object counts to `MaxObjectCountPerCategory` even
though `WorldGenerator`'s contract already requires pre-validated input
(cheap insurance against exactly the "unbounded object count" mistake the
project's performance rules call out). No `FindObjectOfType`/
`GameObject.Find` anywhere in the generation hot path (only Editor tooling,
which runs once per click, uses those — see its own file's remarks on why
that's fine). No LINQ inside placement loops. Terrain heightmap resolution
kept small specifically to keep generation fast. Not attempted this
phase: object pooling, LOD, or any DOTS/ECS — explicitly out of scope
("do not prematurely implement ECS/DOTS").

## Editor tooling

`Assets/Scripts/Editor/WorldGenerationTestTool.cs` (new file, separate
from `DroneRigBuilder.cs` to keep that file focused on drone/camera/OSD —
reuses its camera/OSD/ground-and-light builders, made `internal` for this,
rather than duplicating them):

- **`FPV Sim/World/Generate Test World (Mock Designer)`** — runs the exact
  Himalayan-mountain-course prompt from this phase's brief through
  `MockWorldDesigner` → `WorldSpecificationValidator` → `WorldGenerator`,
  places (or reuses) the existing drone rig at the resolved spawn via
  `DroneController.SetSpawn`/`ResetToSpawn`, and selects it. Requires
  no OpenWorld Reactor, OpenAI, Anthropic, network access, or credentials.
- **`FPV Sim/World/Clear Generated World`** — manual regeneration-safety check.

## Testing

Real, runnable EditMode tests (`WorldGeneratorTests.cs`,
`WorldSeedManagerTests.cs`) — Unity's Editor process has a live
GameObject/Transform/Physics/Terrain system outside Play mode, so
generation, hierarchy structure, terrain dimensions, explicit-position
preservation, checkpoint ordering, determinism, regeneration cleanup, and
spawn-failure handling are all covered by tests that actually construct
real Unity objects and assert on them — not placeholders. **Not covered,
and not possible to cover without a live Editor's Play mode**: anything
requiring the Player loop to actually tick over time — FixedUpdate-driven
physics response, the drone actually colliding with generated geometry
while flying, checkpoint triggers firing from real drone movement. Those
need manual Play-mode verification (see the checklist below); none of this
phase's code has been run in a live Editor.

## Manual Unity verification checklist

1. Open the project in Unity 2022.3 LTS.
2. **FPV Sim → World → Generate Test World (Mock Designer)**.
3. Check the Console — should show `[WorldGeneration]`/`[WorldGenerator]`
   log lines ending in "Generated 'Mock Example World' — 15 checkpoints,
   spawn at ...", no errors or NullReferenceExceptions.
4. In the Hierarchy, confirm `GeneratedWorld` exists with the full
   `Terrain`/`Environment/{Trees,Rocks,...}`/`Obstacles/{Gates,...}`/
   `Sun`/`Weather`/`Spawn` structure, and that the terrain visibly has
   height variation (mountain-like).
5. Enter Play Mode. Arm (Backspace), fly (WASD/throttle) toward the
   generated terrain/trees/gates — confirm the drone actually collides
   with them (bounces off/stops) rather than passing through.
6. Fly through a gate's opening — confirm no collision there (the opening
   is genuinely open, only the frame blocks).
7. Fly through gates in order — watch for `[WorldGeneration]`-style
   confirmation the checkpoint count increments (currently observable via
   a breakpoint/log, since no HUD checkpoint display exists yet — Phase
   9's UI work).
8. **FPV Sim → World → Generate Test World** again — confirm the old
   `GeneratedWorld` is gone (not duplicated) and a fresh one appears, drone
   relocated to the new spawn.
9. **FPV Sim → World → Clear Generated World** — confirm the entire
   `GeneratedWorld` hierarchy disappears cleanly.

## Limitations

- Terrain is a single Unity `Terrain` tile — no overhangs/caves/
  disconnected landmasses.
- No LOD/culling/object pooling for generated environment objects yet.
- Ring/tunnel geometry is a primitive approximation (polygon segments /
  four flat panels), not a smooth curved mesh.
- Explicit obstacle Y coordinates are respected literally and are **not**
  auto-snapped to the generated terrain height — a specification whose
  author didn't know the terrain shape in advance can produce a gate
  floating above or partially embedded in terrain. A future refinement
  could offer a "terrain-relative height" mode; not built this phase, so
  as not to silently override an explicit position (which this phase was
  told never to do).
- No orchestration wiring yet between `WorldGenerationController`
  (design+validation) and `WorldGenerator` (Unity construction) — the
  Editor tool composes them manually; a future runtime UI (Phase 9) will
  need its own composition point.
