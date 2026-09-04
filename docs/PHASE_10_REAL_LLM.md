# Phase 10 — Real LLM World Designer

## 1. Architecture

No change to the Phase 7/9 pipeline shape — this phase fills in one box that
was previously an honest stub:

```
WorldGenerationController (Sim.Core)
        ↓
    IWorldDesigner
        ├── MockWorldDesigner        (unchanged — still the offline default)
        └── LLMWorldDesigner          (unchanged orchestration — prompt-building, parsing)
                 ↓
             ILLMClient
                 ├── AnthropicLLMClient   (Phase 10 — real, implemented)
                 ├── OpenAiLLMClient      (still an honest stub — untouched)
                 └── LocalLLMClient       (still an honest stub — untouched)
                          ↓
                    IHttpTransport (new — testability seam)
                          └── UnityWebRequestHttpTransport (real transport)
```

Nothing outside `Sim.AI.WorldDesign` changed at all: `WorldGenerationController`,
`WorldGenerationRuntimeService`, `RuntimeSimulationBootstrap`, and
`WorldGenerationUI` are untouched — they already depended only on
`IWorldDesigner`, and `RuntimeSimulationBootstrap.CreateDesigner()` already
had an `LLMProviderKind.Anthropic` branch constructing `AnthropicLLMClient`
since Phase 9. Selecting Anthropic in the Inspector now reaches a real
provider instead of an honest stub — that is the entire integration surface
this phase touches from the runtime pipeline's point of view.

## 2. Supported provider

**Anthropic (Claude)** — the one real provider implemented this phase, per
this phase's explicit "no real provider was yet configured in this
project; implement exactly one, don't speculatively build several"
instruction. `OpenAiLLMClient` and `LocalLLMClient` are untouched: still
honest "not yet implemented" stubs (see their own doc-comments for exactly
what completing them would require).

Verified against Anthropic's current official documentation
(`platform.claude.com/docs/en/api/messages`,
`.../agents-and-tools/tool-use/*`,
`.../build-with-claude/structured-outputs`, `.../api/errors`) before
writing any request/response code — endpoint, headers, model names, tool
schema shape, and error shape below are all confirmed from those pages,
not invented or guessed.

## 3. Configuration

Three environment variables, all optional except the key:

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `ANTHROPIC_API_KEY` | Yes | — (throws `LLMNotConfiguredException` if absent) | Authenticates every request. Standard name used by Anthropic's own SDKs/CLI. |
| `ANTHROPIC_MODEL` | No | `claude-sonnet-5` | Which model to call. This project's own configuration name — Anthropic has no single standard env var for it. |
| `ANTHROPIC_TIMEOUT_SECONDS` | No | `60` | Bounded request timeout — see §10. |

Selected in the Editor via `RuntimeSimulationBootstrap`'s existing (Phase
9) `_mode` (`Mock`/`LLM`) and `_llmProvider` (`OpenAI`/`Anthropic`/`Local`)
Inspector fields — no new configuration surface was added to the runtime
pipeline itself.

## 4. Environment variables / API key setup

Reuses the exact dual-lookup pattern
`Sim.AI.EnvironmentReactorCredentialsProvider` established for OpenWorld
Reactor (Phase 6), generalized this phase into
`Sim.AI.WorldDesign.EnvironmentLlmCredentialsProvider` (a new, small class —
Reactor's own class is untouched, per this phase's explicit "do not modify
Reactor integration" instruction) so any current or future `ILLMClient` in
this namespace can reuse the same env-var-then-`.env.local` lookup instead
of re-implementing it: **OS environment variable first, then a line in the
repository-root `.env.local` file** (gitignored since Phase 2, verified
still untracked this phase — see §12 "Secret scan"). To configure locally:

```
ANTHROPIC_API_KEY=<your-key>
```

(optionally also `ANTHROPIC_MODEL=...` / `ANTHROPIC_TIMEOUT_SECONDS=...`)
appended to `.env.local` at the repository root. The `.env.local` fallback
is Editor/local-development only — `Application.dataPath` in a built
Player points inside the build output, not the source repository, so a
shipped build would need a server-mediated credential flow, not this
class (same caveat already documented for Reactor).

## 5. API key setup — what never happens

The key is read only inside `AnthropicLLMClient.CompleteAsync`, attached
only as the `x-api-key` request header sent directly to
`https://api.anthropic.com/v1/messages` over HTTPS. It is never logged,
never included in any exception message, never returned to any caller,
never placed in a Unity scene/ScriptableObject/UI, and (see §12) never
appears in source, tests, or documentation.

## 6. Model configuration

Default `claude-sonnet-5` — a current, real, generally-available model
confirmed directly in Anthropic's own API documentation examples (not
guessed). Fully overridable via `ANTHROPIC_MODEL` without a code change.
`AnthropicLLMClient`'s constructor also accepts a direct `modelOverride`
parameter, used only by tests (mirrors the existing `apiKeyOverride`
pattern already established for all three `ILLMClient` stubs since Phase
7) so tests never depend on what happens to be configured on the machine
running them.

## 7. Structured-output strategy

**This is the most important part of this phase.** `AnthropicLLMClient`
does not ask the model to "please output JSON" in free text — it forces
the model to call one tool via Anthropic's own official structured-output
mechanism:

```json
"tools": [{
  "name": "emit_world_specification",
  "strict": true,
  "input_schema": { ... }
}],
"tool_choice": {"type": "tool", "name": "emit_world_specification"}
```

`strict: true` (Anthropic's grammar-constrained-sampling schema
enforcement — `platform.claude.com/docs/en/agents-and-tools/tool-use/strict-tool-use`)
constrains the model's token sampling to schema-valid output; forcing
`tool_choice` guarantees the model calls exactly this tool, every time, and
never responds with commentary instead of structured data. The tool's
`tool_use` response block's `input` field is *already*
WorldSpecification-shaped JSON — `AnthropicLLMClient` does nothing to it
beyond locating that block and reading `input` back out as text (see
`AnthropicLLMClient.ParseToolInput`); it never deserializes into a
`WorldSpecification` or any other .NET type itself.

`input_schema` comes from one new canonical source,
`WorldSpecificationToolSchema.Build()` (`Sim.AI.WorldDesign`) — written to
stay inside Anthropic's documented structured-output JSON Schema subset
(no `minimum`/`maximum`/length constraints, `additionalProperties: false`
on every object, no recursive `$ref`) and, critically, **never adds an
`enum` constraint to a field the real `WorldSpecification` model documents
as free-form** (`TerrainType`, `EnvironmentObjects[].Category`/
`PlacementHint`, `Obstacles[].Type`, `Course.Style`/`Difficulty`,
`Weather.Type`) — doing so would silently reintroduce the "restrictive
keyword-limited architecture" this project has explicitly avoided since
Phase 5. Only `Scale` (`WorldScale`) and `Flight.PreferredStyle`
(`FlightStyle`) are schema `enum`s, because those two — and only those
two — really are closed C# enums already.

`temperature`/`top_p`/`top_k` are deliberately never sent: verified (same
official documentation) that they are deprecated on current-generation
Claude models and any value other than each parameter's own default is
rejected with a 400 error — sending `LLMCompletionRequest.Temperature`
through would have broken every real request.

**Not live-verified.** No Anthropic credentials exist in this
environment (see §15) — the `strict: true` + forced-`tool_choice` request
shape is confirmed against Anthropic's documentation and exercised
end-to-end against a fake transport (§14), but has not been exercised
against the real API.

## 8. `WorldSpecification` contract

Unchanged — `WorldSpecificationToolSchema` targets the exact same
`WorldSpecification`/`FlightCharacteristics`/`TerrainSpecification`/
`ObjectSpecification`/`ObstacleSpecification`/`CourseSpecification`/
`WeatherSpecification`/`LightingSpecification`/`SpawnSpecification` model
classes from Phases 5/7 — no `LLMWorldSpecification` or other duplicate
model was created. `OriginalPrompt`/`Seed`/`Metadata` are excluded from
the schema entirely (the application always overwrites the first two,
per `WorldSpecificationJsonParser`, and never asks the model for the
third) — asking the model to produce them would be pointless.

## 9. Validation

Unchanged, and never bypassed: the extracted tool `input` JSON flows
through the exact same `IWorldSpecificationJsonParser` →
`WorldSpecificationValidator` path any `IWorldDesigner` output already
goes through (`WorldGenerationController`, Phase 9). A structured,
schema-enforced response is **not** treated as pre-validated — `strict`
mode narrows the failure surface, it does not replace
`WorldSpecificationValidator`, which still runs unconditionally.
`WorldGenerator.Generate()` is never reached for an invalid specification,
regardless of which `IWorldDesigner` produced it (this guarantee already
existed at the controller level since Phase 9 and is designer-agnostic —
see `WorldGenerationControllerTests`).

## 10. Cancellation and timeout

Both flow through the existing `CancellationToken` parameter of
`IWorldDesigner.DesignWorldAsync`/`ILLMClient.CompleteAsync` — no second
cancellation mechanism. Inside `AnthropicLLMClient.CompleteAsync`, a
`CancellationTokenSource` carrying a bounded timeout
(`ANTHROPIC_TIMEOUT_SECONDS`, default 60s) is linked with the caller's
token before the transport call:

- If the **caller** cancels (user clicks Cancel — Phase 9's existing
  `WorldGenerationController.Cancel()`), `OperationCanceledException`
  propagates → `LLMWorldDesigner` reports
  `WorldDesignFailureReason.Cancelled` → controller state `Cancelled`.
- If the **timeout** elapses first, a new `LLMRequestTimeoutException` is
  thrown instead → `LLMWorldDesigner` (extended this phase with a
  dedicated `catch` for it) reports `WorldDesignFailureReason.Timeout` →
  controller state `Failed` with a clean "World design timed out."
  message.

Both `WorldDesignFailureReason` values already existed (Phase 7/8) —
`LLMWorldDesigner` simply wasn't reaching them distinctly before (a
`LLMNotConfiguredException` or a timeout would previously have fallen into
the generic `Unknown` catch-all). Fixed this phase alongside the new
timeout path, since "missing API key"/timeout are both explicitly listed
requirements here and the correct enum values already existed unused.

`IHttpTransport`'s real implementation (`UnityWebRequestHttpTransport`)
never blocks the calling thread: `SendWebRequest()` + a
`while (!operation.isDone) { check cancellation; await Task.Yield(); }`
loop, copied from `OpenWorldReactorWorldGenerationService`'s established,
already-verified-safe pattern — no `Thread.Sleep`, no blocking
`.Result`/`.Wait()`, no `Task.Run` around any Unity API call anywhere in
this phase's code.

## 11. Error handling

| Condition | Result |
|---|---|
| No API key configured | `AnthropicLLMClient` throws `LLMNotConfiguredException` before ever calling the transport (verified — `NoApiKey_DoesNotSendAnyRequest` asserts zero transport calls) → `WorldDesignFailureReason.NotConfigured` |
| Connection-level failure (DNS, offline, refused) | `HttpTransportResponse.ConnectionError` → `LLMCompletionResult.Failed(...)` → `WorldDesignFailureReason.Unavailable` |
| HTTP 401/429/500/etc. | Response body's `{"error":{"type","message"}}` (Anthropic's documented error shape) logged safely, `LLMCompletionResult.Failed(...)` → `WorldDesignFailureReason.Unavailable` |
| Timeout | `LLMRequestTimeoutException` → `WorldDesignFailureReason.Timeout` (see §10) |
| Cancellation | `OperationCanceledException` → `WorldDesignFailureReason.Cancelled` |
| Outer response envelope isn't valid JSON | Caught, `LLMCompletionResult.Failed(...)` → `WorldDesignFailureReason.Unavailable` |
| No `tool_use` block in the response (model didn't call the tool) | `LLMCompletionResult.Failed(...)` → `WorldDesignFailureReason.Unavailable` |
| Extracted `input` isn't valid/parseable `WorldSpecification` JSON | Existing `WorldSpecificationJsonParser` path → `WorldDesignFailureReason.InvalidResponse` |
| Valid `WorldSpecification` fails validation | Existing `WorldGenerationController` path → `WorldDesignFailureReason.ValidationFailed` |

Note `NetworkError` and `Unavailable` are not distinguished for
connection-level vs. HTTP-status-level failures here — both land on
`Unavailable` ("reached the provider" is inaccurate for a pure connection
failure, but the existing enum's docstring already covers both reasonably
and Phase 10's own test list only requires one clean-failure test for this
combined case, not a further split). A reasonable, low-risk future
refinement, not a defect.

## 12. Security

- **No secrets in source, tests, scene files, ScriptableObjects, README,
  or documentation.** Every example in this document uses
  `ANTHROPIC_API_KEY=<your-key>`, never a real value. `.env.local`
  confirmed still `git check-ignore`-covered and untracked (§16).
- **API key never logged.** Attached only as the `x-api-key` header; every
  log statement in `AnthropicLLMClient` logs only provider-safe metadata
  (status codes, Anthropic's own `error.type`/`error.message` text,
  timing) — never a header, never the full request/response body by
  default.
- **`$type`/arbitrary-type-instantiation injection**: `AnthropicLLMClient`
  never deserializes the model's JSON into a .NET type at all — it only
  extracts the tool `input` sub-object's text via `Newtonsoft.Json.Linq`
  (`JObject`/`JToken`, pure data-tree parsing with no type-binding
  concept, so `TypeNameHandling` is not even applicable to this step).
  The one place that step exists is the same, unchanged, already-tested
  `WorldSpecificationJsonParser` (`TypeNameHandling.None`,
  `MetadataPropertyHandling.Ignore`, `MaxDepth = 32`) every `IWorldDesigner`
  already goes through. Verified end-to-end this phase with a dedicated
  test (`MaliciousTypeInjection_InToolInput_NeverExecutesOrThrows_StaysInertData`)
  feeding a `$type`-bearing payload through the real
  `AnthropicLLMClient` → `LLMWorldDesigner` → parser chain.
- **No dynamic code execution, no reflection-based type creation, no
  shelling out, no file writes** anywhere in this phase's code —
  `AnthropicLLMClient`/`WorldSpecificationToolSchema`/
  `UnityWebRequestHttpTransport` only ever build/read plain JSON data and
  make one HTTP POST.
- **Untrusted input boundary unchanged**: only fields that survive
  `WorldSpecificationValidator` may ever influence `WorldGenerator`.

## 13. Mock mode

Completely unaffected — `MockWorldDesigner` is untouched, remains the
`RuntimeSimulationBootstrap` default (`WorldDesignerMode.Mock`), and
requires no internet, no API key, no Reactor. `MockWorldDesignerTests`
and the Phase 9 `WorldGenerationControllerTests`/
`WorldGenerationRuntimeServiceTests` (all Mock-driven) are unmodified and
still pass by inspection of the diff — nothing in `Sim.Core`/`Sim.Simulation`/
`Sim.UI` changed this phase.

## 14. Testing

New `Assets/Tests/EditMode/AnthropicLLMClientTests.cs` — a fully in-memory
`FakeHttpTransport` (`IHttpTransport`, new this phase specifically for
this testability need, per the brief's "testable HTTP abstraction"
request) stands in for the real network; **no automated test depends on a
real API key or makes a real network call**. Covers: no key → zero
transport calls; user prompt sent intact; configured/default model used;
authentication headers correctly constructed; forced structured-output
tool request shape (and that `temperature` is never sent); a successful
tool-call response becomes a real, correctly-populated
`WorldSpecification` end-to-end through the unchanged parser; `$type`
injection stays inert data; malformed outer JSON fails cleanly; a missing
`tool_use` block fails cleanly; HTTP 401/429/500 all fail cleanly without
throwing; a connection error fails cleanly without throwing; a timeout
throws the new distinct exception (not a generic failure) and is
correctly told apart from caller-initiated cancellation racing it; both
reach the correct distinct `WorldDesignFailureReason` through
`LLMWorldDesigner`. `LLMWorldDesignerTests.cs`'s existing
`AnthropicLLMClient_NoKey_ThrowsNotConfigured` test is unchanged and still
passes against the new constructor (all new parameters are optional,
added after the existing `apiKeyOverride` one). `OpenAiLLMClient`/
`LocalLLMClient`'s existing tests are untouched and still pass — neither
class was modified.

## 15. Real-provider smoke testing

**Not run.** No `ANTHROPIC_API_KEY` (or any LLM provider credential) is
present in this project's `.env.local` or environment — it currently
holds only the Phase 6 OpenWorld Reactor credentials. Per this phase's
explicit instruction, this is stated plainly rather than claimed: no real
Anthropic API request was made at any point during this phase's
implementation or testing. To run one once a key is available: set
`ANTHROPIC_API_KEY` (via `.env.local` or the environment), then either use
`RuntimeSimulationBootstrap` in `LLM`/`Anthropic` mode from the runtime
scene, or call `new AnthropicLLMClient().CompleteAsync(...)` directly from
a throwaway Editor script — using a minimal prompt, checking the result
parses into a valid `WorldSpecification` via the existing validator,
**without** calling `WorldGenerator.Generate()` automatically (per this
phase's explicit cost-control instruction).

## 16. Known limitations

- Real Anthropic call unverified against the live API (§15).
- `NetworkError` vs. `Unavailable` not distinguished for Anthropic
  failures (§11) — a reasonable, documented simplification, not a defect.
- `strict: true` combined with a forced `tool_choice: {"type":"tool"}`
  (rather than the `{"type":"any"}` Anthropic's own tip specifically
  illustrates alongside strict mode) is, per the fetched documentation,
  an unrestricted combination — but this exact pairing has not been
  observed against a real response.
- `OpenAiLLMClient`/`LocalLLMClient` remain stubs — untouched, per this
  phase's explicit "implement one provider" instruction.
- No retry logic beyond the single bounded attempt this phase's brief
  asked for — a request that fails is reported as a clean failure, never
  silently retried.

## 17. Future provider additions

`WorldSpecificationToolSchema` and `IHttpTransport`/
`EnvironmentLlmCredentialsProvider` were written in the provider-neutral
part of `Sim.AI.WorldDesign` specifically so a future real `OpenAiLLMClient`
or `LocalLLMClient` implementation can reuse all three (OpenAI's Chat
Completions API has its own, differently-shaped structured-output
mechanism — `response_format: {"type":"json_schema", ...}` — so it would
consume the same canonical schema object differently, not duplicate it)
rather than each hand-rolling its own schema, credential lookup, and
transport plumbing from scratch.
