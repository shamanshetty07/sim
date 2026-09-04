# OpenWorld Reactor Integration — Phase 6

## Status, up front

**Real OpenWorld Reactor integration pending full API/SDK access — but not
blind guesswork either.** This phase found and used Reactor's real public
documentation, and verified a real API key against the real API. What
follows is exactly what was and wasn't confirmed, and exactly what's needed
to go further.

## Identifying the real product

Nothing in this repository's environment (checked again in Phase 6: env
vars, CLI tools, npm/pip/gem packages, config paths) has ever contained an
OpenWorld Reactor SDK. What changed this phase is that a real API key was
provided, which made web research worthwhile. Search results identified:

- **Reactor** (reactor.inc) — "the developer platform for real-time world
  models," publicly documented at **docs.reactor.inc**, recently emerged
  from stealth with $59M in funding for "the platform for real-time AI
  worlds."
- Its model catalog includes **LingBot** and **LingBot World 2** —
  real-time navigable world models by **Ant Group**, hosted on Reactor.

This is an exact match for the project brief's original "Reactor Lingbot"
naming, later clarified to "OpenWorld Reactor." High confidence this is the
intended backend, further confirmed by the API key working (below).

## What was actually verified (real, not simulated)

1. **Authentication schema**, from `docs.reactor.inc/authentication.md`:
   - `POST https://api.reactor.inc/tokens`
   - Header: `Reactor-API-Key: rk_...` (**not** `Authorization: Bearer`)
   - Body: `{"authorization_details":[{"type":"session","resources":{"models":{"match":["<model>"]}},"constraints":{"max_sessions":N,"max_session_duration_seconds":N}}],"expires_after":N}`
   - Response: `{"jwt": "...", "expires_at": <unix_epoch>}`
   - Session-scoped tokens live 1 hour by default, up to 6 hours via `expires_after`.
2. **A real, live test call succeeded.** Using the API key provided this
   session, a scoped token request (`max_sessions: 1`,
   `max_session_duration_seconds: 60`, `expires_after: 300`, scoped to
   `reactor/lingbot-world-2`) returned **HTTP 200** with a valid JWT. The
   key is confirmed live and working. (The JWT itself was discarded
   immediately after — it's a 5-minute-lived, single-session-scoped token,
   not something worth retaining.)
3. **No Unity/C# SDK exists.** Reactor's official SDKs are
   `@reactor-team/js-sdk` (JavaScript/TypeScript, with React bindings) and
   `reactor-sdk` (Python). Nothing for Unity or C#.
4. **The actual generation model is fundamentally not a "prompt in,
   structured world out" service.** LingBot World 2 is a **live, steerable
   video stream**: you open a session, upload a seed image, `set_prompt`,
   `start`, then continuously drive it with WASD (movement) + arrow keys
   (look) + `set_camera_pose` (directed moves), receiving a real-time video
   track at 48fps, 1664×960. Prompts can be hot-swapped mid-stream
   (`set_prompt` again — "the change lands on the next chunk"). There is
   **no seed/determinism parameter documented**, and **no static
   export/save** capability — it's video, not a scene description or asset.
5. **The transport isn't fully documented** in what was fetched. Event
   names (`trackReceived`, media tracks) strongly suggest WebRTC, but this
   wasn't explicitly confirmed, and there's no official low-level protocol
   spec to implement against directly (only the JS/Python SDK abstracts it).
6. **"Never put the API key in client-side code"** is Reactor's own
   explicit guidance — the token-minting call is meant to happen
   server-side, with only the short-lived JWT reaching a client.

## The architectural problem this creates

The project's pipeline (`WorldGenerationRequest` → `IWorldGenerationService`
→ `ReactorWorldResult` → adapter → `WorldSpecification` → Unity builds a
Rigidbody-physics scene) assumes a **one-shot request/response** shape:
submit a prompt, get back a finished (or eventually-finished) description
of a world. LingBot World 2's real shape is a **persistent, continuously-
steered session** that produces video, not a world description Unity's
procedural generator could consume to build collidable geometry. These are
not compatible without a fundamental rethink of what "the FPV camera feed"
even is (a Unity-rendered Rigidbody scene, vs. a live neural video stream
steered by the same WASD/camera input the drone already produces).

## Decision made with the user (this phase)

Presented these findings and asked how to scope the live-session/video
integration. Decision: **defer it.** This phase ships the verified real
authentication path and everything else Phase 6 asked for (Mock
enhancements, real validation logic, structured errors, state model,
cancellation, tests, docs) against the existing one-shot interface shape.
The live-session/streaming integration — and whether it even belongs behind
`IWorldGenerationService` as currently shaped, or needs a different
interface entirely — is an explicit, clearly-scoped open decision, not
something guessed at here. Two directions were identified and left for a
future decision:

- **A companion bridge process** (Node/Python, using Reactor's real SDK)
  that Unity talks to locally — architecturally the supported path (matches
  "never ship the API key to the client" and uses an SDK that actually
  exists), at the cost of a second runtime/process dependency.
- **A native C# client** hand-rolled against Reactor's wire protocol — no
  official support, transport not fully documented format, higher risk of
  building something wrong.

## What `OpenWorldReactorWorldGenerationService` actually does now

```
GenerateWorldAsync(request)
  -> credentials present? no  -> WorldGenerationOutcome.Failed(NotConfigured)
  -> credentials present? yes -> MintSessionTokenAsync(model)   [REAL network call]
       -> succeeds -> WorldGenerationOutcome.Failed(NotImplemented, "...see this doc")
       -> fails    -> WorldGenerationOutcome.Failed(NetworkError | Unavailable)
       -> cancelled -> WorldGenerationOutcome.Failed(Cancelled)
```

`MintSessionTokenAsync` is a genuine `UnityWebRequest` call against the
verified real endpoint/header/schema above — not a simulation. It exists
both as the first real step of `GenerateWorldAsync` and as a standalone
public method usable as a lightweight "is Reactor configured and reachable"
connectivity check. It is **never exercised by automated EditMode tests**
(see "Testing" below) — it was manually verified once, as described above,
using the credential in `.env.local`.

`GenerateWorldAsync` never throws for an expected failure — every case above
is a `WorldGenerationOutcome`, per the project's "no uncontrolled exceptions
crossing this boundary" rule. `MintSessionTokenAsync` and the lower-level
`ReactorNotConfiguredException`/`ReactorApiException` types do throw — they
sit one level down, aimed at code that calls them directly.

## Credentials — how they're stored and used

**Security requirements this phase followed exactly:**

- The API key is **not** in any C# source file, any Unity asset, any doc,
  any log statement, or the git history.
- It lives in **`.env.local`** at the repository root (sibling to `Assets/`,
  never under it — so it's never a Unity asset), file permissions `600`.
  Already covered by the existing `.gitignore` (`.env.local` pattern from
  Phase 2) — verified with `git check-ignore -v .env.local` before writing
  anything else this phase.
- `EnvironmentReactorCredentialsProvider` reads it (falling back to the OS
  environment variable `OPENWORLD_REACTOR_API_KEY` first, since a real env
  var — if actually set — is checked before the file). The file mechanism
  is the practical default because a Unity Editor launched from
  Finder/Dock on macOS does not reliably inherit shell environment
  variables — a well-known platform quirk that would otherwise silently
  leave Reactor unconfigured for most Editor users.
- Every git commit in this phase was preceded by `git status`/`git diff`
  and a grep across the diff and full history for the key value and common
  secret patterns before pushing.
- `IReactorCredentialsProvider` is injectable specifically so automated
  tests never depend on (or accidentally exercise) whatever's really in
  `.env.local` on the machine running them — see "Testing" below.
- Config variable names used: `OPENWORLD_REACTOR_API_KEY` (real,
  required — the `Reactor-API-Key` header value) and
  `OPENWORLD_REACTOR_MODEL` (this project's own convenience for "which
  model to target," defaulting to `reactor/lingbot-world-2` if unset — not
  a Reactor-documented variable name, just a local config choice). No
  `OPENWORLD_REACTOR_ENDPOINT` variable was added: the real endpoint
  (`https://api.reactor.inc/tokens`) is a fixed, documented constant, not
  something Reactor's docs present as configurable — inventing an
  "endpoint" env var and presenting it as required would have been exactly
  the "don't invent configuration names and pretend they're official"
  mistake this phase was told to avoid.
- This mechanism is **Editor/development-only**. `Application.dataPath` in
  a built player points inside the build output, not this source
  repository — the `.env.local` fallback doesn't apply there, and
  deliberately isn't relied on for one. A shipped build needs a
  server-mediated credential flow (matching Reactor's own "never ship the
  API key to the client" guidance), which is out of scope until there's an
  actual shipped-build story to design it against.

## Validation (`WorldSpecificationValidator`)

First real validation logic (Phase 5 shipped only data contracts). Policy:
repair what's safely repairable (null nested objects → fresh defaults,
NaN/Infinity/out-of-range numbers → clamped or substituted, unrecognized-
but-well-formed strings → left alone and flagged as a Warning), reserve
Error for what genuinely can't be repaired (a null specification, or a
missing `OriginalPrompt` — the latter treated as a real bug upstream, not
something to paper over by fabricating a prompt). Full reasoning and the
complete list of checks are in the class doc-comment on
`WorldSpecificationValidator.cs`; limits live in `WorldGenerationLimits.cs`.

## State model (`WorldGenerationController`)

`Assets/Scripts/Core/WorldGenerationController.cs` is the
`GenerateWorld(prompt)` / `Cancel()` entry point a future UI (Phase 8) uses
without knowing Reactor, Mock, the adapter, or the validator exist. It owns
the `Idle → Requesting → Validating → Completed/Failed/Cancelled` state
machine, exposes it via a `StateChanged` event plus `State`/
`LastValidSpecification`/`LastErrorMessage`/`LastFailureReason`, and guards
every state-mutating branch against a stale, already-superseded call (the
user re-clicking Generate before a previous attempt finished must not let
that older attempt's late-arriving result overwrite the newer one's).

## Error handling

Structured via `WorldGenerationFailureReason` (an enum on the non-throwing
`WorldGenerationOutcome`) rather than a proliferation of exception types —
see that enum's doc-comment for the full mapping against the brief's named
error categories (`OpenWorldReactorNotConfigured` → `NotConfigured`,
`OpenWorldReactorUnavailable` → `Unavailable`, `GenerationTimeout` →
`Timeout`, `InvalidGenerationResult` → `InvalidResponse`, `ValidationFailed`
→ `ValidationFailed`, `GenerationCancelled` → `Cancelled`). UI-facing
messages (`LastErrorMessage`) stay generic ("World generation failed.");
full detail goes to `Debug.LogError`/`Debug.LogWarning` only, and never
includes the API key, JWT, or request headers — see `ReactorApiException`'s
remarks for why its message is safe to log.

## Logging

`[WorldGeneration]` prefix throughout, matching the brief's example format:
prompt received, provider name, generation started/completed (with
duration), validation passed/failed, cancellation. Never logs the API key,
JWT, or any request header.

## Mock service

`MockWorldGenerationService` is unchanged in spirit from Phase 5 (still
deliberately non-interpretive — it does not parse the prompt to decide what
to generate) but now actually fulfills the "simulate the contract" brief:
deterministic given a seed (or a stable hash of the prompt when no seed is
given — FNV-1a, not `string.GetHashCode()`, which .NET does not guarantee
stable across runs), an opt-in `SimulatedDelayMilliseconds` for exercising
async/loading-state UI, and honors cancellation during that delay.

## Testing

Per instruction, **no EditMode test requires live Reactor credentials or
network access.** This mattered concretely this phase: a real `.env.local`
now exists on the machine these tests were written on, so
`OpenWorldReactorWorldGenerationServiceTests` injects a fake
`IReactorCredentialsProvider` (always reports "no credentials") rather than
relying on `EnvironmentReactorCredentialsProvider`'s real lookup — the
suite's result must be identical regardless of what's really configured on
whatever machine runs it. The one real network call this phase made (the
token-mint test described above) was run manually via `curl`, once, outside
the test suite, with its output redacted before being shown to the user.

## What's still explicitly not built

- Any live LingBot World 2 session (image upload, `set_prompt`, `start`,
  steering, receiving video) — the deferred piece, see above.
- Any Unity-side rendering of a Reactor video stream.
- `WorldGenerator`/terrain/environment/obstacle generation — Phase 7+,
  unrelated to this phase's Reactor findings.
- A production/shipped-build credential flow — Editor/dev-only for now.

## What's needed from the user to go further

1. A decision on the live-session integration direction (bridge process vs.
   native client vs. something else) — whichever is chosen is a real scope
   commitment beyond "add a service."
2. If a bridge-process direction is chosen: confirmation a Node.js or
   Python runtime alongside Unity is acceptable for this project.
3. Ideally, direct confirmation from Reactor's dashboard/account of which
   model(s) the key is scoped/billed for, and reactor.inc's stance on
   Editor-time (non-shipped) use of the raw key for local development —
   this document's assumption (that's acceptable for local dev, not for a
   shipped build) is reasonable but not something their docs explicitly
   addressed in what was fetched.
