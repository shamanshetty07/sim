# AI World Designer — Phase 7

## Why Reactor cannot currently provide the 3D world

Covered in full in `docs/REACTOR_TO_UNITY_ARCHITECTURE.md` (Phase 6.5) — the
short version: every model Reactor (reactor.inc) hosts, including
LingBot/LingBot World 2, is video-only. No mesh, point cloud, depth (in
practice), GLTF/USD/FBX export, or structured scene-state API exists
anywhere on the platform. That investigation also surfaced a second-order
finding this phase directly acts on: Reactor cannot hand back *any*
structured description of prompt intent either — so it was never going to
be able to supply the content decisions a Unity-native generator needs,
regardless of format.

## Why the LLM World Designer is now responsible for world intent

The project's core principle (docs/ARCHITECTURE.md §1) — "AI decides *what*
the world should contain, Unity decides *how* to build it" — needs an AI
that can actually do the first half. A general-purpose LLM (OpenAI, Claude,
a local model) can: given a rich natural-language prompt, produce
structured JSON describing a world. That's a fundamentally different
capability than what Reactor's hosted models offer (steerable video
generation), and it's the one this architecture actually needs.

The pipeline, replacing the Reactor-centric one from Phases 5-6:

```
User Prompt
    ↓
IWorldDesigner (AI World Design)
    ↓
WorldSpecification (raw, unvalidated)
    ↓
WorldSpecificationValidator
    ↓
WorldGenerator (Unity — not yet implemented)
    ↓
FPV Simulation
```

Layers stay strictly separate, per this phase's explicit instruction: AI
World Design (`Assets/Scripts/AI/WorldDesign/`) knows nothing about Unity
object creation; Validation (`Assets/Scripts/WorldGeneration/Validation/`)
knows nothing about LLM providers; Unity world generation (not yet built)
will only ever consume a validated `WorldSpecification`; UI/Drone/Camera
code has no dependency on any of this.

## The `IWorldDesigner` contract

```csharp
Task<WorldDesignOutcome> DesignWorldAsync(WorldDesignRequest request, CancellationToken cancellationToken = default);
```

`WorldDesignRequest` mirrors `WorldGenerationRequest`'s prompt-preservation
guarantee from Phase 5: the constructor refuses to build a request without
a non-empty `Prompt`, so there's no code path that silently drops or
reduces it. `Seed` and `Constraints` (a `WorldScale?` hint and an
`int? MaxObstacles` soft cap) are optional hints *alongside* the prompt,
never a replacement for it.

`WorldDesignOutcome` returns a **raw, unvalidated** `WorldSpecification` on
success — validation is deliberately a separate downstream step (see
"Layers stay strictly separate" above), not folded into the designer.
Failure is a `WorldDesignFailureReason` + message, never an uncaught
exception crossing this boundary — same non-throwing-outcome philosophy as
`WorldGenerationOutcome` (Phase 5/6), kept as its own parallel type because
this is a deliberately separate pipeline stage.

## `WorldSpecification`: what's new this phase

Added `CourseSpecification` (`Course` field) specifically because the brief
gave a concrete example of information a flat object-count model can't
express:

```json
"course": { "style": "technical_then_high_speed", "difficulty": "hard", "gates": 15 }
```

`CourseSpecification.Style`/`Difficulty` are free-form strings (same
reasoning as `TerrainSpecification.TerrainType` — an enum would foreclose
what the designer can express), `GateCount` is the *intended* count
(independent of however many `Obstacles` entries of type "gate" actually
get placed), and `SectionDescriptions` is an ordered list of free-form
phrases capturing a multi-section narrative ("technical and tight" →
"opens into a high-speed valley"). Deliberately not a fully structured
per-section model (bounds, per-section terrain, etc.) — that structure
isn't needed until `WorldGenerator` exists to consume it, and building it
prematurely would be exactly the kind of restrictive modeling this project
has avoided since Phase 5.

`WorldSpecificationValidator` was extended with a matching `ValidateCourse`
(null → default, empty style/difficulty → sensible default, out-of-range
`GateCount` → clamped) — same repair-vs-reject policy as every other field
(see the validator's own class remarks).

## Provider abstraction

```
IWorldDesigner
     ├── MockWorldDesigner            (real, deterministic — see below)
     └── LLMWorldDesigner              (real orchestration logic)
              └── ILLMClient           (the actual provider swap point)
                       ├── OpenAiLLMClient       (stub — no key configured)
                       ├── AnthropicLLMClient    (stub — no key configured)
                       └── LocalLLMClient        (stub — no endpoint configured)
```

**Design choice worth explaining:** the brief's illustrative architecture
showed `OpenAIWorldDesigner`/`ClaudeWorldDesigner`/`LocalLLMWorldDesigner`
as siblings directly implementing `IWorldDesigner`. Implemented instead as
one `LLMWorldDesigner` (all prompt-engineering, JSON-parsing, and
validation-readiness logic, written exactly once) plus a swappable
`ILLMClient` (just "send system+user text, get text back", the minimum
surface a provider actually differs on). Three parallel *Designer* classes
would each duplicate that logic for no benefit — changing how the schema is
described to the model, or how responses are parsed, would mean editing
three places instead of one. The requirement this satisfies — "the
architecture must allow OpenAI/Claude/local LLM/other providers without
changing Unity's world-generation code" — holds regardless of which shape
sits behind `IWorldDesigner`; nothing outside this folder can tell the
difference.

**Phase 10 update: `AnthropicLLMClient` is now a real integration**, not a
stub — see `docs/PHASE_10_REAL_LLM.md` for the full picture (structured
output via forced tool use, the shared `WorldSpecificationToolSchema`,
timeout/cancellation, error handling, security, testing). `OpenAiLLMClient`
and `LocalLLMClient` remain exactly as described below: honest
configuration-checked stubs, untouched this phase, per Phase 10's explicit
"implement one real provider, leave the others as stubs" instruction.

**`OpenAiLLMClient`/`LocalLLMClient` still make no real network call.** No
OpenAI or local-LLM credentials exist in this project's environment. Each
stub checks for configuration (`OPENAI_API_KEY` — genuinely standard,
used by OpenAI's own SDKs; `LOCAL_LLM_ENDPOINT` — this project's own
convention, flagged as such in its own doc-comment, since "local LLM" has
no single standard API) and throws `LLMNotConfiguredException` if absent.
If configured, each still returns a clean failure rather than attempting
an unverified call — per Phase 7's original "implement a real provider
only once its API is actually configured" instruction, which Phase 10
then acted on for Anthropic specifically. Each class's doc-comment states
exactly what the real HTTP call would look like so completing one is
filling in a documented gap, not research from scratch — mirroring
`OpenWorldReactorWorldGenerationService` before Phase 6, and
`AnthropicLLMClient` itself before Phase 10. These two stubs don't yet
share the dual-lookup credentials pattern (env var + local `.env.local`
file) Phase 10 generalized into `EnvironmentLlmCredentialsProvider` for
`AnthropicLLMClient` — adopting it is a reasonable next step once either
gets real credentials.

## JSON validation and the "never execute AI-generated code" boundary

`WorldSpecificationJsonParser` (`IWorldSpecificationJsonParser`) is the one
place LLM output text becomes a `WorldSpecification` object:

- Uses **Newtonsoft.Json** (`com.unity.nuget.newtonsoft-json`), not Unity's
  built-in `JsonUtility` — a deliberate, justified dependency:
  `JsonUtility` cannot deserialize into auto-implemented C# properties
  (only public fields), and every `WorldSpecification` model class uses
  properties. Newtonsoft is Unity's own officially-distributed package for
  exactly this gap.
- `TypeNameHandling.None` — set **explicitly**, not left as an implicit
  default, because it's the specific setting that prevents untrusted JSON
  from ever causing arbitrary .NET type instantiation (a `$type` field
  pointing at, say, `System.Diagnostics.Process` is simply inert data, not
  something the deserializer acts on). This must never change to
  Auto/All/Objects/Arrays — see the parser's own doc-comment for why in
  more detail. `MetadataPropertyHandling.Ignore` reinforces this: any
  `$type`/`$id`/`$ref` in the input is dropped, not interpreted.
- `MissingMemberHandling.Ignore` — an LLM response with an extra field we
  don't model isn't treated as a failure.
- `MaxDepth` (32) — bounds recursive parsing against pathologically nested
  input, a real DoS vector for any JSON deserializer given untrusted text.
- **The parser never executes anything.** It populates plain data
  properties on a closed set of known C# types. There is no `eval`, no
  reflection-based type resolution from a string, no shelling out. Tested
  directly (`WorldSpecificationJsonParserTests`) with both a `$type`-
  injection attempt and script/SQL-injection-shaped string content — both
  end up as inert data, never executed, never causing anything beyond a
  plain string field holding that literal text.
- `OriginalPrompt` and (when the request specifies one) `Seed` are always
  overwritten from the request after parsing, never trusted from the
  model's own JSON — identical rule to `ReactorWorldAdapter` (Phase 5) and
  for the same reason: the request is the single source of truth for what
  the user actually asked for and what reproducibility depends on.
- Markdown code fences (```` ```json ... ``` ````) around the response are
  stripped defensively — a common LLM habit even when told not to do it,
  not treated as a parse failure.

## Determinism

`MockWorldDesigner` is deterministic: an explicit `Seed` always wins; with
none given, the same prompt deterministically derives the same seed via
`Sim.Utilities.StableHash` (FNV-1a over UTF-8 bytes — unlike
`string.GetHashCode()`, which .NET does not guarantee stable across
processes). This is the same utility `MockWorldGenerationService` (Phase
6) uses, extracted this phase to avoid duplicating the hashing logic in
two places.

For the real `LLMWorldDesigner` path, `WorldSpecificationJsonParser`
enforces the request's seed the same way regardless of what the model
returns — so "same seed" is always honored at the application level even
though LLM *content* generation itself is not inherently deterministic
(a real provider given the same prompt and no seed may phrase/structure
its JSON differently between calls; that's a property of the model, not
something this layer can or should paper over).

## `MockWorldDesigner`: deliberately non-interpretive

Same reasoning as `MockWorldGenerationService` (Phase 5/6): it does **not**
parse or keyword-match the prompt — it always returns the same rich,
fully-populated example specification (populated Terrain,
EnvironmentObjects, 15 example gates, a `CourseSpecification` matching the
brief's own "technical_then_high_speed" example), with the prompt only
echoed into `Description`. A mock that varied its output by keyword would
be indistinguishable from the "hardcoded biome parser pretending to be AI"
architecture this project keeps being explicitly told to avoid — keeping
it honestly non-interpretive means its behavior can never be mistaken for
what real interpretation will do.

## Security boundaries — summary

| Concern | How it's addressed |
|---|---|
| AI-generated code execution | Impossible by construction — the parser only ever populates data properties on known types (`TypeNameHandling.None`); no `eval`, no dynamic type resolution from untrusted text. |
| Malformed/hostile JSON | Caught, converted to a clean `WorldDesignFailureReason.InvalidResponse` — never an uncaught exception. |
| Oversized/deeply-nested payloads | Bounded by `MaxDepth`; hard count/size limits are `WorldSpecificationValidator`'s job downstream (`WorldGenerationLimits`, Phase 6). |
| Prompt/seed spoofing by the model | `OriginalPrompt`/`Seed` always overwritten from the request, never trusted from the response. |
| Provider credentials | Phase 10: `AnthropicLLMClient` follows the same rules as Reactor's credentials (never in source/docs/logs, local `.env.local` file only, gitignored) via the new `EnvironmentLlmCredentialsProvider` — see `docs/PHASE_10_REAL_LLM.md`. No Anthropic key is actually configured in this project's environment as of Phase 10 — nothing to leak. OpenAI/local-LLM remain unconfigured stubs. |

## Future Reactor video integration

Reactor/LingBot's role is now explicitly optional and non-authoritative:

```
User Prompt
     │
     ├──→ LLM World Designer → WorldSpecification → Unity 3D World (authoritative)
     │
     └──→ Reactor/LingBot → optional visual/video experience (decorative only)
```

The existing Reactor integration code
(`Assets/Scripts/AI/{IWorldGenerationService,WorldGenerationOutcome,
MockWorldGenerationService,OpenWorldReactorWorldGenerationService,...}`,
`Assets/Scripts/WorldGeneration/{Models/ReactorWorldResult,
Adapters/ReactorWorldAdapter}`) is **kept, not deleted**, and remains fully
isolated from `Sim.AI.WorldDesign` — neither namespace references the
other. The Unity world generator (next real implementation phase) has no
dependency on Reactor and will not gain one. If Reactor's live video is
ever surfaced in Unity, it would be a rendered panel/backdrop layered on
top of the Unity-native, physically real world Unity itself builds — never
a source of collision geometry, and never required for the simulator to
function.

## What Phase 7 deliberately does not include

- No real LLM provider call (no OpenAI/Anthropic/local-LLM credentials
  exist in this environment — see the provider abstraction section).
  **Superseded Phase 10**: `AnthropicLLMClient` is now real — see
  `docs/PHASE_10_REAL_LLM.md`. OpenAI/local-LLM remain stubs.
- No `WorldGenerator` — still the next real implementation phase.
  **Built Phase 8** — see `docs/WORLD_GENERATION.md`.
- No orchestration/state-machine controller for `IWorldDesigner` analogous
  to `WorldGenerationController` (Phase 6) — not asked for this phase; a
  reasonable next step once a UI needs to drive Generate/Cancel/Retry
  against this pipeline specifically. **`WorldGenerationController` was
  extended to do exactly this in Phase 8/9.**
- No production/shipped-build credential flow for any LLM provider —
  whichever provider is configured first should follow the same
  Editor/dev-only pattern established for Reactor. **Still true as of
  Phase 10** — `EnvironmentLlmCredentialsProvider`'s `.env.local` fallback
  is explicitly Editor/local-dev only (see `docs/PHASE_10_REAL_LLM.md`).
