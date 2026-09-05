# Phase 14 — Save / Load

## 0. What was inspected first

Per this phase's explicit instruction, the full existing stack was read
before writing anything: `WorldSpecification`, `WorldGenerationController`,
`WorldGenerationRuntimeService`, `WorldGenerator`, `GeneratedWorldResult`,
`WorldSeedManager`, `CourseGameplayController`, `CheckpointManager`,
`RaceTimer`, `DroneRecoveryController`, `CourseResultsController`,
`RuntimeSimulationBootstrap`, `WorldGenerationUI`, `CourseHUD`,
`CourseResultsUI`, `WorldSpecificationValidator`, and every existing test
— plus `docs/IMPLEMENTATION_PLAN.md` and every prior phase doc.

Two things this phase reused rather than invented: `Assets/Scripts/
WorldGeneration/Persistence/` already existed as an empty directory,
reserved since Phase 2's architecture document ("`WorldSaveData`,
save/load"); `WorldGenerationMetadata.SchemaVersion` already anticipated
"checked on load by Persistence" in its own Phase-7-era doc comment. This
phase filled in exactly that reserved gap.

## 1. What is saved

A `WorldSaveData` — composition over duplication, wrapping the existing
model types rather than re-declaring their fields:

```csharp
public sealed class WorldSaveData
{
    public int Version { get; set; }               // save-file format version
    public string Prompt { get; set; }              // mirrors Specification.OriginalPrompt exactly
    public int Seed { get; set; }                   // mirrors Specification.Seed exactly
    public WorldSpecification Specification { get; set; }
    public WorldGenerationMetadata Metadata { get; set; } // same instance as Specification.Metadata
    public DateTime SavedAtUtc { get; set; }
}
```

`Prompt`/`Seed` are surfaced as their own top-level properties even
though `WorldSpecification.OriginalPrompt`/`.Seed` already carry the same
values — per this phase's own example shape (`WorldSaveData { Prompt,
Seed, Specification, Metadata }`). They are never a second independent
source of truth: `WorldSaveData.FromSpecification(specification)` is the
**only** supported construction path, and it always copies both straight
from `specification`; `WorldSaveValidator` (§3) rejects a save file where
they've drifted apart (only possible via a hand-edited/corrupted file on
disk, never through normal use).

## 2. What is deliberately NOT saved

No `GameObject`/`Component`/`Terrain`/`Transform`/`Rigidbody` reference,
no generated mesh/collider data, no Unity object instance ID, no event
subscription/delegate, no API key or other credential/authentication
state, and no live simulation state: current Rigidbody velocity/drone
transform, checkpoint trigger runtime state, countdown state, the active
race timer, recovery cooldown, or any UI state. This is a save of the
**world definition**, not a serialization of the live race — after a
load, the course begins from a clean `Waiting` state via the existing
bind lifecycle (§7), exactly like a freshly generated world.

## 3. Save format / validation flow

`IWorldSaveSerializer`/`WorldSaveJsonSerializer` is the one place a save
file's text becomes a `WorldSaveData` object, using the exact same safety
settings `WorldSpecificationJsonParser` (Sim.AI.WorldDesign, Phase 7)
already established — a save file is untrusted input in precisely the
same sense LLM output is:

- `TypeNameHandling.None` — never resolves a type from a `$type` field.
  **Never** change this; it is what prevents a hand-edited or corrupted
  save file from ever instantiating an arbitrary .NET type.
- `MetadataPropertyHandling.Ignore` — any `$type`/`$id`/`$ref` is ignored
  outright, not merely unresolved.
- `MissingMemberHandling.Ignore` — an unrecognized field (e.g. from a
  newer app version) is dropped, not a hard failure.
- `MaxDepth = 32` — bounds recursive parsing depth against a
  pathologically nested file.

`WorldSaveValidator` (new, narrow — Sim.WorldGeneration.Persistence)
checks only what's specific to the save envelope itself: `Version` is
supported, `Prompt` is present and within a generous-but-finite length
(`MaxPromptLength = 8000`), `Specification` is present, and
`Prompt`/`Seed` agree with `Specification.OriginalPrompt`/`.Seed`. It
then folds in a full `WorldSpecificationValidator` pass (Sim.WorldGeneration.
Validation, Phase 6) — the exact same validator every freshly-designed
specification already goes through, never duplicated or reimplemented —
and returns the (possibly repaired, per that validator's existing
repair-vs-reject policy) specification, never the raw one. **A save file
can never bypass `WorldSpecificationValidator`.**

## 4. Storage location

`WorldSaveService` (the real, file-backed `IWorldSaveService`) writes
under `Application.persistentDataPath/Saves/<slot>.json` — Unity's own
application-controlled, per-platform-appropriate storage location. Never
`Assets/`, `ProjectSettings/`, `Packages/`, or the repository root.

**Path traversal is prevented structurally, not by blacklisting `".."`**:
the slot name is checked against a strict allow-list
(`^[A-Za-z0-9_-]{1,64}$` — letters, digits, underscore, hyphen only) before
it ever becomes part of a path. No dot, no slash, no backslash, no tilde
can appear in a slot name at all — an absolute path, a `../` sequence, or
any path-separator-containing string is rejected outright, never
sanitized-and-used. The one default slot (`"default"`) always satisfies
this; the current UI never exposes a slot-name input at all (see §9), so
in normal use the allow-list is pure defense-in-depth — but it holds for
any future caller that does pass one.

Every file operation (`File.WriteAllText`/`ReadAllText`/`Delete`/`Exists`)
is wrapped against `IOException`/`UnauthorizedAccessException` and
reported as a clean `WorldSaveOperationResult`/`WorldLoadResult` failure
— never an uncaught exception.

## 5. Versioning

`WorldSaveData.Version` (currently `1`, `WorldSaveData.CurrentVersion`).
No migration framework: `WorldSaveValidator` rejects anything other than
the current version with a clear error, rather than silently
reinterpreting an incompatible shape. Bump `CurrentVersion` if
`WorldSaveData`'s own shape ever changes incompatibly.

## 6. Save/load runtime flow

```
SAVE:
CourseGameplayController's world is Ready
        ↓
WorldGenerationRuntimeService.SaveWorld()
        ↓ reads Controller.LastValidSpecification (already-validated, already in memory)
WorldSaveData.FromSpecification(...)
        ↓
IWorldSaveService.Save(...)
        ↓
Application.persistentDataPath/Saves/default.json

LOAD:
WorldGenerationRuntimeService.LoadWorld()
        ↓
IWorldSaveService.Load()  — read file → deserialize → WorldSaveValidator (incl. full WorldSpecificationValidator)
        ↓ (only on success)
WorldGenerationController.LoadWorld(validatedSpecification)
        ↓ ValidateAndGenerate — the SAME Validating -> Generating -> Ready/Failed tail
        ↓   GenerateWorldAsync uses, skipping Designing entirely
WorldGenerator.Generate(...)   — the existing generator, no second implementation
        ↓
GeneratedWorldResult -> WorldGenerationController reaches Ready
        ↓ (the existing StateChanged handler — unchanged from Phase 11/12/13)
drone spawn placement, course binding, recovery binding, result-seed tracking
```

**`WorldGenerationController.LoadWorld` structurally never calls
`IWorldDesigner`** — the Validating→Generating→Ready/Failed tail
(`GenerateWorldAsync` and `LoadWorld` both call the same private
`ValidateAndGenerate`) has no reference to the designer at all; only
`GenerateWorldAsync`'s own Designing step does. This is not a runtime
check, it's an absence of any code path — verified directly in
`WorldGenerationControllerTests.LoadWorld_NeverReachesDesigningState_
AndNeverCallsTheDesigner` with a spy `IWorldDesigner`.

**No second generation pipeline exists.** `LoadWorld` reuses the exact
same `WorldGenerator`/`WorldSeedManager`/`WorldSpecificationValidator`
instances `GenerateWorldAsync` already uses — the same
`WorldGenerationController`, not a parallel one.

**A successful load needs no new drone/course/recovery/results code at
all**: `WorldGenerationRuntimeService.HandleStateChanged` (Phase 11/12/13,
completely unmodified) already reacts to `Ready` regardless of *how* it
was reached — it reads `Controller.LastGeneratedWorld` and places the
drone, binds the course, binds recovery, and records the world seed for
results exactly as it does for a freshly generated world.

## 7. Course/race state after a load

Per this phase's explicit instruction, this is a save of the world
*definition*, not the live simulation. After a load: `CourseGameplayController`
is bound fresh (via the same `BindToCourse` call a fresh generation
already triggers) → `Waiting`, checkpoint index `0`, timer at zero,
`DroneRecoveryController` bound fresh to the loaded world's bounds/spawn,
recovery count `0`. The user presses Start to begin racing, exactly like
after any other generation. No lap/checkpoint/timer/recovery-cooldown
state survives a save/load round trip, by design.

## 8. Error handling

Every failure path returns a clean `Failed`/`false` result with a
message — never an uncaught exception, never a silent success. Handled
explicitly: no writable directory, serialization failure, invalid slot
name (path traversal/absolute path/separator-containing), missing save
file, corrupted JSON, an incompatible version, an invalid
`WorldSpecification` (any existing `WorldSpecificationValidator` error),
a mismatched Prompt/Seed between the envelope and the specification, file
read/write failure.

**Transactional load, as specified**: `WorldGenerationRuntimeService.
LoadWorld()` calls `IWorldSaveService.Load()` — which reads, deserializes,
and fully validates (including `WorldSpecificationValidator`) — **before**
`WorldGenerationController.LoadWorld` is ever called. A malformed/corrupt/
invalid save file therefore never reaches the point of touching the live
world at all: `WorldGenerationController`'s state is provably untouched on
that failure path (verified directly —
`LoadWorld_SaveServiceLoadFails_ReturnsErrorMessage_ControllerStateUntouched`).
Once validation has passed and `WorldGenerationController.LoadWorld` is
actually called, generation follows the exact same semantics a normal
regeneration already has: `WorldGenerator.Generate()` clears the previous
world before attempting the new one (Phase 8, unchanged), so if generation
itself then fails for some other reason (a scenario that should not occur
for a specification that already passed the same validator, but is not
provably impossible), the old world is not preserved — this is existing,
already-documented `WorldGenerator` behavior for *any* regeneration
failure, not a regression introduced by this phase. In practice, since a
save is only ever produced from a specification that already reached
`Ready` once before, this path is not expected to occur.

## 9. UI integration

`WorldGenerationUI` gained two optional buttons — **Save World**, **Load
World** — wired to `WorldGenerationRuntimeService.SaveWorld()`/`.LoadWorld()`
directly. No persistence logic lives in the UI: both handlers write
whatever message the service returns into the existing status label.
`Save`/`Load` availability follows the existing `WorldGenerationStatusFormatter`
pattern (`IsSaveAvailable` — only once a world is `Ready`;
`IsLoadAvailable` — whenever not busy designing/validating/generating).

A load's *eventual* outcome (Ready or Failed) is already reported by the
existing `StateChanged`-driven status text — no second status message
needed for that part. `LoadWorld()` returns a message only for a failure
that happens *before* the controller is ever involved (no save file,
corrupted/invalid save), since no state change will ever arrive to report
that case otherwise.

No auto-load on startup, no auto-save on any gameplay event — per this
phase's explicit instruction. The prompt field keeps whatever text was
last in it; nothing about Save/Load touches it.

## 10. Determinism

Loading never generates a new seed: `WorldSaveData.Seed` (mirroring
`Specification.Seed`) flows straight into the same `WorldGenerator.Generate
(validatedSpecification)` call a fresh generation uses, which constructs
its own `WorldSeedManager` from `specification.Seed` exactly as always.
The same saved specification + seed reproduces the same terrain/
environment/obstacle layout — verified directly (`LoadWorld_
SameSpecificationAndSeed_ProducesDeterministicTerrain` samples terrain
height at the same point after two independent loads of an identical
specification+seed and asserts equality). No second randomization
mechanism was introduced; `UnityEngine.Random`'s global state is never
touched by any of this phase's new code, matching every existing
generator.

## 11. Events / API surface

No new C# events. `IWorldSaveService`: `Save`, `Load`, `Delete`, `Exists`.
`WorldGenerationController`: one new method, `LoadWorld(WorldSpecification)`
— participates in the same single-flight cancellation semantics
`GenerateWorldAsync` already has (calling either cancels any attempt
already in flight). `WorldGenerationRuntimeService`: `SaveWorld()`/
`LoadWorld()`, both returning a short string message (or `null` from
`LoadWorld` when the state machine will report the rest itself).

## 12. Testing

**Automated tests written — not run.** No Unity Editor is available in
this environment (stated honestly, same as every prior phase). These are
real, checked-in EditMode tests a Unity Test Runner will execute:

- `WorldSaveDataTests` — `FromSpecification` always mirrors Prompt/Seed/
  Metadata from the given specification; null throws.
- `WorldSaveJsonSerializerTests` — round trip (prompt/seed/specification
  content/metadata/version); malformed/empty/null/literal-`null` JSON
  fails cleanly; unrecognized extra fields ignored, not a failure; `$type`
  injection remains inert (never resolved to a real type); script/SQL-
  injection-shaped strings remain inert data; 200-deep nested JSON fails
  cleanly via `MaxDepth`, no stack overflow.
- `WorldSaveValidatorTests` — valid data succeeds; unsupported version,
  missing/too-long prompt, missing specification, and a
  Prompt/Seed-vs-Specification mismatch all rejected; a real
  `WorldSpecificationValidator` failure (missing `OriginalPrompt`) is
  actually delegated to and rejected, not reimplemented; the *repaired*
  specification (not the raw one) is what's returned on success.
- `WorldSaveServiceTests` — real file I/O against an isolated temp
  directory created fresh per test (never the machine's real
  `Application.persistentDataPath`); round trip; writes land under the
  configured root, nowhere else; path-traversal (`../escape`,
  `../../escape`), absolute-path, and slash/backslash-containing slot
  names are all rejected (and provably don't escape the configured root);
  a valid alphanumeric slot name succeeds; a missing save file and a
  corrupted save file both fail cleanly without throwing; a save file
  that fails `WorldSpecificationValidator` fails cleanly; `Exists`/
  `Delete` behave correctly, including "nothing to delete."
- `WorldGenerationControllerTests` (extended) — `LoadWorld(null)` fails
  cleanly; a valid specification reaches `Ready` with a real generated
  world; the state sequence is exactly Validating→Generating→Ready (no
  Designing at all) and a spy `IWorldDesigner` is never called; an
  invalid specification reaches `Failed`/`ValidationFailed`; an
  unresolvable spawn reaches `Failed` with no stale `GeneratedWorld` in
  the scene; the same specification+seed loaded twice reproduces the same
  sampled terrain height; calling `LoadWorld` twice never leaves two
  `GeneratedWorld` roots.
- `WorldGenerationRuntimeServiceTests` (extended) — `SaveWorld`/`LoadWorld`
  against a fake, in-memory `IWorldSaveService` (no real file I/O at this
  layer — that's `WorldSaveServiceTests`' job): no save service configured
  → a message, never a throw; no generated world yet → a message, the
  save service is never called; after a real generation, `SaveWorld`
  forwards the actual generated specification; a save-service load
  failure never touches the controller's state at all (proving the
  transactional guarantee in §8); a successful load reaches `Ready` and
  places the drone at the loaded spawn, through the same handler a fresh
  generation already uses.

**No test in this phase touches a real network, the Anthropic API,
Reactor, or an API key** — grepped for after writing, to confirm.

**Unity file-I/O behaviour**: `Application.persistentDataPath` itself
(the real path resolution, not the file operations against it — those
are covered by `WorldSaveServiceTests` against an injected temp
directory) can only be confirmed correct inside a real Unity process.
Listed explicitly in the manual checklist below, not silently skipped.

## 13. Manual Unity test checklist

Nothing below has been run in a live Editor — none was available while
writing this phase, exactly as every prior phase has stated. This is what
to check by hand in Unity 2022.3 LTS:

1. Generate a world (e.g. the Himalayan example prompt).
2. Click **Save World** — confirm the status text shows a save-succeeded
   message.
3. Click **Clear World** — confirm the world disappears.
4. Click **Load World** — confirm the status text moves through
   "Validating world specification...", "Generating Unity world...",
   ending at "World ready — fly!" (the same text a fresh generation
   shows — this phase adds no separate loading message for that part).
5. Confirm the world regenerates (terrain/environment/obstacles visible).
6. Confirm the seed is the same (same terrain shape as the original
   generation — compare visually, or via a debug read of
   `WorldGenerationController.LastValidSpecification.Seed` if convenient).
7. Confirm the course/gates regenerate (same gate count/layout as before
   Clear).
8. Confirm the drone is placed at a valid spawn (not embedded in
   terrain/obstacles).
9. Click **Start Race** — confirm the race starts normally.
10. Confirm the Console shows no Anthropic/network log lines during step
    4 — only "Loading a saved world specification — no design/LLM step."
    and the existing Validating/Generating logs.
11. Generate a **different** world (a different prompt).
12. Click **Load World** again (the save from step 2 is still on disk;
    Save was not pressed again).
13. Confirm the *previous* (step-1) world is recreated, not the one from
    step 11.
14. Manually corrupt the save file (open
    `<persistentDataPath>/Saves/default.json` in a text editor, e.g.
    delete a `}` or replace it with garbage text) and click **Load
    World** again.
15. Confirm failure is clean: a clear "corrupted"/"failed validation"
    message, no crash, and the world from step 11/13 (whichever was
    current) is left exactly as it was.
16. This phase's UI does not expose a filename/slot-name input at all
    (see §9) — there is no in-UI path-traversal surface to test by hand;
    the rejection is verified at the code level (`WorldSaveServiceTests`)
    instead.
17. Inspect the repository working tree after all of the above (`git
    status`) — confirm no save file was written anywhere under the
    repository (it only ever goes to `Application.persistentDataPath`,
    outside the repo entirely).

## 14. Known limitations

- Single save slot only (`"default"`) — per this phase's explicit "if the
  design only needs one save slot, keep it simple" instruction. The
  underlying `IWorldSaveService`/`WorldSaveService` API already accepts an
  optional slot name for a future multi-slot UI, with the same
  path-traversal protection applying to any value.
- No slot-name UI at all — Save/Load always target the one default slot.
- No migration framework — an incompatible `Version` is rejected with a
  clear error, never reinterpreted.
- `Application.persistentDataPath`'s actual resolved location can only be
  confirmed inside a real Unity Editor/Player — see §13.
- No automated Play Mode test evidence — see §12/§13. This environment
  has no Unity Editor available to run one; nothing here claims
  otherwise.

## 15. Security considerations

- Save files are treated as untrusted input throughout: the same
  `TypeNameHandling.None`/`MetadataPropertyHandling.Ignore`/
  `MissingMemberHandling.Ignore`/`MaxDepth` protections already
  established for LLM output (Phase 7/10) apply identically here — no
  `$type`-based arbitrary type instantiation is possible.
- Path traversal is prevented by a strict allow-list on the slot name,
  not a blacklist — structurally immune, not pattern-matched against
  known-bad sequences.
- No secrets of any kind are ever part of `WorldSaveData` — no API key,
  no Reactor/Anthropic credential, no environment variable, no HTTP
  header. Grepped the full diff before committing to confirm.
- No SQL, no shell execution, no dynamic code execution, no
  reflection-based object creation anywhere in this phase's new code.
