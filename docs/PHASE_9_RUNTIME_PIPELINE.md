# Phase 9 — Runtime Prompt Pipeline

## 1. Runtime architecture

```
WorldGenerationUI (Sim.UI)              — collects prompt text, displays state, zero business logic
        ↓ GenerateWorldAsync(prompt) / Cancel() / ClearWorld()
WorldGenerationRuntimeService (Sim.Simulation)  — the drone-wiring bridge (new this phase)
        ↓ (forwards to)                          ↓ (on Ready)
WorldGenerationController (Sim.Core)     IDroneSpawnTarget → DroneController
        ↓ IWorldDesigner.DesignWorldAsync
        ↓ IWorldSpecificationValidator.Validate
        ↓ WorldGenerator.Generate
GeneratedWorldResult (spawn position/rotation, CheckpointManager)
```

`WorldGenerationController` (existing since Phase 6, extended — not replaced — this phase)
is the **single authoritative orchestrator**: it now owns Design → Validate →
Generate end to end, not just Design → Validate as it did through Phase 8.
`RuntimeSimulationBootstrap` (Sim.Simulation) is the composition root that
builds this chain once at startup and wires it to the UI and the drone.

**No keyword parsing anywhere in this chain.** The prompt is read once
(`WorldGenerationUI`, from the input field) and handed to
`WorldGenerationController.GenerateWorldAsync(prompt)` unmodified; that
method wraps it into a `WorldDesignRequest` and passes it straight to
`IWorldDesigner.DesignWorldAsync`. No `if (prompt.Contains(...))` exists
anywhere in this phase's code — verified by construction, not just by
omission (`grep -rn "prompt.Contains"` across `Assets/Scripts/` finds
nothing).

## 2. Why a separate `WorldGenerationRuntimeService`, not just the controller

`WorldGenerationController` still has **no reference to `Sim.Drone`** —
preserved deliberately, matching `WorldGenerator`'s own "world construction
and drone control stay cleanly separate" rule from Phase 8. Folding
drone-placement directly into the controller would break that boundary and
make the controller unusable standalone (as Editor tooling already uses it,
with no drone in the scene at all). `WorldGenerationRuntimeService` is the
one place in the runtime layer allowed to know about both: it subscribes to
`controller.StateChanged`, and when state reaches `Ready`, calls
`IDroneSpawnTarget.PlaceAt(...)` with the resolved spawn. It does not
duplicate or shadow the controller's state machine — `Controller` exposes
the real one directly; a UI reads `service.Controller.State`, not a second,
parallel state.

## 3. Controller state machine

`WorldGenerationState` (`Sim.Core`) — extended in place this phase (not a
second competing enum):

```
Idle → Designing → Validating → Generating → Ready
                                            ↘ Failed
                          (from any point) ↘ Cancelled
```

Renamed from the Phase 6/8 version (`Requesting`→`Designing`,
`Completed`→`Ready`) and given a new `Generating` value, to match this
phase's exact state vocabulary and to reflect that reaching a terminal
success state now means a real Unity world exists, not just a validated
specification.

## 4. Mock vs. LLM mode

`RuntimeSimulationBootstrap` exposes `WorldDesignerMode { Mock, LLM }` in
the Inspector (defaults to `Mock`). Mock mode constructs `MockWorldDesigner`
directly — no external configuration, no network, no API keys, works
completely offline, and reuses Phase 7's implementation unchanged. LLM mode
additionally exposes `LLMProviderKind { OpenAI, Anthropic, Local }` and
constructs `LLMWorldDesigner` backed by the corresponding `ILLMClient` stub
(`OpenAiLLMClient`/`AnthropicLLMClient`/`LocalLLMClient`, all from Phase 7).
**None of these three make a real network call** — no credentials exist
for any of them in this project's environment. Selecting LLM mode does not
fake a successful AI response: `GenerateWorldAsync` reaches `Failed` with
`WorldDesignFailureReason.NotConfigured` and a message pointing at
`docs/AI_WORLD_DESIGNER.md`, regardless of which provider is selected.

## 5. Dependency injection

`WorldGenerationController`'s constructor takes `IWorldDesigner` (an
interface), never a concrete provider type. `RuntimeSimulationBootstrap`
is the only place that decides *which* concrete `IWorldDesigner` to
construct (based on `_mode`/`_llmProvider`); nothing downstream of that
point — the controller, the runtime service, the UI — needs to change to
support a different provider. `WorldGenerationRuntimeService` similarly
takes `IDroneSpawnTarget`, not a concrete `DroneController` reference — see
§8.

## 6. Validation flow

Unchanged from Phase 6/8: `WorldGenerationController` calls the existing
`WorldSpecificationValidator.Validate(...)` — no validation rules are
duplicated in the UI or anywhere in this phase's new code. On failure, every
`ValidationError` (field + message, exactly as the validator produced it —
not reworded) is logged via `Debug.LogWarning`, and the controller's
user-facing `LastErrorMessage` is set to `"World specification failed
validation."` (the UI shows this via `WorldGenerationStatusFormatter`; the
per-field detail is in the Console for diagnosis, matching how the Phase 6
error-handling table already documented "surfaced in the debug panel").

## 7. Error handling

Every failure path ends in `WorldGenerationState.Failed` with
`LastErrorMessage` set to a UI-safe string and `LastFailureReason` set to
the specific `WorldDesignFailureReason` — never an uncaught exception, and
never a partial success. Concretely:

| Failure point | Result |
|---|---|
| Designer returns `Success = false` (not configured, network error, etc.) | `Failed`, reason from the outcome |
| Designer throws unexpectedly | `Failed`, reason `Unknown` (logged in full; UI message stays generic) |
| Validator rejects the specification | `Failed`, reason `ValidationFailed` |
| `WorldGenerator.Generate` returns `Success = false` (e.g. unresolvable spawn) | `Failed`, reason `Unknown`, message is `WorldGenerator`'s own `ErrorMessage` |

`WorldGenerator`'s own cleanup (Phase 8: clears any partial world on
failure) is untouched and still the only place that ever destroys generated
GameObjects — the controller doesn't duplicate that logic, it just doesn't
call `WorldGenerator` again until asked to.

## 8. Cancellation

Cancellation flows through the async design phase via the same
`CancellationTokenSource`/stale-call-guard mechanism already established in
Phase 6/8 — unchanged. What's new this phase: `WorldGenerator.Generate()` is
synchronous, main-thread-only Unity object construction with no safe way to
interrupt it partway (per this phase's explicit instruction not to invent
unsafe multithreading around Unity API calls). So there is exactly one
additional cancellation check, placed right before the point of no return:
immediately after validation succeeds and before `Generate()` is called, if
cancellation was requested (during the design `await`, or immediately
after), generation never starts at all and the controller goes straight to
`Cancelled`. Once `Generate()` has actually started, it always runs to
completion — this is "cancel the design phase and prevent subsequent
generation," exactly as instructed, not an attempt to abort Unity object
construction mid-flight.

## 9. Threading

- `IWorldDesigner.DesignWorldAsync` may genuinely need to await network I/O
  for a real LLM call — that's why `GenerateWorldAsync` is `async`.
- `WorldGenerator.Generate()` — and everything after it in the method
  (validation, the cancellation check, `Generate()` itself) — runs
  synchronously on the **main thread**, safe to call directly.
- This safety depends on Unity's own `SynchronizationContext`: every
  `await` in this codebase resumes its continuation back on the main
  thread automatically, because nothing here ever uses `Task.Run` or
  `.ConfigureAwait(false)` — the two things that would break that
  guarantee. This was a deliberate check, not an assumption left
  unexamined: `grep -rn "Task.Run\|ConfigureAwait" Assets/Scripts/` was
  run before writing this section and returns nothing.
- No Unity API (`GameObject`, `Terrain`, `Collider`, `Transform`, scene
  operations, drone placement) is ever called from a background thread
  anywhere in this phase's code.

## 10. Drone spawn integration

`IDroneSpawnTarget` (`Sim.Simulation`) — a one-method interface
(`PlaceAt(Vector3, Quaternion)`) — is what `WorldGenerationRuntimeService`
actually depends on, not `DroneController` directly. `DroneControllerSpawnTarget`
is the real, production implementation: a thin adapter calling the existing
`DroneController.SetSpawn` + `ResetToSpawn` (Phase 3) — no drone physics,
camera, OSD, or rig-building logic is touched or duplicated. This
indirection exists specifically so `WorldGenerationRuntimeService` is unit
-testable with a fake target that just records what it was asked to do,
sidestepping a real gap in Unity's Edit-mode testing: `Awake()` doesn't run
for a component added via script outside Play mode, so a "real"
`DroneController` built in an EditMode test never actually gets its
`Rigidbody`/config references wired — see `DronePhysics`'s own Phase 3
remarks on this exact issue.

## 11. Runtime scene setup

`Assets/Scenes/` was empty going into this phase (no scene file has ever
actually been committed — see `docs/IMPLEMENTATION_PLAN.md`). Building the
scene by hand was rejected for the same reason established since Phase 3:
hand-authored `.unity` files are fragile (GUID/fileID references) and easy
to corrupt. Instead, `WorldGenerationTestTool.BuildRuntimeSceneToDisk()`
(**FPV Sim → World → Build Runtime Scene (Save To Disk)**) builds the scene
programmatically and saves it via `EditorSceneManager`, reusing
`DroneRigBuilder`'s existing, proven drone/camera/OSD construction (not
duplicated) and adding this phase's new pieces:

- The prompt UI canvas (`WorldGenerationUI` + a hand-built `TMP_InputField`/
  `Button` hierarchy — no external UI assets, matching the project's
  primitive-fallback philosophy applied to UI).
- An `EventSystem` with `InputSystemUIInputModule` — **not** the legacy
  `StandaloneInputModule`. This project already requires the New Input
  System to be active for drone controls to function at all (Phase 3), so
  the UI's input module has to match; the legacy module would silently
  receive no input if Active Input Handling excludes the old Input Manager.
- A `Simulation Bootstrap` GameObject carrying `RuntimeSimulationBootstrap`,
  wired to the drone and the UI.

Saved to `Assets/Scenes/MainScene.unity`. This mirrors exactly how
`DroneRigBuilder`'s own "Build ... Test Scene (Save To Disk)" commands
already work (Phase 3/4) — the same pattern, not a new one.

**Why the bootstrap doesn't build the drone rig itself at runtime**:
`DroneRigBuilder`'s construction uses `UnityEditor.AssetDatabase`/
`SerializedObject`/`Undo` — Editor-only APIs unavailable in a Player build
(and, more immediately, in `Sim.Runtime.asmdef`, which cannot reference
`Sim.Editor.asmdef`). Duplicating that construction logic in runtime-safe
code was rejected as exactly the "duplicate the drone rig builder" this
phase was told not to do. Instead, `RuntimeSimulationBootstrap` expects the
drone to already exist (built once via the Editor tool, saved into the
scene) and logs a clear, actionable warning — continuing to function for
world generation, just without placing a drone — if it doesn't find one.

## 12. How to test without API keys

Everything in this phase's default configuration (`WorldDesignerMode.Mock`)
requires zero external configuration. Open `MainScene.unity` (after
building it via the Editor command above), press Play, and the prompt UI
is pre-filled with the standing Himalayan example prompt
(`Sim.WorldGeneration.Models.ExamplePrompts.Himalayan` — the same constant
`WorldGenerationTestTool`'s own quick-test command uses, extracted this
phase so the two never drift into slightly different copies of the same
example). Click Generate — no internet, no Reactor, no OpenAI, no
Anthropic, nothing beyond what's already in the repository.

## 13. How to configure a real provider, if/when implemented

Not implemented this phase — deliberately (see §4 and
`docs/AI_WORLD_DESIGNER.md`). When a real `ILLMClient` implementation
exists for one of the three providers: set `RuntimeSimulationBootstrap`'s
`_mode` to `LLM` and `_llmProvider` to the corresponding provider in the
Inspector. Credentials must follow the same pattern already established for
OpenWorld Reactor (`docs/OPENWORLD_REACTOR_INTEGRATION.md` "Credentials") —
a local, gitignored `.env.local`-style file or environment variable, never
committed to source, never logged, never displayed in the UI.

## 14. Known limitations / what Phase 10 should address

- No real LLM provider is implemented — LLM mode always fails honestly.
  Implementing one (starting with whichever provider is actually
  configured first) is natural Phase 10 scope.
- `RuntimeSimulationBootstrap` cannot build the drone rig itself at
  runtime (§11) — the scene must be built once via the Editor tool first.
  A more self-sufficient runtime bootstrap (if ever needed for something
  like a WebGL/standalone build with no prior Editor step) would need the
  drone-construction logic factored into a genuinely runtime-safe shared
  class first — not attempted this phase, flagged as real follow-up work,
  not silently deferred.
- No progress indicator beyond the status text (no percentage/spinner) —
  the brief listed this as optional.
- No generated-world summary display (object counts, checkpoint count) in
  the UI yet — `GeneratedWorldResult`/`CheckpointManager` already carry
  this data; only the UI-side display is missing.
- ~~Checkpoint progress (current/total, lap completion) has no UI yet~~ —
  addressed Phase 11: `CourseGameplayController` + `CourseHUD`. See
  docs/PHASE_11_COURSE_GAMEPLAY.md.
- `WorldGenerationUI`'s Canvas/Button/TMP_InputField construction has not
  been run in a live Unity Editor — see "Manual Unity verification
  checklist" below for what needs confirming by hand, especially whether
  `InputSystemUIInputModule` receives clicks/typing correctly without an
  assigned Input Actions asset.

## Manual Unity verification checklist

Nothing below has been run in a live Editor — none was available while
writing this phase. This is what to do in Unity 2022.3 LTS to verify it:

1. Open the project. Let it resolve packages.
2. **FPV Sim → World → Build Runtime Scene (Save To Disk)** — builds and
   saves `Assets/Scenes/MainScene.unity`.
3. Check the Console immediately after — no errors, no warnings about
   missing serialized fields.
4. Open `MainScene`, enter Play Mode.
5. Confirm the prompt UI panel is visible (bottom-left), pre-filled with
   the Himalayan example prompt, status reads "Enter a world description."
6. Click **Generate**. Confirm the button becomes non-interactable and
   Cancel becomes interactable; watch the status text move through
   "Designing world...", "Validating world specification...", "Generating
   Unity world...", ending at "World ready — fly!".
7. Confirm `GeneratedWorld` appears in the Hierarchy with terrain/
   environment/obstacles, and the drone has moved to the generated spawn
   (visible in the FPV camera view / Scene view).
8. Fly (arm with Backspace, WASD/throttle) — confirm collision with
   generated geometry, and flying through a gate's opening doesn't collide.
9. Click **Generate** again with a different/modified prompt. Confirm the
   old `GeneratedWorld` is gone (search the Hierarchy — only one exists)
   and the drone relocates to the new spawn.
10. Click **Clear World**. Confirm `GeneratedWorld` disappears, status
    returns to "Enter a world description.", Generate is interactable
    again, Clear is not.
11. Clear the prompt field entirely and click Generate — confirm a clear
    "enter a world description" message, no crash, no state change.
12. Click Generate, then immediately click Cancel — confirm status reaches
    "Generation cancelled." and Generate becomes interactable again (not
    stuck).
13. In the Inspector, change `RuntimeSimulationBootstrap`'s Mode to `LLM`
    (any provider), re-enter Play Mode, click Generate — confirm a clear
    "not configured" failure message, not a fake success.
14. Specifically confirm UI interactivity itself: does clicking
    Generate/Cancel/Clear and typing in the prompt field actually work at
    all with `InputSystemUIInputModule` and no assigned Input Actions
    asset? This is the single least-certain piece of this phase without a
    live Editor to check it in.
