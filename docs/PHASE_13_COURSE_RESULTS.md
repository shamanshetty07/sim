# Phase 13 — Course Results / Race Summary

## 0. What was inspected first

Per this phase's explicit instruction, the full existing stack was read
before writing anything: `CourseGameplayController`, `CourseState`,
`RaceTimer`, `CheckpointManager`, `CourseHUD`, `CourseStatusFormatter`,
`WorldGenerationRuntimeService`, `WorldGenerationController`,
`WorldGenerationUI`, `RuntimeSimulationBootstrap`, `IDroneSpawnTarget`/
`DroneControllerSpawnTarget`, `DroneController`, `DroneRecoveryController`,
`WorldSpecification`/`CourseSpecification`, `GeneratedWorldResult`, the
Editor tooling, every existing test, and `docs/IMPLEMENTATION_PLAN.md`.
Confirmed: `CourseGameplayController.RaceFinished` is already the single
authoritative finish event (Phase 11); `DroneRecoveryController` had no
per-run recovery counter yet, so one small, targeted addition was needed
there (see §5) — everything else this phase needed already existed.

## 1. Architecture

```
CourseGameplayController.RaceFinished (existing, Phase 11)
        ↓
CourseResultsController (new)
        ↓ builds, from existing state only
CourseResult (new, immutable snapshot)
        ↓
CourseResultsUI (new) ←→ CourseResultFormatter (new, pure)
```

`CourseResultsController` is a plain C# class — same "constructed once,
never recreated" pattern as `CourseGameplayController`/
`DroneRecoveryController` — that does nothing except *listen* to
`CourseGameplayController`'s own events and *read* its own/
`DroneRecoveryController`'s already-existing public state
(`ElapsedSeconds`, `CurrentCheckpointIndex`, `TotalCheckpoints`,
`RecoveryCountThisRun`). It performs no calculation of its own beyond
copying those values into a `CourseResult` at the right instant — the UI
never calculates anything, exactly per this phase's explicit
architectural principle.

## 2. CourseResult model

`Sim.Gameplay.CourseResult` — immutable, get-only properties, one
constructor:

```csharp
public CourseResult(
    float elapsedSeconds,
    int completedCheckpoints,
    int totalCheckpoints,
    int recoveryCount,
    bool isCompleted,
    int worldSeed)
```

No mutable gameplay state, no reference to `CheckpointManager`/
`RaceTimer`/anything live — every field is a plain value copied out once.
`IsCompleted` is always `true` for every `CourseResult` produced today
(the only way one is ever constructed is via a genuine finish), kept as
an explicit field rather than assumed so a future producer of partial/
abandoned-run data has somewhere to say otherwise without changing this
type's shape. `WorldSeed` is recorded but deliberately not surfaced
prominently anywhere in the results UI (see §9) — available for a future
persistence phase, per this phase's explicit "may simply be available
internally for future persistence" instruction.

Course name/style (`WorldSpecification.WorldName`/
`CourseSpecification.Style`) were considered (explicitly optional in the
brief) and deliberately **not** added, to keep this phase's data model
and wiring surface minimal — see §14.

Verified immutable in tests two ways: every constructor argument lands
unchanged on its property, and a reflection-based regression guard
confirms every property is get-only (no public setter exists at all,
not just "nothing calls one").

## 3. Finish snapshot

`CourseResultsController` subscribes to exactly one thing that produces a
result: `CourseGameplayController.RaceFinished` — the same event Phase 11
already guarantees fires exactly once per completed run (guarded inside
`CheckpointManager.ReportCheckpointPassed`'s own `IsFinished` check, and
covered by that phase's own `RaceFinished_FiresExactlyOnce` test). No
second finish detector exists anywhere in this phase.

At the instant that event fires, `CourseGameplayController.HandleRaceFinished`
has *already* stopped the timer and set `State = Finished` (that method's
own order: `_timer.Stop(); SetState(Finished); RaceFinished?.Invoke();`)
— so by the time `CourseResultsController`'s handler runs,
`course.ElapsedSeconds` reads the frozen, stopped value (not a moving
`Time.time`-derived number), and `course.CurrentCheckpointIndex` already
equals `course.TotalCheckpoints` (the finish condition). The resulting
`CourseResult` is a true snapshot: nothing in it is recalculated later,
and nothing about it changes if the timer/checkpoints continue to exist
and change afterward (verified directly — advancing the fake clock by
1000 simulated seconds after a finish does not change the stored
`CourseResult.ElapsedSeconds` at all).

## 4. Time formatting

**No second time formatter was created.** `CourseResultFormatter.
FormatFinalTime` delegates straight to the existing `CourseStatusFormatter.
FormatTimer` — the exact mm:ss.ff format the live Course HUD's timer
already uses, per this phase's explicit "do not duplicate the timer/
progress logic" instruction. `CourseStatusFormatter.FormatTimer` gained
one addition this phase: a safety guard returning `"--:--.--"` for NaN,
Infinity, or negative input (previously undefined/implementation-specific
behavior — `Mathf.RoundToInt` on a NaN value is not something any prior
phase actually verified was safe). This benefits the live HUD too, not
just results — the same method, one call site, one guard.

Minutes are never wrapped: the format is `totalCentiseconds / 6000` for
minutes (plain integer division, no modulo), so `3661.25` correctly
displays as `"61:01.25"`, not an incorrect `"01:01.25"`. Verified for all
of this phase's specified example values: `0` → `"00:00.00"`, `1.234` →
`"00:01.23"`, `61.5` → `"01:01.50"`, `125.678` → `"02:05.68"`,
`3661.25` → `"61:01.25"`.

## 5. Recovery counting

`DroneRecoveryController` gained one new public property this phase:
`RecoveryCountThisRun` (int, get-only from outside). Incremented in
exactly one place — the success path of `BeginRecovery`, immediately
after `IDroneSpawnTarget.PlaceAt` actually runs — so a recovery that
fails (no spawn target bound) never increments it, matching "only
*successful* automatic recovery events... should increment the counter."

**Never incremented by**: manual Reset (`CourseGameplayController.Reset()`
calls `IDroneSpawnTarget.PlaceAt` directly — a completely separate code
path from `DroneRecoveryController.BeginRecovery`), initial spawn
placement (`WorldGenerationRuntimeService`'s own `PlaceAt` call on Ready —
also never touches `DroneRecoveryController`), or course
initialization/binding (`Bind()` only resets the counter, never
increments it).

Reset to `0`:
- Whenever `CourseGameplayController.RaceStarted` fires (the one event
  subscription `DroneRecoveryController` holds — see its own remarks on
  why this doesn't violate "never writes course state," since it only
  reactively resets its *own* internal counter). This is the primary,
  semantically-correct reset point: "a new race starts."
- Defensively, again, in both `Bind()` and `Unbind()` — so a stale count
  from an abandoned run (regenerated or cleared before ever finishing)
  can never leak into a result for a *later* run, even though in practice
  `RaceStarted` always fires again before any later result could exist.

## 6. Results UI

`Sim.UI.CourseResultsUI` (MonoBehaviour) — a dedicated center-screen
panel, built by `WorldGenerationTestTool.BuildCourseResultsCanvas()`
(same hand-built-hierarchy Editor-tooling pattern as
`BuildCourseHudCanvas`/`BuildWorldGenerationCanvas`). Shows "COURSE
COMPLETE", final time, gate count, recovery count, and Restart/New World
buttons. Contains no gameplay logic: visibility is `_course.State ==
CourseState.Finished` (driven off `CourseGameplayController.StateChanged`,
already existing), every displayed value is a pure function
(`CourseResultFormatter`) of the `CourseResult` handed to it via
`CourseResultsController.ResultsReady`, and both buttons only forward to
existing controllers (see §7/§8).

**Coexists with `CourseHUD`, does not replace it** — `CourseHUD`
(top-right) keeps showing `FINISHED`/final gate count/timer exactly as it
already did before this phase (unchanged); this panel adds a second,
more prominent moment plus the two new actions, without touching
`CourseHUD`'s own timer/progress display code at all. `CourseHUD`'s
existing Reset button remains available while Finished too (Phase 11
behavior, unchanged) — a minor, harmless overlap in *affordance* with the
new Restart button (both ultimately call the exact same
`CourseGameplayController.Reset()`), not a duplicate of any *logic*.

FPV telemetry (`FPVHUD`/`TelemetryUI`/`TelemetryFormatter`) is completely
untouched by this phase.

## 7. Restart behavior

`CourseResultsUI`'s Restart button calls `CourseGameplayController.Reset()`
directly — the exact same Phase 11 method `CourseHUD`'s own Reset button
already calls. It does **not** call `WorldGenerator.Generate()`,
`WorldGenerationController`, or `WorldGenerationRuntimeService` at all.
Flow: Finished → Reset() → checkpoints/timer reset, drone repositioned at
the *same* bound spawn (same `WorldRuntimeBounds`/`GeneratedWorld`,
untouched) → `CourseState.Waiting` (which `CourseResultsController`'s
`StateChanged` subscription already reacts to by clearing `LastResult`,
so the results panel hides itself and the stale result is discarded) →
user presses Start again.

## 8. New World behavior

`CourseResultsUI`'s New World button calls
`WorldGenerationRuntimeService.ClearWorld()` directly — the exact same
method `WorldGenerationUI`'s own Clear button already calls. It
implements no generation logic of its own: `ClearWorld()` →
`WorldGenerationController.ClearGeneratedWorld()` →
`WorldGenerationState.Idle` → the already-existing
`WorldGenerationRuntimeService.HandleStateChanged` non-Ready branch
(Phase 11/12, unchanged) unbinds `CourseGameplayController`/
`DroneRecoveryController`, which in turn clears `CourseResultsController.
LastResult` (via the same `StateChanged` rule). `WorldGenerationUI` was
already visible and interactive the whole time (never hidden by this
phase) — its prompt field keeps whatever text was last in it (the
Himalayan example prompt, if untouched), Generate becomes available
again, and the user edits/clicks Generate themselves. **No LLM call is
made merely by clicking New World** — generation only ever happens when
the user explicitly clicks Generate on the existing, unmodified
`WorldGenerationUI`.

## 9. Clear behavior

"Clear World" (the existing `WorldGenerationUI` button) and "New World"
(the new Results button) are literally the same action — both call
`WorldGenerationRuntimeService.ClearWorld()`. There is exactly one Clear
code path, not two. Its effect on results is identical to §8: `LastResult`
cleared, panel hidden, no active course, drone/UI/`WorldGenerationController`
itself untouched.

## 10. Persistence boundary

**Nothing added by this phase is written to disk, `PlayerPrefs`, a
database, or the cloud.** `CourseResult` is a plain in-memory object;
`CourseResultsController.LastResult` holds at most one instance at a
time, replaced or cleared, never appended to a list, never serialized.
If the Unity process restarts, every result disappears — there is no
save/load, no leaderboard, no best-time tracking, no achievements, no
persistence infrastructure of any kind. `WorldSeed` is carried on the
model specifically so a *future* persistence phase has something to key
off of, without this phase implementing any persistence itself.

## 11. Events

`CourseResultsController.ResultsReady(CourseResult result)` — one plain
C# event, fired exactly once per completed race, immediately after
`LastResult` is set to the newly built result. No event bus.
`CourseGameplayController.RaceFinished` remains the authoritative finish
signal; `ResultsReady` is downstream of it, never a competing or
duplicate finish notification.

## 12. Testing

**Automated tests written — not run.** No Unity Editor is available in
this environment (stated honestly, same as every prior phase). These are
real, checked-in EditMode tests a Unity Test Runner will execute:

- `CourseResultTests` — every constructor argument lands on the matching
  property unchanged; every property is get-only (reflection-based
  regression guard against a future accidental setter).
- `CourseResultsControllerTests` — final elapsed time captured at the
  finish instant and unaffected by the clock continuing to move
  afterward; completed/total checkpoint counts captured; recovery count
  captured (via a real `DroneRecoveryController`, including letting
  Cooldown elapse so the finish-driving checkpoint report isn't itself
  suppressed); recovery count starts at 0 for a fresh
  `DroneRecoveryController`; manual `Reset()` and initial
  bind/no-op `Bind()` never increment it; a full Reset→StartRace cycle
  resets it via `RaceStarted`; `ResultsReady` fires exactly once per
  finish, and a duplicate/extra `ReportCheckpointPassed` after finishing
  does not produce a second result (relying on `CheckpointManager`'s own
  existing guard, not a new one); `LastResult` is null before any finish,
  is cleared by `Reset()` (Restart), by `Unbind()` (Clear World), and by
  rebinding to a new course (regeneration); a second finish after a
  restart produces a genuinely different `CourseResult` instance;
  `SetWorldSeed` is carried into the next result produced.
- `CourseStatusFormatterTests` (extended) — `FormatTimer` over one hour
  (no minute wraparound), and NaN/positive-Infinity/negative-Infinity/
  negative-value safety fallback.
- `CourseResultFormatterTests` — every example value this phase's brief
  specifies (`0`, `1.234`, `61.5`, `125.678`, `3661.25`), the NaN/
  Infinity/negative safety fallback, completion-count formatting, and
  recovery-count formatting.
- `WorldGenerationRuntimeServiceTests` (extended) — the generated world's
  seed is carried into the next result produced, over the real
  Mock → `WorldGenerator` pipeline (not a fake); a null
  `CourseResultsController` doesn't break reaching Ready.

**Unity UI/GameObject behaviour**: `CourseResultsUI`'s actual panel
show/hide (`GameObject.SetActive`), button click wiring
(`Button.onClick`), and text assignment cannot be meaningfully exercised
without a live Canvas/EventSystem — this is a genuine Play-mode-only
concern (same category Phase 9/11/12 already documented for their own
UI classes), listed explicitly in the manual checklist below, not
silently skipped or fabricated.

## 13. Manual Unity test checklist

Nothing below has been run in a live Editor — none was available while
writing this phase, exactly as every prior phase has stated. This is what
to check by hand in Unity 2022.3 LTS:

1. Open the runtime scene; generate the Himalayan example world.
2. Start the race.
3. Confirm the results panel is hidden.
4. Race through all 15 gates.
5. Confirm the results panel appears immediately upon the final gate.
6. Confirm the displayed final time looks correct and matches the Course
   HUD's own timer at the moment of finish.
7. Confirm the gate count reads `15 / 15`.
8. Restart the course (do **not** finish it this time).
9. During the new run, deliberately trigger one automatic recovery (fly
   out of bounds, wait for it to trigger).
10. Finish the race.
11. Confirm the results panel shows `Recoveries: 1`.
12. Manually click the Course HUD's Reset button once (not from a
    crash) before starting a new run; confirm it did not affect any
    displayed recovery count from the *previous* result (there should be
    no result showing at all, since Reset hides results).
13. Click **Restart** on the results panel.
14. Confirm the results panel disappears immediately.
15. Confirm it's the same generated world (same terrain/gates/spawn) —
    not a newly generated one.
16. Finish the race again (a fresh, uneventful run).
17. Confirm a fresh result appears, with `Recoveries: 0`, distinct from
    the first result.
18. Click **New World** on the results panel.
19. Confirm the results panel disappears.
20. Confirm the World Generation UI is visible/interactive, with its
    prompt field showing whatever text was last there.
21. Enter a different prompt and click Generate.
22. Confirm a genuinely new world is generated, and the old result is
    nowhere shown (results panel stays hidden until this new course
    finishes).
23. Click **Clear World** (the original button, not New World).
24. Confirm results are cleared/hidden (should already be, from step 22's
    course never having finished — verify no stale result reappears).
25. Confirm FPV telemetry (altitude/velocity/mode/throttle/horizon)
    remains fully functional throughout every step above.
26. Confirm course checkpoint progress does not silently change on its
    own between finishing and clicking Restart (should stay at the
    finished count until Restart actually resets it).
27. Generate several worlds in a row and confirm no duplicate results
    panels, Course HUDs, or gameplay managers accumulate anywhere in the
    Hierarchy.

## 14. Known limitations

- Course name/style (`WorldSpecification.WorldName`/
  `CourseSpecification.Style`) are not included in `CourseResult` —
  explicitly optional in the brief, skipped to keep this phase's data
  model and wiring surface minimal. Adding them later is a small,
  additive change (two more constructor parameters, one more
  `SetWorldContext`-style call from `WorldGenerationRuntimeService`,
  matching how `WorldSeed` was wired) — not a redesign.
- `WorldSeed` is recorded but never displayed anywhere in the results UI
  in this phase (available internally only, per the brief's explicit
  "do not make the seed prominent unless useful").
- No automated Play Mode test evidence for the actual panel show/hide,
  button clicks, or text rendering — see §12/§13. This environment has
  no Unity Editor available to run one; nothing here claims otherwise.
- `CourseHUD`'s own Reset button and the results panel's Restart button
  both remain available and do the same thing while Finished — a minor,
  intentional UI overlap (see §6), not a defect.

## 15. Future persistence considerations (not this phase)

- `CourseResult.WorldSeed` is the one field explicitly included with a
  future persistence phase in mind — a best-time system could key stored
  times by seed (and, if added later, course style) without needing to
  change `CourseResult`'s shape.
- Persisting `CourseResult` instances (PlayerPrefs, a local file, or a
  future backend) is deliberately out of scope here — this phase only
  produces the in-memory snapshot; nothing writes it anywhere.
- Leaderboards, best-time comparison, achievements, and any ranking/
  progression system all remain future work, explicitly excluded from
  this phase's scope.
