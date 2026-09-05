# Phase 12 — Crash / Fall Detection & Automatic Respawn

## 0. What was inspected first

Per this phase's explicit instruction, the full existing stack was read
before writing anything: `DroneController`, `DronePhysics`,
`DroneFlightModel`, `FlightTelemetry`, `FlightModeController`,
`WorldGenerationController`, `WorldGenerationRuntimeService`,
`CourseGameplayController`, `RaceTimer`, `IDroneSpawnTarget`,
`DroneControllerSpawnTarget`, `SpawnResolver`, `WorldGenerator`, the
generated-world hierarchy (`TerrainGenerationResult`/`ObstacleGenerator`),
`CourseHUD`, `CourseStatusFormatter`, `CheckpointManager`/
`CheckpointTrigger`, `RuntimeSimulationBootstrap`, and every Phase 9/11
EditMode test. Confirmed: `TerrainGenerationResult` already exposes
exactly the bounds query this phase needs (`Origin`/`Width`/`Depth`,
`IsWithinBounds`, `SampleHeight`) — reused directly, not recomputed.
`IDroneSpawnTarget`/`DroneControllerSpawnTarget`/`DroneController.SetSpawn`+
`ResetToSpawn` already do exactly what "reset the drone" needs — reused
directly, no second reset path.

## 1. Architecture

```
GeneratedWorldResult (SpawnPosition/Rotation, CheckpointManager, Bounds — new this phase)
        ↓ (on WorldGenerationController reaching Ready)
WorldGenerationRuntimeService                — extended, not replaced (Phase 9/11/12)
        ↓ BindToCourse(...)                   ↓ Bind(bounds, spawn)   (unbind on every other state)
CourseGameplayController (Sim.Gameplay)       DroneRecoveryController (Sim.Gameplay) — new this phase
        ↑ State (read-only)  ↓ SetCheckpointProcessingSuppressed
        └──────────────────────────────┘
                        ↓ PlaceAt(spawn)
                IDroneSpawnTarget → DroneControllerSpawnTarget → DroneController
```

`DroneRecoveryController` is a plain C# class — same pattern as
`CourseGameplayController`/`Sim.Core.WorldGenerationController` —
constructed once by `RuntimeSimulationBootstrap` and re-bound to a new
world's bounds/spawn every regeneration, never recreated. That single-
instance policy is what guarantees no duplicate recovery managers ever
accumulate (same guarantee Phase 11 already established for course
gameplay).

It reads `CourseGameplayController.State` (to gate margin-based recovery
to Racing — see §3) and calls its one new passthrough,
`SetCheckpointProcessingSuppressed` (see §9), but never writes anything
else about race/checkpoint state, and never touches
`DronePhysics`/`DroneFlightModel`/`FlightModeController` at all.

## 2. Detection strategy

Two, and only two, conditions count as "unrecoverable":

1. **The drone's position is not finite** (`NaN`/`Infinity` on any axis)
   — a raw safety net against physics/numerical corruption, checked every
   Tick before anything else, and recovered from **immediately**, with no
   confirmation delay and regardless of course/race state.
2. **The drone's position is outside the generated world's actual
   playable bounds by more than a configurable margin** — either
   horizontally, or fallen below the sampled ground height by more than a
   configurable margin — confirmed over a short debounce window, and only
   acted on while the course is actually `Racing` (see §3).

**Explicitly not used, per this phase's explicit instruction**:
orientation (pitch/roll, "is the drone upside down"), angular velocity,
or linear velocity/speed. `DroneAttitudeState`/`FlightTelemetry` are never
read by `DroneRecoveryController` at all — it depends only on
`IDroneStateSource.Position` (world-space position) plus
`CourseGameplayController.State`, `WorldRuntimeBounds`, and
`DroneRecoveryConfig`.

## 3. Why orientation/velocity are not crash signals

Acro mode is open-loop rate control that permits arbitrarily fast,
sustained rotation on any axis by design — a 720°/s flip is not a crash,
it's the mode working correctly. Horizon mode still permits aggressive
maneuvers within its self-leveling range. The drone can legitimately fly
fully inverted in Acro (and briefly in Horizon) with no loss of control.
A momentarily very high linear speed is ordinary racing behavior, not
loss of control. A momentarily low altitude is ordinary "diving through a
low gate" behavior, not a crash. Any threshold on these
(`if (rotation > X)`, `if (velocity > X)`, `if (isUpsideDown)`) would
misfire on entirely legitimate flight — this was the brief's explicit,
central warning, and it drove every other design decision in this phase.
Position-relative-to-world-bounds has no such ambiguity: there is no
legitimate FPV maneuver that requires being 200 meters outside the
generated terrain's footprint or 50 meters below its surface.

## 4. World bounds

`Sim.WorldGeneration.WorldRuntimeBounds` (new this phase) — a narrow,
read-only wrapper over the same `TerrainGenerationResult` `WorldGenerator`
already builds for every generation. It does **not** duplicate any
terrain math: `IsWithinHorizontalBounds`/`SampleGroundHeight` delegate
straight to `TerrainGenerationResult.IsWithinBounds`/`SampleHeight` — the
exact same calls `SpawnResolver`/`ObstacleGenerator` already rely on for
spawn safety and obstacle placement. `WorldGenerator.Generate()`
constructs one alongside the `CheckpointManager` it already built, and
`GeneratedWorldResult.Bounds` carries it out — a fresh instance per
successful generation, never cached across regenerations, exactly
matching how `CheckpointManager` already works (see
docs/WORLD_GENERATION.md "Regeneration").

`DroneRecoveryController.IsOutOfBounds` applies `DroneRecoveryConfig.
RecoveryMargin` on top of the terrain's actual footprint
(`Origin.x/z` ± `Width`/`Depth` ± margin) — the recovery boundary is
deliberately *larger* than the visual terrain edge, per this phase's
explicit "do not make the boundary exactly coincide with the visual edge"
instruction, so a drone flying near the edge is never surprised by a
recovery it didn't expect.

## 5. Below-world detection

Never `if (position.y < 0)` — generated terrain is centered on the world
origin with `Origin.y` typically 0 but height varying per-point above
that, and terrain type/seed change where "the ground" actually is at any
given X/Z. Instead: `WorldRuntimeBounds.SampleGroundHeight(x, z)` (the
same `Terrain.SampleHeight` call `SpawnResolver` uses) is sampled at the
drone's *current* X/Z, and the drone counts as below-world once its Y
falls more than `DroneRecoveryConfig.BelowWorldMargin` under that sampled
height. This is only sampled when the drone is already within the
horizontal bounds+margin check (see `IsOutOfBounds`'s ordering) — a
horizontal violation is detected and returned first, so `Terrain.
SampleHeight` (not guaranteed meaningful far outside the terrain's own
footprint) is never called with a wildly out-of-range point.

**No terrain search happens per frame.** The `TerrainGenerationResult`
underlying `WorldRuntimeBounds` is bound once, at `Bind()` time (called
once per successful generation, from `WorldGenerationRuntimeService`) —
`Tick()` only ever calls the already-cached `_bounds`'s methods.

## 6. Why no maximum altitude

Per this phase's explicit instruction: an arbitrary max altitude must not
be imposed unless the world/course actually defines one, and neither
`WorldSpecification` nor `CourseSpecification` currently do — adding a
new field to either for this purpose alone would be scope creep unrelated
to "add a recovery system." `TerrainGenerationResult.MaxHeight` (the
heightmap's own vertical size) is exposed on `WorldRuntimeBounds` for
completeness but is **not** used as a ceiling anywhere in
`DroneRecoveryController` — flying high above the generated terrain is
entirely valid FPV behavior (freestyle, establishing shots, simply
climbing), and the config has no altitude-ceiling field at all. If a
future phase adds a genuine course concept of "ceiling," this is the one
place to wire it in — see §17.

## 7. Confirmation / debounce

`DroneRecoveryConfig.ConfirmationDurationSeconds` (default `0.5`s):
the first Tick that finds the drone out-of-bounds (and Racing — see §3)
transitions to `RecoveryPending` and records the clock time, without
recovering yet. Every subsequent Tick re-checks the *current* position:
if it's back within bounds, state returns to `Monitoring` immediately (no
recovery, no false positive from a single noisy/transient frame); if
still out-of-bounds once `ConfirmationDurationSeconds` have elapsed
(via the same `IGameplayClock` `RaceTimer` uses — never real-time
sleeping), recovery actually triggers. Non-finite positions skip this
entirely and recover on the very first Tick that observes them — an
invalid transform is never "maybe transient," it's immediately unsafe.

## 8. Respawn

`DroneRecoveryController.Reset` flow (`BeginRecovery`):
`RecoveryStarted` fires → checkpoint processing is suppressed (see §9)
→ `IDroneSpawnTarget.PlaceAt(spawnPosition, spawnRotation)` — the exact
same call `WorldGenerationRuntimeService` uses to place the drone
initially and `CourseGameplayController.Reset()` uses for manual
reset/restart, going through `DroneControllerSpawnTarget` →
`DroneController.SetSpawn` + `ResetToSpawn` → `RecoveryCompleted` fires.
**No second drone-reset implementation exists anywhere in this phase.**

## 9. Physics reset

`DroneController.ResetToSpawn()` (Phase 3, untouched) already does
everything this phase needs: `DronePhysics.ResetTo` sets
`Rigidbody.position`/`rotation` and zeroes `velocity`/`angularVelocity`
in one call, `DroneInput.ResetKeyboardThrottle()` clears any held virtual
throttle, and `FlightModeController.ForceDisarm()` disarms (motors off)
— the same disarm-on-reset behavior manual Reset/the "R" debug key
already had. Recovery reuses this exactly; nothing in `DronePhysics`/
`DroneFlightModel`/`FlightModeController`/`DroneInput` was modified.

## 10. Checkpoint preservation

`DroneRecoveryController` never calls `CheckpointManager.Reset()` (which
zeroes progress — that's `CourseGameplayController.Reset()`'s job, for
manual reset only) and never calls `ReportCheckpointPassed` itself.
`CurrentCheckpointIndex`/`CompletedCheckpoints` are simply never touched
by a recovery — gate 5/15 before a crash is still gate 5/15 after
respawn.

The one real risk is the respawn *teleport itself*: `SpawnResolver`'s own
overlap check uses `QueryTriggerInteraction.Ignore` (it validates against
solid obstacle/environment colliders, not trigger volumes), so a spawn
point is not actually guaranteed clear of a checkpoint's trigger volume.
To guarantee a respawn can never register as passing (or wrongly
attempting) a checkpoint: `CheckpointManager` gained a small, narrow
addition this phase — `IsSuppressed`/`SetSuppressed(bool)` — a complete
no-op switch for `ReportCheckpointPassed` (no state change, no events)
distinct from `Reset()` (which zeroes progress; this only pauses
reporting). `CourseGameplayController.SetCheckpointProcessingSuppressed`
is the one narrow passthrough `DroneRecoveryController` calls — suppress
at the start of a recovery, un-suppress once `CooldownDurationSeconds`
has elapsed (see §11), so the drone has had time to actually settle at
its spawn before checkpoint triggers go live again. This is the same
"reuse the existing defensive pattern" Phase 11 already established for
Countdown→Racing (which resets progression at the exact moment Racing
starts, discarding any pre-GO trigger touch) — a small, targeted addition
to an existing class, not a duplicate checkpoint system.

## 11. Timer behavior

**The race timer is never touched by a recovery.** `RaceTimer`/
`CourseGameplayController.ElapsedSeconds` keep advancing through a
recovery exactly as if nothing happened — matching this phase's explicit
"the timer should continue from the race start rather than resetting the
whole race" instruction, taken literally: not paused, not stopped, not
reset. A recovery is a near-instantaneous teleport (no async work, no
multi-frame settling beyond the Cooldown window described next), so there
is no meaningful "how long did the reset take" to account for anyway.

## 12. Cooldown

`DroneRecoveryConfig.CooldownDurationSeconds` (default `1.5`s): after
`BeginRecovery` completes, state moves to `Cooldown`. While in `Cooldown`,
`Tick()` does not evaluate the drone's position at all — no new
recovery can trigger, however invalid the (possibly still-fake, in a
test, or still-settling, in reality) reading is — preventing the
"respawn → still detected as invalid → immediate second respawn → loop"
failure mode explicitly warned against. Checkpoint processing stays
suppressed for the same window (see §10). Once
`CooldownDurationSeconds` have elapsed, checkpoint processing resumes and
state returns to `Monitoring`.

## 13. World generation interaction / regeneration

Recovery only ever runs while a valid generated world is actually bound.
`WorldGenerationRuntimeService.HandleStateChanged` (extended again this
phase, same method Phase 11 already extended for the course) unbinds
**both** `CourseGameplayController` and `DroneRecoveryController` on
every non-`Ready` state — including the transient
Designing/Validating/Generating states a fresh `GenerateWorldAsync` call
passes through — and binds both fresh on `Ready`:

```csharp
if (state != WorldGenerationState.Ready)
{
    _courseGameplayController?.Unbind();
    _droneRecoveryController?.Unbind();
    return;
}
...
_courseGameplayController?.BindToCourse(result.CheckpointManager, result.SpawnPosition, result.SpawnRotation);
_droneRecoveryController?.Bind(result.Bounds, result.SpawnPosition, result.SpawnRotation);
```

This guarantees recovery never runs during Designing/Validating/
Generating, never retains a previous world's bounds after a regeneration
(`Unbind()` always happens before the new `Bind()`, and `DroneRecoveryController`
holds no other reference to the old `WorldRuntimeBounds`/`GeneratedWorld`),
and always re-binds to the newly generated world's actual bounds/spawn —
verified directly in the extended `WorldGenerationRuntimeServiceTests`
(real Mock → `WorldGenerator` pipeline, real generated terrain, not a
fake).

## 14. Clear behavior

`ClearGeneratedWorld()` drives `WorldGenerationState` back to `Idle`, a
non-`Ready` state — the same `Unbind()` path above runs: recovery bounds
are discarded, `IsBound` becomes `false`, and `Tick()` becomes a no-op
until the next successful generation. The drone, the UI, and
`WorldGenerationController` itself are all untouched, per this phase's
explicit instruction.

## 15. Configuration

`Sim.Gameplay.DroneRecoveryConfig` — a plain `[Serializable]` class (not
a ScriptableObject; small enough that a dedicated asset would be
overkill), exposed as a tunable field on `RuntimeSimulationBootstrap`:

| Field | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Master switch. `false` disables all automatic recovery — manual Reset is unaffected either way. |
| `RecoveryMargin` | `25` m | How far beyond the terrain's actual horizontal footprint before counting as out of bounds. |
| `BelowWorldMargin` | `15` m | How far below the sampled ground height before counting as below-world. |
| `ConfirmationDurationSeconds` | `0.5` s | Debounce window for horizontal/below-world violations (non-finite positions skip this). |
| `CooldownDurationSeconds` | `1.5` s | Post-recovery pause before monitoring/checkpoint processing resume. |

No maximum-altitude field exists — see §6.

## 16. Manual Reset (unaffected)

`CourseGameplayController.Reset()` (Phase 11) is completely untouched by
this phase — same method, same behavior, still the only thing the Course
HUD's Reset button calls. Automatic recovery and manual Reset are
deliberately two separate actions using the same underlying
`IDroneSpawnTarget.PlaceAt` mechanism, not one replacing the other; with
`DroneRecoveryConfig.Enabled = false`, manual Reset is the only recovery
path available, exactly as before this phase existed.

## 17. HUD

`CourseHUD.BindRecovery(DroneRecoveryController)` (new, optional) —
subscribes to `RecoveryStarted`/`RecoveryFailed` purely to show
"RECOVERING..."/"RECOVERY FAILED" on the same transient message line
checkpoint/wrong-checkpoint feedback already uses (auto-clears after ~2s
via the existing timeout in `CourseHUD.Update()`). Never a permanent
indicator; course state text (`COURSE READY`/`RACING`/etc.) is completely
untouched by recovery. `CourseHUD` still contains no gameplay/recovery
*logic* — it only displays what `DroneRecoveryController` already
computed.

## 18. Events

`DroneRecoveryController`: `RecoveryStarted(string reason)`,
`RecoveryCompleted()`, `RecoveryFailed(string reason)`. Plain C# events,
no event bus. `RecoveryFailed` fires instead of `RecoveryCompleted` when
a recovery was attempted but couldn't actually be carried out (no
`IDroneSpawnTarget` bound) — a clean, observable failure, never a crash
and never a silently-pretended success.

## 19. Testing

**Automated tests written — not run.** No Unity Editor is available in
this environment (stated honestly, same as every prior phase). These are
real, checked-in EditMode tests a Unity Test Runner will execute:

- `DroneRecoveryControllerTests` — the full state machine: disabled →
  no recovery; inside bounds → no recovery; horizontal/below-world
  crossing → `RecoveryPending` (not immediate); confirmed after the
  debounce window → recovers; briefly crossing and returning → no false
  recovery; NaN/Infinity → immediate recovery regardless of confirmation
  duration *and* regardless of course state (including `Waiting`);
  recovery uses `IDroneSpawnTarget`/restores bound spawn position+
  rotation; checkpoint index preserved across a recovery; checkpoint
  processing suppressed through the whole cooldown window, then resumed;
  a `Finished` race is not recovered/restarted by an out-of-bounds
  drone; cooldown prevents an immediate second recovery, then allows one
  again once elapsed; `RecoveryStarted`/`RecoveryCompleted` fire exactly
  once per recovery, `RecoveryFailed` fires (not `RecoveryCompleted`)
  with no spawn target bound; `Unbind`/`Bind`/rebind reset cleanly; the
  race timer keeps advancing through a recovery, never reset. Uses a
  fake `IGameplayClock` (jumps instantly — no test sleeps for real
  seconds), a fake `IDroneSpawnTarget`/`IDroneStateSource`, and a
  **real** generated `UnityEngine.Terrain` (via the actual
  `TerrainGenerator`, wrapped in a real `WorldRuntimeBounds`) — no reason
  to fake terrain sampling when Unity's own Terrain system runs fine in
  EditMode.
- `WorldRuntimeBoundsTests` — construction guard, and
  `IsWithinHorizontalBounds`/`SampleGroundHeight` delegate correctly to a
  real `TerrainGenerationResult`.
- `CheckpointManagerTests` (extended) — `SetSuppressed`/`IsSuppressed`:
  a complete no-op for both correct and wrong-index reports while
  suppressed, normal processing resumes once un-suppressed.
- `CourseGameplayControllerTests` (extended) —
  `SetCheckpointProcessingSuppressed` passthrough blocks/restores
  progression correctly, no-ops safely with nothing bound.
- `WorldGenerationRuntimeServiceTests` (extended) — recovery binds to the
  real generated bounds on `Ready` (via `MockWorldDesigner` →
  `WorldGenerator`, the real pipeline); regenerating rebinds the *same*
  `DroneRecoveryController` instance (proving no duplicate managers);
  `ClearWorld()` unbinds; a null `DroneRecoveryController` doesn't break
  reaching `Ready`.

**Unity trigger/physics behaviour**: exactly as Phase 11 already noted,
a live drone `Rigidbody` actually crossing a generated `CheckpointTrigger`
(or actually falling through terrain under real gravity) cannot be
exercised in EditMode — Unity's physics step does not run outside Play
mode. Listed explicitly in the manual checklist below, not silently
skipped or fabricated.

## 20. Manual Unity test checklist

Nothing below has been run in a live Editor — none was available while
writing this phase, exactly as every prior phase has stated. This is what
to check by hand in Unity 2022.3 LTS:

1. Generate the Himalayan example course; confirm it reaches Ready and
   the Course HUD shows `COURSE READY`, `1 / 15`.
2. Click **Start Race**; wait for GO.
3. Fly normally through the opening section.
4. Confirm no false recovery/"RECOVERING..." message during normal
   flight anywhere within the course bounds.
5. Perform an aggressive roll/flip in Acro mode.
6. Confirm no recovery is triggered merely from rotation, however fast.
7. Fly close to (but still inside) the course's horizontal boundary.
8. Confirm a reasonable margin — recovery does not trigger just for
   approaching the visual terrain edge.
9. Deliberately fly straight out past the terrain's edge and keep going.
10. Confirm "RECOVERING..." appears after roughly half a second of
    remaining outside, then the drone snaps back to the spawn point.
11. Confirm the drone returns to spawn position **and** spawn
    orientation (not its pre-crash rotation).
12. Confirm checkpoint progress (`gate N/15`) is unchanged by the
    recovery.
13. Confirm the race timer did not reset or visibly pause — it should
    read a continuously increasing time across the recovery.
14. Deliberately fly down through the terrain (if reachable) or off a
    cliff edge until well below the ground.
15. Confirm recovery triggers and the drone returns to spawn.
16. Confirm no infinite respawn loop — after one recovery, the drone
    stays at spawn (does not immediately re-trigger) for at least the
    cooldown window.
17. Trigger a recovery while flying near a checkpoint gate.
18. Confirm the checkpoint is **not** accidentally counted as passed by
    the respawn teleport.
19. Complete the race (pass all 15 gates).
20. Fly the (still-controllable) drone out of bounds after Finished;
    confirm **no** recovery/restart occurs — the race stays Finished.
21. Generate a second, different world.
22. Confirm the old world's bounds are not used — flying to where the
    *old* world's edge was (now likely still inside the *new* terrain,
    or outside it, depending on the new world's size) behaves according
    to the *new* world's actual footprint, not the old one's.
23. Clear the world.
24. Confirm recovery monitoring stops (no "RECOVERING..." message even if
    the drone is left somewhere odd) until a new world is generated.
25. Manually click **Reset** (not triggered by a fall).
26. Confirm manual Reset still works exactly as it did in Phase 11.
27. Confirm existing FPV telemetry (altitude/velocity/mode/throttle/
    horizon) and all three flight modes remain completely unchanged
    throughout every step above.

## 21. Performance

Reviewed for the brief's explicit concerns:

- **No per-frame allocations** — `Tick()`/`EvaluatePosition`/
  `IsOutOfBounds` only read cached fields and do float/`Vector3` math; no
  `new`, no LINQ, no boxing.
- **No scene-wide search** — `FindObjectOfType`/`FindGameObjectsWithTag`
  never appear in `DroneRecoveryController`. The drone is identified via
  the already-injected `IDroneStateSource`/`IDroneSpawnTarget` reference
  (itself backed by the `DroneController` `RuntimeSimulationBootstrap`
  already resolved once at startup), never by `GameObject.name` or a
  runtime lookup.
- **No repeated `GetComponent`** — `DroneControllerSpawnTarget.Position`/
  `Rotation` read `DroneController.transform` directly (a cached field
  access on an already-held component reference), not a fresh
  `GetComponent` call.
- **No expensive physics queries** — no `Physics.OverlapSphere`/
  raycasts/etc. anywhere in this phase's new code; `SampleGroundHeight`
  is the same terrain heightmap lookup `SpawnResolver` already performs
  once per spawn attempt, called here at most once per frame while the
  drone is within the horizontal margin.

## 22. Known limitations

- No maximum-altitude recovery — deliberate, see §6.
- Non-finite checks cover **position** only (not rotation/velocity) —
  the position check alone already prevents an invalid transform from
  propagating, and adding a broader "relevant state" surface without a
  concrete corrupted-value scenario to guard against would be scope
  beyond what this phase's brief actually described.
- `DroneRecoveryController` does not itself verify "recovery does not
  modify drone flight-mode configuration" via an automated test — the
  fakes used (`IDroneSpawnTarget`/`IDroneStateSource`) have no
  flight-mode concept to assert against, and a real `DroneController`
  can't be meaningfully driven outside Play mode (see Phase 3's own
  remarks on this exact EditMode gap). Verified instead by code
  inspection: `DroneRecoveryController.cs` never imports `Sim.Drone`,
  never holds a field of type `FlightModeController`/`DroneConfig`, and
  never calls a method on either — the only place either name appears in
  that file is this class's own doc comment stating that fact (`grep -c
  "FlightModeController\|DroneConfig" Assets/Scripts/Gameplay/
  DroneRecoveryController.cs` returns 1: this remark, not a usage).
  `ResetToSpawn()` does call `FlightModeController.ForceDisarm()` as it
  always has (Phase 3) —
  disarming (motors off), not a *mode* change (Angle/Horizon/Acro is
  untouched) — the same existing behavior manual Reset already has.
- Countdown/confirmation/cooldown durations are small numeric constants
  in `DroneRecoveryConfig`, tunable in the Inspector but not exposed
  through `WorldSpecification`/prompt-driven generation.
- No automated Play Mode test evidence — see §19/§20. This environment
  has no Unity Editor available to run one; nothing here claims
  otherwise.

## 23. Future improvements

- A course-defined maximum altitude, if a future phase adds that concept
  to `CourseSpecification` (see §6) — `WorldRuntimeBounds.MaxHeight`
  already exists and could feed it without further plumbing.
- Optional velocity-at-impact-adjacent heuristics *layered on top of*
  (never replacing) the position-based detection here, if a future phase
  wants faster-than-debounce reaction to a genuine high-speed terrain
  impact — deliberately not attempted this phase, to avoid exactly the
  "misfires on legitimate flight" failure mode the brief warned against.
- Configurable recovery tuning exposed per-generated-world (e.g. a tight
  technical course wanting a smaller `RecoveryMargin`) rather than one
  global `DroneRecoveryConfig` for the whole session.

**Update, Phase 13:** `DroneRecoveryController` gained a per-run
`RecoveryCountThisRun` counter (reset on `RaceStarted`/`Bind`/`Unbind`,
incremented only on a successful recovery) so the new results panel can
report how many automatic recoveries happened during a completed run —
see docs/PHASE_13_COURSE_RESULTS.md §5.
