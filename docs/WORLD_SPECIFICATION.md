# World Specification & OpenWorld Reactor Integration — Phase 5

## The pipeline

```
User Prompt (complete natural-language text)
        │
        ▼
WorldGenerationRequest        prompt preserved verbatim, + seed/scale hints,
                               + optional PreviousSpecification for "regenerate
                               with a tweak"
        │
        ▼
IWorldGenerationService        provider-agnostic. Implementations:
                               MockWorldGenerationService (honest placeholder,
                               does not interpret the prompt — see below)
                               OpenWorldReactorWorldGenerationService (stub —
                               throws ReactorNotConfiguredException; no real
                               SDK/API access exists in this environment)
        │
        ▼
WorldGenerationOutcome         success/failure wrapper
        │ (on success)
        ▼
ReactorWorldResult             the backend's OWN result shape — not assumed to
                               already look like WorldSpecification. Carries a
                               PayloadKind (Unknown / StructuredData /
                               NativeSceneReference) plus whichever payload
                               field is meaningful for that kind
        │
        ▼
ReactorWorldAdapter             translates ReactorWorldResult -> WorldSpecification.
                               Phase 5: maps only the fields safe to interpret
                               regardless of backend (name/description/seed/
                               metadata/prompt); does not yet parse a
                               structured payload or resolve a native asset
                               reference — there is no real payload shape to
                               write that logic against yet
        │
        ▼
WorldSpecification              Unity's own normalized, bounded contract
        │
        ▼
WorldSpecificationValidator      NOT YET IMPLEMENTED (data contracts —
                               ValidationResult/ValidationError — exist;
                               the actual limits/repair logic is later work)
        │
        ▼
WorldGenerator (Unity)           NOT YET IMPLEMENTED (Phase 7+)
        │
        ▼
Playable FPV world
```

## Why the prompt is a first-class, preserved value

Earlier design language (Phase 1-2) described the pipeline as "prompt →
biome parser → fixed procedural generator." That was wrong, and this phase
corrects it structurally, not just in wording:

- `WorldGenerationRequest`'s constructor **requires** a non-empty prompt —
  there is no code path that constructs a request without one.
- The prompt is carried as `WorldGenerationRequest.Prompt` all the way to
  `WorldSpecification.OriginalPrompt` — every `WorldSpecification` that
  exists has the prompt that produced it attached, not just a derived
  biome/terrain-type string.
- `WorldGenerationMetadata.RequestId` ties a `ReactorWorldResult` (and
  therefore the `WorldSpecification` built from it) back to the exact
  request/prompt, so this traceability survives save/load too (Phase
  12/13).

Nothing between the UI and the backend call is allowed to reduce the prompt
to a fixed parameter set. `WorldGenerationRequest.RequestedScale` and
`Seed` are *hints alongside* the prompt, not a replacement for it — the
backend is expected to read the prompt itself for everything else.

## Why `ReactorWorldResult` exists as a separate type from `WorldSpecification`

This was the central open design question this phase had to make a
provisional call on. The three options considered (as posed to the
implementer):

**A.** `WorldSpecification` represents high-level generation *intent*; a
separate model represents Reactor's actual output; an adapter bridges them.

**B.** `WorldSpecification` *is* the normalized description Reactor
returns directly — no separate native-result type.

**C.** Both, as genuinely separate concerns.

**Decision: Option A (closest to C in practice).** `ReactorWorldResult` is
the backend-native envelope; `ReactorWorldAdapter` converts it to
`WorldSpecification`. Reasoning:

- **No real Reactor access exists to confirm Option B is even possible.**
  This environment has no OpenWorld Reactor SDK, API, documentation, or
  configuration (checked: environment variables, CLI tools, installed
  packages, common config paths — all empty). Assuming Reactor's native
  output already matches a Unity-specific normalized shape would be
  inventing an API, which this phase was explicitly told not to do.
- **Reactor may not return "a description of a world" at all.** The task
  brief is explicit that it "may generate or provide an actual world
  representation" — i.e. it might hand back something closer to a
  scene/asset/mesh reference than a JSON description. `WorldSpecification`
  (trees/rocks/gates as structured lists) cannot represent that; forcing it
  to try would be the "restrictive replacement for the actual generated
  world" this phase was told to avoid. `ReactorWorldResult.PayloadKind =
  NativeSceneReference` plus `NativeAssetReference` exists specifically so
  that possibility isn't designed away.
- **A separate envelope costs little and loses nothing.** If Reactor turns
  out to return exactly what `WorldSpecification` needs, `ReactorWorldAdapter`
  degenerates into a close-to-1:1 mapper — cheap to write once the real
  shape is known. If it returns richer content, having kept the two types
  separate means that richness (via `StructuredPayloadJson` /
  `NativeAssetReference`) isn't already lost by the time anyone tries to use
  it.

**This is provisional, not final.** Once real OpenWorld Reactor access
exists, re-examine this decision against its actual capabilities — it may
turn out Option B (or a variant) is a better fit. See "Open questions"
below for exactly what would need to be known to make that call for real.

## What Unity owns vs. what OpenWorld Reactor owns

| | Owns |
|---|---|
| **OpenWorld Reactor** | Interpreting the prompt. Deciding what the world *should contain* — biome, layout, object placement intent, obstacle count/placement intent, mood/weather/lighting intent. Whatever creative/generative reasoning turns a sentence into a world design. |
| **Unity (this project)** | Deciding *how* to actually build it: terrain meshing, prefab/primitive placement, physics colliders, deterministic seeding, ensuring the result is *flyable* (see "FPV suitability" below), rendering, save/load, and — critically — **never executing anything Reactor sends** as code. `WorldGenerator`/`TerrainGenerator`/`ObstacleGenerator`/etc. (Phase 7+) are the only things that call `GameObject.Instantiate`, `AddComponent`, `Rigidbody` APIs, or `SceneManagement`. |
| **The Adapter (`ReactorWorldAdapter`)** | The seam between the two. Everything Reactor-native (whatever shape that turns out to be) is translated here into Unity's normalized `WorldSpecification` before anything downstream touches it. |
| **The Validator (data contracts exist; logic doesn't yet)** | Bounding what Unity will actually attempt to build — hard limits on counts/sizes, repairing recoverable issues, rejecting unrecoverable ones — regardless of whether the excess/invalid data came from Reactor or a bug in the adapter. |

This is the same "AI decides *what*, Unity decides *how*" principle from
`docs/ARCHITECTURE.md` §1, now spelled out concretely against a named
backend instead of a generic "the AI".

## FPV suitability is Unity's responsibility, driven by the prompt's intent

Regardless of what OpenWorld Reactor returns, the generated world must
still be flyable: navigable space, collision, a safe spawn, obstacles that
make sense, a playable flight path. This project's job is to make sure
*that* is always true — but *what kind* of flyable space it should be is
still driven by the prompt, via `FlightCharacteristics`:

- "Create a tight technical FPV race through a dense forest" implies high
  `TightnessScore01`, `PreferredStyle = Technical` or `Race`, high
  `ObstacleDensity01`.
- "Create an open desert environment for high-speed FPV flying" implies low
  `TightnessScore01`, `PreferredStyle = Cruise`, low `ObstacleDensity01`.

These two prompts must not resolve to the same template. `FlightCharacteristics`
exists as its own model (separate from `TerrainSpecification`) specifically
so flight intent isn't accidentally coupled to biome — a "technical desert
canyon race" and a "sprawling open forest cruise" are both coherent prompts
that cross those two axes independently.

**Not yet implemented**: anything that actually *reads* `FlightCharacteristics`
to change generation behavior — that's `WorldGenerator`/`ObstacleGenerator`
(Phase 7/9/10) once they exist. This phase only establishes that the data
exists and is structured to make that possible without a redesign later.

## Why several model fields are strings, not enums

`TerrainSpecification.TerrainType`, `WeatherSpecification.Type`,
`ObjectSpecification.Category`, and `ObstacleSpecification.Type` are all
free-form strings rather than fixed enums. This is deliberate: an enum
would be exactly the "restrictive replacement" this phase was told to
avoid — it would mean OpenWorld Reactor (or the adapter, or a future
richer Mock) could only ever express a category/type decided on in advance
by this codebase. The trade-off is explicit: type-safety and validity
checking for these fields is the Validator's job (an allow-list, or a
documented fallback for an unrecognized value), not the C# type system's.
Phase 9's environment generator (`PrefabRegistry`) already has a fallback
mechanism for this — an unrecognized category gets a primitive placeholder
rather than being rejected.

## Open questions (need real OpenWorld Reactor access to answer)

Everything in this list is a genuine unknown, not a placeholder decision —
answering these is what turns `OpenWorldReactorWorldGenerationService` from
a stub into a real integration:

- How is a prompt actually submitted? A REST API? A native SDK/plugin
  invoked from C#? A local process? Something else?
- Is generation local (on-device) or remote (networked)? If remote, what's
  the endpoint/auth model? (`REACTOR_API_KEY`/`REACTOR_ENDPOINT`/
  `REACTOR_MODEL` in `OpenWorldReactorWorldGenerationService` are
  placeholder names, not confirmed against any real contract.)
- Does it return structured world data, an actual scene/asset/mesh
  representation, or both? If structured, in what schema?
- Does it support streaming (progressive generation) or is it request/response?
- Does it accept a seed, and if so, does the *same* prompt + seed reproduce
  the *same* result? (`ReactorWorldResult.IsDeterministic` exists to record
  this once it's known — deterministic regeneration is a hard product
  requirement, not something to assume true.)
- What metadata does it return alongside a result (generation time, model/
  version identifiers, warnings)?
- If it returns a native scene/asset representation, what's the intended
  Unity-side integration mechanism — an importer, a runtime loader, a
  streaming API?

Until these are answered, `OpenWorldReactorWorldGenerationService` remains
a documented stub, and `ReactorWorldAdapter` remains limited to the fields
it can safely populate without guessing at a payload shape.

## What Phase 5 deliberately does not include

- No parsing of `ReactorWorldResult.StructuredPayloadJson` — no real or
  deliberately-designed-mock shape exists to parse against yet.
- No `WorldSpecificationValidator` logic — only its data contracts
  (`ValidationResult`, `ValidationError`).
- No `WorldGenerator`/terrain/environment/obstacle generation — Phase 7+.
- No real OpenWorld Reactor integration — blocked on SDK/API access this
  environment does not have.
- `MockWorldGenerationService` is intentionally minimal (one static
  example, does not interpret the prompt) — a mock actually useful for
  development (multiple examples, simple prompt-aware selection for
  testing convenience) is Phase 6 scope, built against this same contract.
