# Reactor → Unity Architecture Investigation — Phase 6.5

**Question:** How does an OpenWorld Reactor–generated environment become an
environment our Unity FPV drone can physically fly through?

**Short answer:** It doesn't, not directly — Reactor's real, documented
product surface is a live video stream, on every model on the platform,
with no 3D/spatial/structured export of any kind. See "Recommendation"
at the end. This finding is based on a systematic read of Reactor's real
public documentation (docs.reactor.inc), not guesswork, and no undocumented
transport was reverse-engineered — everything cited below is from a
published doc page.

## Method

Continuing from Phase 6 (which found `docs.reactor.inc/authentication.md`
and one model's overview/schema), this pass fetched the platform's full
documentation site map (`llms.txt`, 63 pages) and specifically checked
every page plausibly related to non-video output:

- `concepts/tracks.md` — the track type system
- `concepts/frame-metadata.md` — per-frame metadata attachment
- `concepts/recordings.md` — session export/download
- `models/overview.md` — the shared wire protocol
- `model-api-reference/lingbot-world-2/schema.md` — full command/event list
- `model-api-reference/lingbot/overview.md` — the other LingBot model
- `model-api-reference/happy-oyster/overview.md` + `schema.md` — the
  "permanent explorable worlds" model, specifically checked because
  "permanent" sounded like it might mean an exportable/persistent world
  representation
- `resources/faq.md`
- `changelog/overview.md` — checked for a recently-added export feature

## 1. What Reactor actually generates

Every model on the platform (checked: LingBot, LingBot World 2,
HappyOyster, plus the video-generation-only models FastH3/Helios/LongLive-
2.0/SANA-Streaming/Visko Orbis Stable/X2/LTX) produces **video**. For the
interactive/navigable ones specifically:

| Model | Output | Resolution/Rate |
|---|---|---|
| LingBot | Video, WASD-navigable | 1664×960, 16fps |
| LingBot World 2 | Video, WASD+camera-navigable, image-anchored | 1664×960, 48fps |
| HappyOyster | Video, two modes (free exploration / narrative steering) | 480-720p |

HappyOyster's "permanent" worlds are **session-resumable, not exported** —
an `encrypted_world_id` lets you `attachWorld()` to continue generating the
same world later. It is not a saved 3D scene; it's a resumable video
generation session. Confirmed explicitly in its docs: "no evidence of 3D
asset export, multi-user world sharing, or structured world data
availability."

## 2. What data we can access

Beyond the video pixels themselves:

- **Structured JSON events** — but these describe *generation/session
  state* (is a prompt/image accepted, which chunk is playing, is
  generation paused, current input-command values), never *scene content*.
  Confirmed on LingBot World 2's full schema: the `state` event reports
  `move_longitudinal`, `look_horizontal`, `camera_pose_active` (a boolean —
  *whether* a directed pose command is active, not a pose value) etc. —
  inputs and status, not computed world geometry or coordinates. Verbatim
  from the schema fetch: *"No camera pose/position/rotation data in
  events... No world coordinate system is exposed in the API... No
  structured scene/object state JSON."*
- **Frame metadata (`user_data`)** — opaque bytes you attach yourself to
  outbound frames and read back on the corresponding model output, for
  your own correlation/sequencing. Not a channel for the model to tell you
  anything about the scene it generated.
- **Recordings** — MP4 (JS SDK) or MPEG-TS→MP4 (Python SDK) video clips,
  and the download URL **expires after 24 hours**. Video, not a scene.
- **Track types** — formally just `"video"` and `"audio"`. The generic
  wire-protocol docs mention some model *could* expose a depth-map track
  alongside its color video (a real technique — encoding depth into a
  second video-kind track) — but this is **not true of any model actually
  checked**: LingBot World 2 confirmed to produce only `main_video`
  (RGB), no depth track.

## 3. What Unity needs

For the project's stated goal (a Rigidbody-physics FPV drone with real
collision flying through a generated environment), Unity needs at minimum
one of:

- Actual geometry (mesh/heightmap) it can build `MeshCollider`/terrain
  collision from, **or**
- A structured scene description (positions/types/scales of terrain
  features and obstacles) it can procedurally build geometry from — the
  `WorldSpecification` contract this project already has (Phases 5-6), **or**
- Depth/spatial data dense enough to reconstruct approximate collision
  geometry (e.g. a depth map → point cloud → collision mesh pipeline).

A video stream, however good-looking or steerable, satisfies **none** of
these on its own — Unity's Rigidbody/PhysX system has no way to derive a
collider from RGB pixels.

## 4. Are the two compatible?

**Not directly, and not through any workaround short of building the
physics world ourselves.** Concretely:

- No mesh/point-cloud/depth data of any kind is available from any checked
  model — ruling out even an approximate collision reconstruction.
- No structured scene/object JSON is available — ruling out "read back
  what Reactor placed and build it as Unity primitives," which was this
  project's original Phase 5/6 design assumption (`ReactorWorldResult`
  with a `StructuredData` payload kind). That payload kind was designed
  defensively, before this could be confirmed either way; it's now
  confirmed there is nothing to populate it with, for any model on the
  platform today.
- The video stream itself *could* be displayed inside Unity (a decoded
  WebRTC video texture, or a Unity WebView plugin embedding Reactor's own
  JS player) as a **visual backdrop** — but the drone's actual flight would
  have no physical relationship to what's shown: there is no collision
  data behind the pixels, no depth, nothing to make "flying into that
  cliff in the video" correspond to an actual collision in Unity. This
  would be decoration, not environment.

## 5. Possible integration architectures

**Option A — Direct Reactor → Unity world integration.**
Reactor hands Unity something it can build real geometry/collision from.
**Not available.** No model exposes mesh, point cloud, depth (in practice),
GLTF/USD/FBX, or structured scene data. This option requires a Reactor
product capability that does not currently exist.

**Option B — Reactor bridge process → Unity.**
A companion Node/Python process (using Reactor's real SDK) mediates
between Reactor and Unity, keeping the API key server-side per Reactor's
own guidance. **Solves the transport/security problem, not the data
problem.** A bridge only relays what Reactor actually sends — video and
generation-state events. It would make Option A meaningfully easier to
build *if* Option A's missing data ever became available, and it would be
the right way to pipe the live video into Unity as decoration (see
Option C below) if that's ever wanted. On its own it does not produce a
flyable world.

**Option C — Hybrid Reactor + Unity generation.**
Unity's own procedural generator (the `WorldSpecification` → terrain/
environment/obstacle pipeline already scaffolded in Phases 5-6) is the
actual, physical, collidable world. Reactor's role shrinks to either (a)
nothing, for now, or (b) a non-authoritative decorative layer (e.g. a
Reactor video panel/skybox-adjacent visual, requiring a bridge process per
Option B) layered on top of, but not driving, the real Unity geometry.
**This is buildable today**, using only documented, real capabilities.

**Option D — Current Reactor API cannot support the desired simulator
yet.** Also true, as a statement about the *specific goal* of "Reactor
generates the actual flyable, collidable world." See recommendation.

## 6. Recommended architecture

**Option D is the accurate diagnosis; Option C is the resulting practical
path forward, once D is accepted.** These aren't in tension — D describes
what Reactor cannot do today, and C describes what to actually build given
that constraint, using infrastructure this project already has.

Reasoning:

- Every one of Reactor's 8 hosted models was checked. Every one is a video
  model. There is no scenario among them (including the "permanent worlds"
  one, which sounded most promising going in) where the generated
  environment exists as anything other than pixels.
- This isn't a gap in *this project's* integration work — it's the actual
  shape of Reactor's product today. No amount of correct client-side
  engineering (bridge process, native client, anything) produces geometry
  that was never emitted server-side.
- Unity's own procedural generation is the only currently-available path
  to a Rigidbody-collidable world, and this project already has real
  infrastructure for it (`WorldSpecification` and its sub-models,
  `WorldSpecificationValidator`, the `WorldGenerationController` state
  machine) sitting ready for a `WorldGenerator` to consume — this is
  exactly what Phase 7 was going to build before this investigation was
  requested.

## An important second-order finding

The original architecture ("AI decides *what*, Unity decides *how*")
assumed OpenWorld Reactor's intelligence could inform *some* structured
representation of world content — JSON, a scene graph, anything readable.
That assumption doesn't hold for **any output modality**, not just 3D
specifically: Reactor cannot hand back a description of "what's in the
scene" in any form — only video pixels and generation/session-status
events. So even a Unity-native procedural generator (Option C) **cannot
get its content decisions from Reactor** as currently accessible. Whatever
turns the user's prompt into "200 trees, cliffs on the north edge, 8 gates
along a canyon" has to be a different capability than what LingBot/Reactor
provides — most plausibly a general-purpose LLM call that outputs
structured JSON matching `WorldSpecification`'s shape, which is a
different integration than anything built in Phases 5-6 and a decision
for the user, not assumed here. Flagged, not solved, in this document —
raising it is this investigation's job; deciding it is the next
conversation.

## 7. What additional Reactor access/API/SDK would be required

For Option A to become viable, Reactor would need to ship (none of which
exist today, per the documentation checked):

- A structured scene/world-state export (JSON describing placed objects,
  terrain, coordinates) — the most direct fit for this project's existing
  `WorldSpecification` contract, **or**
- A mesh/point-cloud/depth export (GLTF/USD/FBX, or a genuinely-available
  depth track usable for reconstruction) — a heavier integration but
  compatible with "the actual generated geometry," **or**
- A documented Unity/C# SDK, or an official WebRTC wire-protocol spec
  detailed enough to implement a native client against without guessing.

None of these were found in the full 63-page documentation set, the
changelog, or the FAQ. If the user has access to a Reactor account
representative, private beta docs, or a different product tier not
reflected in the public docs, that would be the next thing to check before
concluding this is a hard blocker rather than a "not yet public" one.

## What this phase deliberately did not do

- Did not reverse-engineer the undocumented parts of the WebRTC session
  protocol.
- Did not invent a Reactor capability, endpoint, or SDK method not found
  in real documentation.
- Did not build a bridge process, a Unity world generator, or any other
  implementation — this is a research/decision document only, per the
  instruction it was created under.
