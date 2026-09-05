# Phase 11 — FPV Course Gameplay, Checkpoints, Timing & Race HUD

## 0. What was inspected first

Per this phase's explicit instruction, the existing implementation was
read in full before anything was written: `CheckpointManager`,
`CheckpointTrigger`, `CheckpointDefinition`, `ObstacleGenerator`,
`ObstacleGenerationResult`, `WorldGenerator`, `GeneratedWorldResult`,
`WorldGenerationController`, `WorldGenerationState`,
`WorldGenerationRuntimeService`, `RuntimeSimulationBootstrap`,
`IDroneSpawnTarget`/`DroneControllerSpawnTarget`, `DroneController`,
`FlightTelemetry`, `FPVHUD`/`TelemetryUI`/`TelemetryFormatter`,
`WorldGenerationUI`/`WorldGenerationStatusFormatter`,
`WorldGenerationTestTool`, `DroneRigBuilder`, `CourseSpecification`,
`ObstacleSpecification`.

Phase 8 already had exactly one `CheckpointManager`, one
`CheckpointTrigger`, and a deterministically ordered checkpoint sequence
(`ObstacleGenerationResult.Checkpoints`, sorted by
`CheckpointDefinition.Index` — see `ObstacleGenerator.Generate`). None of
that was duplicated. This phase adds the race-flow layer *on top of* it.

## 1. Architecture

```
GeneratedWorldResult (SpawnPosition/Rotation, CheckpointManager)
        ↓ (on WorldGenerationController reaching Ready)
WorldGenerationRuntimeService                — extended, not replaced (Phase 9)
        ↓ BindToCourse(checkpointManager, spawnPos, spawnRot)   ↓ (unbind on every other state)
CourseGameplayController (Sim.Gameplay)       — new this phase
        ↓ owns                                ↓ reacts to
   RaceTimer (IGameplayClock)          CheckpointManager events
        ↓                                     (CheckpointPassed / RaceFinished / WrongCheckpointAttempted)
CourseHUD (Sim.UI)                    — new this phase, complements FPVHUD
```

`CourseGameplayController` is a plain C# class — same pattern as
`Sim.Core.WorldGenerationController` — constructed **once** by
`RuntimeSimulationBootstrap` and re-bound to a new `CheckpointManager`
every time a world regenerates, never recreated. That single-instance
policy is what guarantees no duplicate gameplay managers ever accumulate.

## 2. Course state machine

`Sim.Gameplay.CourseState`, deliberately separate from
`Sim.Core.WorldGenerationState` — the two never share a switch statement,
an enum, or a state value:

```
Waiting → Countdown → Racing → Finished
   ↑___________Resetting____________|
Failed (bound world has no usable checkpoints — terminal until a valid world is (re)bound)
```

- **Waiting** — a valid course is bound (or none has ever been bound) and
  idle. `TotalCheckpoints > 0` is required for `StartRace()` to do
  anything.
- **Countdown** — 3-2-1-GO running. Checkpoints/timer are not active yet.
- **Racing** — timer running; checkpoints advance `CurrentCheckpointIndex`
  in order.
- **Finished** — the final checkpoint was passed in order; timer stopped.
- **Failed** — the generated world had zero checkpoints, or no
  `CheckpointManager` at all. Not a crash, not a fake "playable" state.
- **Resetting** — transient, set only for the duration of one `Reset()`
  call, before landing back on `Waiting`.

`WorldGenerationController`'s own state machine
(`Idle → Designing → Validating → Generating → Ready → Failed/Cancelled`)
is completely untouched by this phase.

## 3. Checkpoint ordering

**Unchanged from Phase 8, reused exactly as-is.** `ObstacleGenerator`
sorts its `CheckpointDefinition` list by `Index` before returning it, and
`CheckpointManager`'s constructor discovers `CheckpointTrigger` components
via `GetComponentsInChildren` and reads each one's `CheckpointIndex` — a
value baked in at generation time via `CheckpointTrigger.Configure(index)`,
never inferred from `GameObject.name`, sibling/hierarchy order, or
distance from the drone at runtime. `CourseGameplayController` never
re-derives or re-sorts this sequence; it only reads
`CheckpointManager.CurrentCheckpointIndex`/`TotalCheckpoints`.

Verified directly in tests (not just by omission):
`CheckpointManagerTests.CheckpointOrder_DrivenByConfiguredIndex_NotSiblingIndexOrName`
and `CourseGameplayControllerTests.CheckpointOrder_IgnoresGameObjectNameAndHierarchyOrder`
both build a checkpoint hierarchy with triggers added in **reverse** index
order and named so alphabetical order disagrees with real order, and
confirm progression still only depends on `Configure(index)`.

## 4. Trigger behaviour / gate-passing semantics

Also unchanged from Phase 8: `CheckpointTrigger.OnTriggerEnter` identifies
the drone via `other.GetComponentInParent<DroneController>() != null` —
a component check, not a tag string or a name comparison — and only then
calls `CheckpointManager.ReportCheckpointPassed(index)`. Only the actual
checkpoint trigger volume counts; there is no "near enough"/distance-based
substitute. `ObstacleGenerator` places that trigger volume in the
obstacle's opening, separate from the obstacle's own blocking collider
frame — flying near or touching a gate's frame does not count, only
crossing through it does.

**Checkpoint progression is independent of `CourseGameplayController`'s
own state** — `CheckpointManager.ReportCheckpointPassed` will advance
`CurrentCheckpointIndex` regardless of whether the course is currently
Waiting, Countdown, Racing, or Finished. This matters for one edge case:
a drone sitting near gate 1 at spawn could, in principle, drift through
its trigger during the 3-second countdown, before the race has actually
started. To make sure that can never silently consume checkpoint 0 before
Racing truly begins, `CourseGameplayController.Tick()` calls
`CheckpointManager.Reset()` at the exact moment Countdown transitions to
Racing — so checkpoint 0 is guaranteed to still be required the instant
the timer starts, regardless of anything that touched the trigger
beforehand. Covered by
`CourseGameplayControllerTests.Tick_TransitionToRacing_DiscardsAnyCheckpointPassedDuringCountdown`.

## 5. Out-of-order / wrong-checkpoint handling

`CheckpointManager.ReportCheckpointPassed(index)`: if `index !=
CurrentCheckpointIndex`, progression does not advance — the same rule
Phase 8 already had — and now additionally raises
`WrongCheckpointAttempted(attemptedIndex, requiredIndex)`.
`CourseGameplayController` relays this as its own
`WrongCheckpointAttempted` event; `CourseHUD` shows "Checkpoint N
required" for two seconds. Deliberately simple — no path tracking, no
"you went the wrong way" directional logic, exactly as instructed.

## 6. Timer

`Sim.Gameplay.RaceTimer` — `Start()`/`Stop()`/`Reset()`/`IsRunning`/
`ElapsedSeconds` — driven entirely by `IGameplayClock`
(`UnityGameplayClock.NowSeconds => Time.time` in production), never
reading `Time.time` directly itself. `CourseGameplayController` is the
only thing that calls `Start`/`Stop`/`Reset` on it:

- `Start()` — the instant `Tick()` notices the countdown has elapsed
  (Countdown → Racing), never lazily on the first checkpoint pass (that
  was Phase 8's `CheckpointManager.ElapsedSeconds` behaviour — see §8 for
  why it was changed).
- `Stop()` — the instant `CheckpointManager.RaceFinished` fires.
- `Reset()` — on `BindToCourse`, `Unbind`, and `Reset()`.

Supports multiple Start/Stop pairs (accumulating elapsed time across them)
even though `CourseGameplayController` currently only ever does one of
each per race — no reason to make that assumption load-bearing in the
timer itself.

## 7. Start / countdown

`CourseGameplayController.StartRace()`: no-op unless `State == Waiting`
and `TotalCheckpoints > 0`; otherwise records `_countdownStartedAtSeconds`
and transitions to `Countdown`. `CountdownRemainingSeconds` computes
`CountdownDurationSeconds (3f) - (clock.NowSeconds -
countdownStartedAtSeconds)`, clamped to ≥ 0 — no coroutine, no
`WaitForSeconds`. Something has to notice when that reaches zero:
`Tick()`, called once per frame by `RuntimeSimulationBootstrap.Update()`
(the *only* per-frame polling this phase adds, and it does nothing in any
state but Countdown — cheap to call unconditionally). `CourseHUD` shows
"3", "2", "1", "GO!" via `CourseStatusFormatter.FormatCountdown`
(`Mathf.CeilToInt` of the remaining seconds, "GO!" once it hits 0).

## 8. Finish

`CheckpointManager.RaceFinished` fires exactly once, when the final
checkpoint is passed in order (`CurrentCheckpointIndex >=
TotalCheckpoints`, guarded so a stale extra report after finishing is a
no-op). `CourseGameplayController.HandleRaceFinished` stops the timer,
transitions to `Finished`, and raises its own `RaceFinished` event.
`CourseHUD` shows "FINISHED" and the frozen final time
(`RaceTimer.ElapsedSeconds` no longer advances once stopped — no separate
"final time" field needed). The drone is **not** frozen, destroyed, or
taken away from the player — it stays fully controllable, per this
phase's explicit instruction.

## 9. Reset / restart

One method, `CourseGameplayController.Reset()`, serves both "Reset" (e.g.
after a crash, mid-race) and "Restart" (after Finished) — the brief
describes them as the same sequence (stop timer → reset checkpoints →
reset course state → reposition drone → Waiting), and there is no
behavioural difference between the two once you're inside this method;
the HUD's Reset button is simply available whenever `State` is Countdown,
Racing, or Finished (`CourseStatusFormatter.IsResetAvailable`). Concretely:
`CheckpointManager.Reset()` → `RaceTimer.Reset()` →
`IDroneSpawnTarget.PlaceAt(startPosition, startRotation)` → `State =
Waiting` → `CourseReset` event. No-op if nothing is bound (e.g. calling
Reset while Failed).

**Drone reset reuses the existing abstraction, not a second
implementation**: `CourseGameplayController` depends on
`Sim.Simulation.IDroneSpawnTarget` — the exact same interface
`WorldGenerationRuntimeService` already uses to place the drone at a
freshly generated spawn — and `RuntimeSimulationBootstrap` passes it the
identical instance (`DroneControllerSpawnTarget`, wrapping
`DroneController.SetSpawn` + `ResetToSpawn`). There is no second
drone-reset code path anywhere in this phase.

## 10. Fall / out-of-bounds respawn — explicitly not implemented

Per this phase's explicit "don't build a sophisticated crash detection
system" instruction: **no automatic fall/out-of-bounds detection was
added.** `FlightTelemetry` already exposes altitude and velocity, and a
future phase could reasonably threshold on those, but doing so correctly
(what counts as "fallen" vs. diving intentionally through a low gate;
what counts as "out of bounds" for terrain that varies per generated
world) is exactly the kind of scope this phase was told to avoid. The
only respawn mechanism in this phase is the explicit user-triggered Reset
button (§9), which is always available while Racing/Countdown/Finished
and works regardless of *why* the user wants to reset.

## 11. World regeneration / Clear World interaction

`WorldGenerationRuntimeService.HandleStateChanged` (extended this phase):

```csharp
if (state != WorldGenerationState.Ready)
{
    _courseGameplayController?.Unbind();
    return;
}
// ... place drone ...
_courseGameplayController?.BindToCourse(result.CheckpointManager, result.SpawnPosition, result.SpawnRotation);
```

Unbinding on **every** non-Ready state (not only `Idle`/`Failed`) is
deliberate: a fresh `GenerateWorldAsync` call passes through
Designing/Validating/Generating on its way to a *new* Ready, and the old
course must stop reacting to the old `CheckpointManager` before
`WorldGenerator.Generate()`'s own `Clear()` destroys that old world's
GameObjects (Phase 8 behaviour, untouched) — never a subscription left on
a `CheckpointManager` whose triggers no longer exist.
`CourseGameplayController.Unbind()`/`BindToCourse()` always detach from
whatever was previously subscribed first, so there is never a duplicate
subscription and never a stale reference — verified in
`CourseGameplayControllerTests.BindToCourse_Rebind_OldCheckpointManagerNoLongerAffectsController`
and the corresponding `WorldGenerationRuntimeServiceTests` regeneration
test (using the real pipeline: `MockWorldDesigner` → `WorldGenerator`, not
a fake).

**Clear World** goes through the exact same `Unbind()` path (Clear sets
`WorldGenerationState` back to `Idle`, which is a non-Ready state) —
course state returns to `Waiting` with `TotalCheckpoints == 0`; the drone,
the UI, and `WorldGenerationController` itself are all untouched, per this
phase's explicit instruction.

## 12. Drone abstraction

Exactly as specified:

```
CourseGameplayController → IDroneSpawnTarget → DroneControllerSpawnTarget → DroneController
```

No new drone implementation, no direct `DroneController` reference inside
`Sim.Gameplay`.

## 13. HUD

`Sim.UI.CourseHUD` (MonoBehaviour) + `Sim.UI.CourseStatusFormatter` (pure,
static, unit-tested formatting — same pattern as `TelemetryFormatter`/
`WorldGenerationStatusFormatter`). Shows:

- Course state text: `COURSE READY` / `GET READY` / `RACING` / `FINISHED`
  / `RESETTING` / `COURSE UNAVAILABLE: <reason>`.
- Checkpoint progress, `N / total` (1-based, capped at total) while not
  counting down; the 3-2-1-GO countdown in the same slot while counting
  down.
- Timer, `mm:ss.ff` (e.g. `00:24.81`, `01:42.37`).
- A transient message line for checkpoint-passed / wrong-checkpoint
  feedback, cleared after 2 seconds.
- Start / Reset buttons, wired to `CourseGameplayController.StartRace()` /
  `.Reset()` — nothing else. No gameplay logic lives in `CourseHUD`; every
  displayed value is a pure function of the controller's own state,
  matching `WorldGenerationUI`'s existing "UI ↓ controller, never the
  reverse" rule.

Built by `WorldGenerationTestTool.BuildCourseHudCanvas()` (Editor tooling,
same construction style as `BuildWorldGenerationCanvas`/
`DroneRigBuilder.BuildOsdCanvas`) as a separate top-right panel — it does
not replace or modify `FPVHUD`/`TelemetryUI` in any way; altitude,
velocity, mode, throttle, and the rest of the existing OSD are untouched.

## 14. Events

`CourseGameplayController`: `StateChanged(CourseState)`, `CourseReady`,
`RaceStarted`, `CheckpointPassed(int)`, `WrongCheckpointAttempted(int
attempted, int required)`, `RaceFinished`, `CourseReset`,
`CourseFailed(string reason)`. Plain C# events — no event-bus framework.
Subscriptions: `CourseHUD.Initialize`/`Detach` always unsubscribes from
whatever it was previously bound to before (re)subscribing, matching
`FPVHUD.SetDroneController`'s existing pattern; `CourseGameplayController`
itself does the same with `CheckpointManager`'s events in
`Unbind()`/`BindToCourse()`.

## 15. Testing

**Automated tests actually run**: none — no Unity Editor is available in
this environment (same limitation every prior phase has stated honestly).
The tests below are real, checked-in EditMode tests that a Unity Test
Runner will execute; they were not fabricated as "passing."

- `RaceTimerTests` — Start/Stop/Reset/IsRunning/ElapsedSeconds, multiple
  start/stop cycles, all via a fake `IGameplayClock` (jumps instantly,
  never sleeps for real seconds).
- `CheckpointManagerTests` — order enforcement, `WrongCheckpointAttempted`,
  `RaceFinished` firing once, post-finish no-op, `Reset()`, and order
  independence from GameObject name/hierarchy (triggers built in reverse
  index order).
- `CourseGameplayControllerTests` — the full state machine (items 1-22
  from the brief's test list): initial Waiting; cannot start with no
  checkpoints; Waiting→Countdown→Racing; timer starts at Racing/stops at
  Finished; correct vs. wrong checkpoint; final checkpoint finishes and
  stops the timer; Reset zeroes checkpoint index and timer and invokes
  the drone spawn target; Unbind/rebind without stale references; events
  fire exactly once (`RaceStarted`, `CheckpointPassed`, `RaceFinished`,
  `CourseReset`, `CourseFailed`); order independent of GameObject name/
  hierarchy. Uses a fake `IGameplayClock` and a fake `IDroneSpawnTarget`
  (same reasoning as `WorldGenerationRuntimeServiceTests`: a real
  `DroneController` never gets its `Rigidbody`/config wired outside Play
  mode).
- `CourseStatusFormatterTests` — every formatter branch (state text,
  countdown digits/GO!, checkpoint progress incl. the 0-checkpoint
  placeholder, timer formatting incl. the 59.997s → "01:00.00" rounding
  edge case, wrong-checkpoint message, Start/Reset availability per
  state).
- `WorldGenerationRuntimeServiceTests` (extended) — course binds on
  Ready with the real generated `CheckpointManager` (via
  `MockWorldDesigner` → `WorldGenerator`, the real pipeline, not a fake);
  regenerating rebinds the *same* `CourseGameplayController` instance
  (proving no duplicate managers) and the old `CheckpointManager` no
  longer affects it; `ClearWorld()` unbinds back to Waiting/zero
  checkpoints; a null `CourseGameplayController` doesn't break reaching
  Ready.

**Unity trigger/physics behaviour**: `CheckpointTrigger`'s actual
`OnTriggerEnter` callback (a live drone Rigidbody physically crossing a
generated gate's trigger volume) cannot be exercised in EditMode — Unity's
physics step does not run outside Play mode. This was true for Phase 8's
own checkpoint triggers already and remains true here; it is a genuine
Play-mode-only concern, listed explicitly in the manual checklist below,
not silently skipped.

## 16. Manual Unity test checklist

Nothing below has been run in a live Editor — none was available while
writing this phase, exactly as every prior phase has stated. This is what
to check by hand in Unity 2022.3 LTS:

1. Open the project; let it resolve packages.
2. **FPV Sim → World → Build Runtime Scene (Save To Disk)** — confirm no
   Console errors, and confirm a "Course HUD" canvas now exists alongside
   "World Generation UI" and "FPV HUD" in the Hierarchy.
3. Open `MainScene`, enter Play Mode.
4. Enter/confirm the Himalayan example prompt, click **Generate**. Confirm
   world generation reaches "World ready — fly!".
5. Confirm the Course HUD reads **COURSE READY** and **1 / 15** (the
   Himalayan/Mock example's 15 gates).
6. Click **Start Race**. Confirm the countdown displays 3, 2, 1, GO! and
   the timer begins running at GO.
7. Fly through gate 1. Confirm progress becomes **2 / 15** and the
   message line briefly shows it.
8. Skip gate 2 and fly through gate 3. Confirm progress does **not**
   advance past 2/15, and the message line shows "Checkpoint 2 required".
9. Return to gate 2, then continue through the course in order.
10. Confirm passing the final (15th) gate shows **FINISHED**, the timer
    stops, and a final `mm:ss.ff` time is displayed and stays fixed.
11. Confirm the drone is still flyable after Finished (not frozen/
    destroyed).
12. Click **Reset**. Confirm progress returns to **1 / 15**, the timer
    returns to `00:00.00`, state returns to **COURSE READY**, and the
    drone has been repositioned back to the start spawn.
13. Click **Start Race** again and confirm a full second run works
    identically (no leftover state from the first run).
14. Generate a **new** world (different prompt or the same one again).
    Confirm the Course HUD rebinds to the new checkpoint count, the old
    course's progress/timer do not leak into the new one, and there is
    exactly one "Course HUD"/one gameplay controller in play (no
    duplicates accumulating in the Hierarchy or in behaviour across
    repeated generations).
15. Click **Clear World**. Confirm the Course HUD returns to an inactive/
    waiting display, the drone and both UI canvases remain present and
    functional, and `WorldGenerationController`'s own UI is unaffected.
16. Confirm the existing FPV telemetry HUD (altitude/velocity/mode/
    throttle/horizon) is still fully functional throughout all of the
    above — nothing in this phase should have touched it.
17. Confirm generated geometry still collides correctly (gates block
    flight outside their opening, terrain/environment collision
    unchanged) — this phase did not touch `ObstacleGenerator`/collision
    at all, but is worth reconfirming after the Editor-tooling changes.
18. Specifically confirm the physics-dependent piece EditMode cannot
    cover: flying the actual drone Rigidbody through a real generated
    `CheckpointTrigger` volume in Play Mode fires `OnTriggerEnter` and
    advances progress as expected.

## 17. Known limitations

- No automatic fall/out-of-bounds respawn — explicit user Reset only (see
  §10 for the reasoning).
- A checkpoint trigger is always "live" once bound; the only guard
  against a pre-race stray trigger touch is the Countdown→Racing reset
  described in §4 — a stray touch *during Racing itself* (e.g. drifting
  backward through an already-passed gate) has no special handling beyond
  the existing in-order check, matching standard FPV racing rules (you
  cannot re-trigger a gate you've already passed to gain anything, and
  gates ahead of the current one still require passing gates in between).
- No lap counting / multi-lap courses — `CheckpointManager` models one
  single pass through an ordered sequence, matching `CourseSpecification`
  having no lap-count concept yet.
- Countdown duration (3 seconds) and the checkpoint-message display
  duration (2 seconds) are constants, not exposed as configuration —
  matching this phase's "keep it simple" instruction.
- No automated Play Mode test evidence — see §15/§16. This environment has
  no Unity Editor available to run one; nothing here claims otherwise.

## 18. Future work (not this phase)

- Multi-lap courses / best-lap tracking.
- Configurable countdown duration, exposed in the WorldSpecification or a
  settings UI.
- Automatic fall/out-of-bounds detection, once a real definition of
  "fallen"/"out of bounds" exists per generated world (see §10).
- Persisted best times (explicitly out of scope this phase — no
  database/persistence was added).
- Multiplayer/leaderboards (explicitly out of scope this phase).
