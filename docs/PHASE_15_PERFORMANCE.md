# Phase 15 — Performance Optimization

## 0. What was inspected first

Per this phase's explicit instruction, the full runtime/generation path
was read before changing anything: `DroneController`, `DronePhysics`,
`DroneFlightModel`, `DroneInput`, `FlightTelemetry`, `FlightModeController`,
`FPVCameraController`, `CameraSmoothing`, `CourseGameplayController`,
`CheckpointManager`, `CheckpointTrigger`, `RaceTimer`,
`DroneRecoveryController`, `CourseResultsController`, `TerrainGenerator`,
`EnvironmentGenerator`, `ObstacleGenerator`, `LightingGenerator`,
`WeatherGenerator`, `SpawnResolver`, `WorldSeedManager`,
`PrimitiveWorldPrefabRegistry`, `WorldGenerationController`,
`WorldGenerationRuntimeService`, `RuntimeSimulationBootstrap`,
`WorldSaveService`/`WorldSaveJsonSerializer`, `WorldGenerationUI`,
`CourseHUD`, `CourseResultsUI`, `TelemetryUI`/`FPVHUD`, every existing
test, and `docs/IMPLEMENTATION_PLAN.md`/`docs/ARCHITECTURE.md`/every
prior phase doc.

## 1. Baseline

**No Unity Editor/Profiler is available in this environment.** Every
finding below comes from static reading of the actual C# source — line
counts, loop bounds, allocation sites, call frequency inferred from where
a method is invoked (FixedUpdate vs. Update vs. once-per-generation vs.
once-per-Save/Load). No FPS, frame time, CPU/GPU sample, memory figure,
or GC count is reported anywhere in this document or in code comments,
because none was actually measured. Where a number appears (e.g. "129×129
heightmap", "up to 1,280,000 GameObjects"), it is a count derived directly
from the code/constants, not a timing measurement.

Work was classified as the brief's categories A-I before touching
anything:

| Category | Findings |
|---|---|
| A. Runtime per-frame work | `FPVCameraController.LateUpdate`, `FPVHUD.Update`/`TelemetryUI`, `CourseHUD.Update`, `CourseGameplayController.Tick`/`DroneRecoveryController.Tick` (called from `RuntimeSimulationBootstrap.Update`) |
| B. Runtime physics work | `DroneController.FixedUpdate` → `DronePhysics`/`DroneFlightModel` |
| C. World-generation work | `WorldGenerator` → `TerrainGenerator`/`EnvironmentGenerator`/`ObstacleGenerator`/`LightingGenerator`/`WeatherGenerator`/`SpawnResolver` — all one-shot, per-generation, not per-frame |
| D/E. Memory / GC | String interpolation in `TelemetryUI`/`CourseHUD` (per-frame call sites); `TerrainGenerator`'s per-pixel loop; `EnvironmentGenerator`'s per-object loop (both generation-time, not per-frame) |
| F. Rendering/object count | Primitive `GameObject`s from `PrimitiveWorldPrefabRegistry`, bounded by `WorldGenerationLimits` (see §5) |
| G. File I/O | `WorldSaveService` — one read or one write per explicit Save/Load click, never per-frame |
| H. LLM/network | `AnthropicLLMClient`/`LLMWorldDesigner` — only reached from `GenerateWorldAsync`'s Designing step, never from `LoadWorld`, never from any per-frame path (verified, see §15 below) |
| I. Editor-only tooling | `WorldGenerationTestTool`/`DroneRigBuilder` — Editor-time only, never shipped in a Player build's hot path |

**The central distinction this phase kept in mind throughout**: world
generation (category C) runs once per Generate/Load click, never per
frame — it is not held to the same bar as category A/B. Nothing in §5/§6
below was done "because it's slow," only "because the same
input/output guarantee should cost less work to produce."

## 2. Hotspots found

Real, concrete items — not theoretical:

1. **`TerrainGenerator.FractalNoise`** re-derived a constant
   normalization value (`sum of persistence^i` for a fixed
   octaves/persistence/lacunarity) from scratch on every one of the
   129×129 = 16,641 heightmap pixels a single terrain generation
   samples — the same four floating-point additions, producing the
   same result, every time. Generation-time only (not per-frame), so
   this is a small win in absolute terms, but it is a genuinely
   redundant calculation with a trivial, zero-risk fix — exactly what
   this phase's §6 asked to look for.
2. **`TelemetryUI.UpdateTelemetry`** reformats and reassigns all ten
   telemetry text fields on *every* `FixedUpdate` (50 Hz by default),
   including `Mode`/`Armed`, which only ever change on an explicit
   discrete action (cycle-mode/arm/disarm) — meaning in the
   overwhelming majority of physics steps, two of those ten fields are
   reformatted and reassigned for a value that is provably identical to
   what's already displayed.
3. **`FPVHUD`/`TelemetryUI.UpdateFps`** reformats and reassigns the FPS
   text on *every rendered frame*, even though the smoothed value
   rounds to the same displayed integer across many consecutive frames.
4. **`CourseHUD.Update`** reformats and reassigns the race-timer text on
   *every rendered frame* — including indefinitely after the race
   reaches `Finished`, at which point `CourseGameplayController.
   ElapsedSeconds` is frozen (Phase 11/13 design) but the HUD keeps
   recomputing and reassigning an identical string for as long as a
   player leaves the results screen open.
5. **`SpawnSpecification.AlternateSpawnPoints`** had no length limit
   anywhere. `SpawnResolver.Resolve` performs one real
   `Physics.OverlapSphere` query per entry it tries (specified position,
   then each alternate, in order) — an untrusted, unusually long list
   (LLM output, or a hand-edited/corrupted Phase 14 save file) could
   drive an unbounded number of physics queries during one generation.
6. **`WorldGenerationLimits.MaxObjectCountPerCategory` (20000) ×
   `MaxEnvironmentObjectCategories` (64)** bound each dimension
   individually but not their product — up to 1,280,000 primitive
   `GameObject`s could be requested by a specification that passes
   every existing check. A real combinatorial gap, not a theoretical one.

Nothing else inspected rose to the level of a genuine hotspot — see §4/§9
below for what was checked and found already correct.

## 3. Changes made

Each change, what was wrong, why, what changed, and how it was validated:

### 3.1 `TerrainGenerator.FractalNoise` — hoisted normalization constant

- **What was wrong**: the normalization divisor (`sum_{i=0}^{3}
  0.5^i = 1.875`, for this file's fixed 4 octaves/0.5 persistence) was
  recomputed via a 4-iteration loop inside `FractalNoise`, called once
  per heightmap pixel (16,641 times per terrain generation).
- **Why it was wrong**: the value is invariant — it does not depend on
  `x`/`z`/the noise sample at all, only on the (always-identical)
  octaves/persistence/lacunarity — so recomputing it per-pixel is pure
  waste.
- **What changed**: `FractalNoiseOctaves`/`FractalNoisePersistence`/
  `FractalNoiseLacunarity` are now named `const`s (previously inline
  literals at the one call site); `FractalNoiseNormalization` is a
  `static readonly` field computed once (`ComputeFractalNoiseNormalization()`,
  a field initializer — runs once, at class load, not once per pixel).
  `FractalNoise` now divides by that precomputed field instead of
  re-accumulating it.
- **How it was validated**: the output is mathematically identical (same
  formula, same inputs, just computed once instead of redundantly) —
  the existing `WorldGeneratorTests.
  Generate_SameSeed_ProducesSameTerrainHeightAtSamePoint` test (same
  specification+seed → same sampled terrain height) was re-read and
  requires no change; it continues to exercise exactly the guarantee
  this refactor must not break.

### 3.2 `TelemetryUI` — dirty-check Mode/Armed/FPS before reformatting

- **What was wrong**: `UpdateTelemetry` (called every `FixedUpdate`, 50
  Hz) always called `TelemetryFormatter.FormatMode`/`FormatArmed`
  (string interpolation) and assigned the result, and `UpdateFps`
  (called every rendered frame) always called `FormatFps` and assigned
  it — regardless of whether the underlying value had changed since the
  last update.
- **Why it was wrong**: `Mode`/`Armed` only change on an explicit
  discrete action (Tab to cycle mode, Backspace to arm/disarm) — for
  every other physics step, the reform► that ran was for a value
  already on screen. FPS, once exponentially smoothed
  (`FPVHUD.Update`), frequently rounds to the same integer across many
  consecutive rendered frames.
- **What changed**: `TelemetryUI` now stores `_lastDisplayedMode`
  (`FlightMode?`), `_lastDisplayedArmed` (`bool?`), and
  `_lastDisplayedFps` (`int?`) — each nullable so the very first update
  always paints. `UpdateTelemetry`/`UpdateFps` compare the incoming
  value against the stored one first, only reformatting+reassigning
  when it actually differs. The continuously-varying fields
  (altitude/speed/vertical-speed/throttle/pitch/roll/yaw/angular-speed)
  are deliberately **not** dirty-checked — see §9 for why.
- **How it was validated**: by inspection — the displayed text is
  identical to before in every case (same formatter, same values, just
  computed lazily); no behavioral test exists for this MonoBehaviour
  (see §10 for why), so this is flagged for manual confirmation (§11
  item 4).

### 3.3 `CourseHUD` — dirty-check the race timer before reformatting

- **What was wrong**: `Update` (every rendered frame) always called
  `CourseStatusFormatter.FormatTimer(_controller.ElapsedSeconds)` and
  assigned the result to `_timerText`, including for as long as the
  course stays `Finished` (where `ElapsedSeconds` is frozen).
- **Why it was wrong**: an idle results screen left open for minutes
  would reformat and reassign an identical timer string every single
  rendered frame indefinitely, for no reason.
- **What changed**: `CourseHUD` stores `_lastDisplayedElapsedSeconds`
  (`float`, initialized to `NaN` so the first frame always paints) and
  only reformats/reassigns when `ElapsedSeconds` actually differs from
  the last displayed value (exact equality — deliberately not an
  epsilon compare, since the goal is only to catch the exactly-frozen
  case; `RaceTimer` never produces the same non-frozen value twice in a
  row while actually running, so this can never suppress a real update
  during `Racing`).
- **What was deliberately left alone**: the checkpoint/countdown text
  (`_checkpointText`) — it serves two purposes (countdown digits during
  `Countdown`, gate progress otherwise) and changes meaningfully often
  during the period this HUD matters most (`Racing`); dirty-checking it
  cleanly would need per-purpose state tracking for a smaller, less
  clear-cut benefit than the timer fix. Not changed, to avoid adding
  complexity not clearly justified by a real gain — see the brief's own
  "do not make code unreadable for tiny theoretical gains."
- **How it was validated**: by inspection, same reasoning as §3.2;
  flagged for manual confirmation (§11 item 5).

### 3.4 New limit: `WorldGenerationLimits.MaxAlternateSpawnPoints`

- **What was wrong**: `SpawnSpecification.AlternateSpawnPoints` had no
  length bound anywhere in the existing validation.
- **Why it was wrong**: `SpawnResolver.Resolve` tries the specified
  position, then every alternate, in list order, and each attempt
  performs one real `Physics.OverlapSphere` query
  (`OverlapsAnythingOtherThanTerrain`) — an unusually long list from an
  untrusted source (LLM output, or a save file — save files are
  explicitly untrusted input per Phase 14) could drive an unbounded
  number of physics queries during one generation.
- **What changed**: `WorldGenerationLimits.MaxAlternateSpawnPoints = 32`
  (new constant, documented); `WorldSpecificationValidator.ValidateSpawn`
  trims the list to this length using the exact same `RemoveRange` +
  `Warning` repair pattern already used for `EnvironmentObjects`/
  `Obstacles` in the same class — no new validation *pattern*, just one
  more field covered by the existing one.
- **How it was validated**: `WorldSpecificationValidatorTests.
  Validate_TooManyAlternateSpawnPoints_TrimsToLimit`/
  `Validate_AlternateSpawnPointsWithinLimit_NotTrimmed` (written, not
  executed — see §10).

### 3.5 New limit: `WorldGenerationLimits.MaxTotalEnvironmentObjectCount`

- **What was wrong**: `MaxObjectCountPerCategory` (20000, existing) and
  `MaxEnvironmentObjectCategories` (64, existing) each bound one
  dimension of `WorldSpecification.EnvironmentObjects`, but nothing
  bounded their product.
- **Why it was wrong**: a specification with 64 categories at 20000
  objects each — individually valid against every existing check —
  would ask for 1,280,000 primitive `GameObject`s, a combinatorial case
  neither existing limit alone prevents.
- **What changed**: `WorldGenerationLimits.MaxTotalEnvironmentObjectCount
  = 10000` (new constant, documented — chosen well below the
  20000×64 product, still far more generous than any real prompt asks
  for, and deliberately not equal to `MaxObjectCountPerCategory` itself
  so the two limits stay meaningfully different concepts).
  `EnvironmentGenerator.Generate` now tracks a running total across its
  `foreach` loop over categories and clamps each category's actually-
  resolved count (`ResolveCount`'s result — covers *both* an explicit
  `Count` and a `Density01`-derived one uniformly) down to whatever
  budget remains, stopping entirely once the budget is exhausted. This
  lives in the generator, not the validator, because the validator runs
  before generation and cannot see a `Density01`-derived count — only
  `EnvironmentGenerator` sees the fully-resolved number for either path.
- **How it was validated**: new `EnvironmentGeneratorTests` (real
  EditMode tests over a real generated `UnityEngine.Terrain`) — within-
  limit requests are respected exactly; a combinatorial over-limit
  request is clamped to precisely the configured total, with earlier
  categories in list order receiving their full request and a later one
  receiving only the remaining budget; same specification+seed
  reproduces the same total object count (written, not executed — see
  §10).

## 4. Determinism

No change in this phase alters what a given `WorldSpecification` + seed
produces:

- §3.1 (`TerrainGenerator`) computes the exact same value, just once
  instead of redundantly — bit-for-bit identical output.
- §3.4/§3.5 (the two new limits) are themselves deterministic clamps:
  the same input list/count is trimmed/capped to the same result every
  time, with no randomness involved in *which* entries survive (always
  a prefix, in the existing list/iteration order — the same rule
  `EnvironmentObjects`/`Obstacles` trimming already used before this
  phase).
- Nothing in this phase touches `WorldSeedManager`, any generator's
  `System.Random` usage, or `UnityEngine.Random`'s global state. No
  second randomization mechanism was introduced anywhere.
- §3.2/§3.3 (UI dirty-checks) affect *when* a UI text field is
  reassigned, never *what* value it is eventually assigned — the
  displayed text at any given moment is unchanged from before this
  phase.

Verified via the existing `WorldGeneratorTests`/`WorldSeedManagerTests`
(same seed → same terrain/spawn; different seed → different terrain —
both pre-existing, unmodified by this phase) plus the new
`EnvironmentGeneratorTests.Generate_DeterministicCount_
SameSpecificationAndSeed_SameTotal`.

## 5. Runtime (per-frame / per-fixed-frame)

- **`DroneController.FixedUpdate`** (50 Hz default): samples input,
  computes flight output, applies physics, publishes telemetry. No
  allocations beyond the `FlightTelemetry`/`DroneInputSample`/
  `FlightOutput`/`DroneAttitudeState` value-type structs (stack, not
  heap); no LINQ; no per-frame `GetComponent`; no per-frame logging.
  Unchanged by this phase.
- **`FPVCameraController.LateUpdate`**: reads the drone's `CameraMount`
  transform, applies optional smoothing/shake (Perlin-based, not
  `Random.value`, so no per-frame allocation there either). Unchanged.
- **`TelemetryUI.UpdateTelemetry`/`UpdateFps`** (via `FPVHUD`, every
  `FixedUpdate`/rendered frame respectively): now skips reformatting/
  reassigning Mode/Armed/FPS when unchanged (§3.2); the continuously-
  varying fields still update unconditionally, unchanged.
- **`CourseGameplayController.Tick()`/`DroneRecoveryController.Tick()`**
  (via `RuntimeSimulationBootstrap.Update`, every rendered frame): pure
  comparisons against cached state (countdown/cooldown/pending timers,
  bounds, config) — zero allocations, no search, no polling introduced.
  Confirmed unchanged and already correct.
- **`CourseHUD.Update`** (every rendered frame): now skips reformatting/
  reassigning the race timer when unchanged (§3.3); checkpoint/countdown
  text still updates unconditionally, unchanged.

## 6. Generation (world-generation-time only, never per-frame)

- **`TerrainGenerator`**: one 129×129 heightmap pass per generation (not
  reduced — resolution is unchanged, per this phase's explicit
  instruction not to alter terrain characteristics). §3.1's fix removes
  one redundant per-pixel calculation; the fractal-noise sampling itself
  (4 `Mathf.PerlinNoise` calls per pixel) is unchanged.
- **`EnvironmentGenerator`**: one `GameObject.CreatePrimitive`-backed
  instance per placed object, now bounded in total by
  `MaxTotalEnvironmentObjectCount` in addition to the existing
  per-category bound (§3.5).
- **`ObstacleGenerator`/`LightingGenerator`/`WeatherGenerator`**:
  inspected, already bounded/small (a handful of `GameObject`s each, or
  a loop already capped by `MaxObstacleCount`) — no changes.
- **`SpawnResolver`**: now tries at most `1 +
  MaxAlternateSpawnPoints` (33) positions, each one real
  `Physics.OverlapSphere` call, instead of an unbounded number (§3.4).

## 7. Memory / GC considerations

- The string-interpolation allocations in `TelemetryUI`/`CourseHUD`
  (§3.2/§3.3) are small, short-lived (Gen0) allocations either way — the
  fix is about *frequency* (skipping them when the value hasn't
  changed), not about eliminating allocation as a goal in itself. Per
  this phase's explicit "do not blindly eliminate all allocations"
  instruction, the continuously-varying telemetry/checkpoint fields
  were deliberately left reformatting unconditionally.
- Generation-time allocations (`heights` array, per-object `GameObject`/
  `Collider`/`Renderer` instances) are one-shot, not per-frame — the
  brief is explicit that generation is not held to per-frame allocation
  standards, and nothing here was changed merely to reduce a one-time
  allocation with no repeated cost.
- No LINQ was found in any per-frame code path (`DroneController`,
  `DronePhysics`, `FPVCameraController`, `CourseGameplayController.Tick`,
  `DroneRecoveryController.Tick`, `CheckpointManager`, `TelemetryUI`,
  `CourseHUD`) — confirmed by direct inspection, not introduced or
  removed by this phase.

## 8. Unity Profiler

**Not available in this environment.** No Unity Editor was installed, so
no Profiler session, no frame-time capture, no GC-allocation view, and no
Play Mode run of any kind was performed. Every claim in this document is
a static-analysis finding (loop bounds, call-site frequency, allocation
sites read directly from source) — never a measurement. §11 lists exactly
what a live Editor's Profiler should be used to confirm once one is
available.

## 9. Explicitly inspected and left unchanged (with reasoning)

- **`CheckpointTrigger.OnTriggerEnter`**'s
  `GetComponentInParent<DroneController>()` — runs once per actual
  physical trigger-enter *event*, not per frame; bounded by how often
  the drone (the only thing with a moving `Rigidbody` in this project)
  actually enters a trigger volume. Not a hot path despite superficially
  resembling one.
- **`WorldSaveService`/`WorldSaveJsonSerializer`**: one file read/write
  and one (de)serialization pass per explicit Save/Load click — never
  looped, never per-frame, never repeated for the same click. Left
  exactly as Phase 14 built it, per this phase's own explicit "save/load
  does NOT need to be optimized as if it were a per-frame operation"
  instruction.
- **`AnthropicLLMClient`/`LLMWorldDesigner`**: only ever reached from
  `WorldGenerationController.GenerateWorldAsync`'s Designing step.
  Confirmed (again, per Phase 14's own design) that `LoadWorld` never
  reaches it, no gameplay/recovery/results/save code path calls it, and
  no caching was added — per this phase's explicit "do not add caching
  unless there is a concrete architectural reason" instruction, none
  existed.
- **`ObstacleGenerator`'s auto-layout gate loop**: already bounded by
  `Mathf.Clamp(..., 0, WorldGenerationLimits.MaxObstacleCount)` before
  this phase; no combinatorial gap analogous to §3.5 exists here, since
  it's a single flat list, not category-count × per-category-count.
- **`DroneRigBuilder`/`WorldGenerationTestTool`**: Editor-only tooling,
  never part of a shipped Player's runtime path — not a performance
  target for this phase.

## 10. Testing

**Automated tests written — not executed.** No Unity Editor is available
in this environment (stated honestly, same as every prior phase). These
are real, checked-in EditMode tests a Unity Test Runner will execute:

- Extended `WorldSpecificationValidatorTests` —
  `Validate_TooManyAlternateSpawnPoints_TrimsToLimit`,
  `Validate_AlternateSpawnPointsWithinLimit_NotTrimmed`.
- New `EnvironmentGeneratorTests` (real generated `UnityEngine.Terrain`,
  not mocked) — requested counts respected within limits (single and
  multiple categories); a combinatorial over-total request is clamped to
  exactly `MaxTotalEnvironmentObjectCount`, with list-order allocation
  confirmed per group; same specification+seed reproduces the same total
  object count.

**No new test was written for the `TelemetryUI`/`CourseHUD` dirty-check
changes** (§3.2/§3.3) — both are `MonoBehaviour`s tightly coupled to
concrete `TextMeshProUGUI` fields with no interface seam, and building a
real `TextMeshProUGUI` reliably in EditMode without a live Editor to
verify the result against was judged more likely to produce a misleading
"passing" test than a meaningful one. Flagged explicitly as a manual
verification item (§11) instead of forcing a test into existence.

**No fabricated benchmark/timing assertions exist anywhere** — every
assertion in every test compares stable, deterministic generation outputs
(object counts, list lengths, terrain height at a point) never wall-clock
duration, per this phase's explicit instruction.

## 11. Manual verification checklist (Unity Editor required)

Nothing below has been run in a live Editor — none was available while
writing this phase. This is what to check by hand in Unity 2022.3 LTS:

1. Open the Unity Profiler, generate the Himalayan example world, and
   confirm generation completes with no errors/warnings in the Console
   related to this phase's changes.
2. Fly the drone (Angle/Acro/Horizon) and confirm flight *feel* is
   completely unchanged from before this phase (thrust, torque, rate
   control, deadzone/expo, arm/disarm all behave identically).
3. With the Profiler's CPU/GC view open, confirm the FPV HUD's Mode/
   Armed text only actually redraws when you cycle mode (Tab) or arm/
   disarm (Backspace) — not continuously — and that the FPS counter
   only updates its displayed number when the rounded value changes.
4. Confirm the Course HUD's timer visibly ticks during Racing exactly as
   before, and — after finishing a race — leave the results screen open
   for a while and confirm (via the Profiler) that nothing keeps
   reallocating a timer string every frame.
5. Confirm the Course HUD's gate/countdown text still updates correctly
   and promptly during Countdown and while passing checkpoints (this
   field was deliberately left unoptimized — confirm no regression).
6. Generate a world with a deliberately large `EnvironmentObjects` list
   (e.g. via a hand-edited prompt asking for an extreme number of
   objects across many categories) and confirm the scene does not exceed
   `WorldGenerationLimits.MaxTotalEnvironmentObjectCount` total
   environment objects, with a corresponding validation Warning visible
   in the Console.
7. Confirm a specification with many `AlternateSpawnPoints` (more than
   32) still generates successfully, using only the first 32.
8. Confirm terrain shape/height for a known prompt+seed looks the same
   as it did before this phase (spot-check against Phase 8/9's own
   manual checklists if still available).
9. Use the Profiler's Deep Profile / GC Alloc view during a normal flight
   session to confirm no unexpected per-frame allocation spikes remain
   in `Sim.Drone`/`Sim.Camera`/`Sim.Gameplay`/`Sim.UI` code.
10. Confirm Save/Load (Phase 14) still works correctly and is not called
    repeatedly or automatically — a single click still performs exactly
    one file operation.
