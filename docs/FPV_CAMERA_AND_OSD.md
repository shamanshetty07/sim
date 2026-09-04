# FPV Camera & OSD — Phase 4

## Data flow

```
Input -> Drone (Phase 3) -> Flight Physics -> FlightTelemetry -> OSD/HUD (Phase 4)
```

The UI layer never controls or reads back into drone physics. Concretely:

- `FPVCameraController` holds a `Transform` reference (the drone's `CameraMount`)
  and nothing else drone-related — no `Rigidbody`, no `DronePhysics`, no
  `DroneController`. It reads that Transform in `LateUpdate` and writes only
  its own Transform. There is no code path by which this script could apply
  a force, set a velocity, or otherwise touch the Rigidbody.
- `FPVHUD` holds a `DroneController` reference for exactly one purpose:
  subscribing to `TelemetryUpdated`. It never calls anything else on
  `DroneController`, never touches `DronePhysics`/`Rigidbody`, and
  `TelemetryUI` (which does the actual display work) doesn't even have a
  `DroneController` reference — `FPVHUD` hands it plain `FlightTelemetry`
  structs.

## Camera hierarchy

```
Drone                          (Rigidbody, DroneController, DronePhysics, DroneInput)
 └── CameraMount                (marker component only, no behaviour)

FPV Camera                      (NOT parented under Drone — see below)
 ├── Camera
 ├── AudioListener
 └── FPVCameraController        (_mount -> Drone/CameraMount)
```

`FPV Camera` is a sibling GameObject, not a child of `Drone`/`CameraMount`,
deliberately. If it were parented, Unity's automatic transform inheritance
and the script's own explicit world-space position/rotation assignment would
both be trying to drive the same Transform every frame — harmless in
practice since the explicit assignment always wins, but confusing to reason
about and easy to get wrong later (e.g. if the smoothing script is ever
disabled, a parented camera would silently fall back to rigid inheritance
instead of just not moving). Keeping it independent means `FPVCameraController`
is the *only* thing that ever writes to that Transform, full stop.

`CameraMount`'s local position is `(0, 0.02, 0.09)` on the drone — front and
slightly above center, roughly where a real FPV camera sits, just past the
edge of the primitive body visual (body half-extent is ~0.08 on that axis).
Its local rotation is identity; all tilt is applied by `FPVCameraController`
so there is exactly one place that controls it.

## Camera configuration (`FPVCameraController`)

| Field | Default | Notes |
|---|---|---|
| `_fieldOfView` | 120° | Range 60-170. FPV drones commonly run wide for peripheral awareness at speed. |
| `_tiltDeg` | 15° | Additional tilt beyond the mount's own orientation. **Sign not verified against Unity's rotation chirality without an Editor to test in** — if 15 tilts the view the wrong way, use a negative value. |
| `_positionSmoothSpeed` | 0 (rigid) | 0 = camera position exactly matches the mount every frame, like a bolted-on real FPV camera. |
| `_rotationSmoothSpeed` | 20 | Exponential-decay smoothing (frame-rate independent — see `CameraSmoothing`), takes the edge off high-frequency physics jitter without meaningfully lagging behind the drone's attitude. 0 = rigid. |
| `_shakeAmplitude` | 0 (off) | Perlin-noise positional jitter. Off by default; keep small if enabled — this is meant to be felt, not to obscure the view. |
| `_shakeFrequency` | 20 | How quickly the shake pattern varies. |

Position and rotation smoothing use the same exponential-decay formula
(`1 - e^(-speed * deltaTime)`), which — unlike a plain `Lerp(a, b, constant)`
— behaves identically regardless of frame rate. It lives in `CameraSmoothing`
as pure `Vector3`/`Quaternion` math with zero Unity-physics or Transform
dependency, specifically so it's unit-testable without a scene (see
`Assets/Tests/EditMode/CameraSmoothingTests.cs`).

## Telemetry flow

`FlightTelemetry` (Phase 3, extended here) now also carries
`LocalAngularVelocityDegPerSec` (and a computed `AngularSpeedDegPerSec`
magnitude), populated from the same `DroneAttitudeState` `DronePhysics`
already computes each `FixedUpdate` — no new physics calculation was added,
this is a value that existed locally in `DroneController.FixedUpdate` but
wasn't previously threaded into the telemetry struct. That's the only Phase
3 change this phase required.

```
DroneController.FixedUpdate()
  -> builds FlightTelemetry
  -> TelemetryUpdated event fires
       -> FPVHUD.HandleTelemetryUpdated(telemetry)
            -> TelemetryUI.UpdateTelemetry(telemetry)   [formats + displays]
```

FPS is **not** part of `FlightTelemetry` — it's a rendering-time concern
(computed once per rendered `Update`, a different frequency than the
physics-rate telemetry event) and is computed directly in `FPVHUD.Update()`
using an exponential-decay smoothed `1/deltaTime`, then pushed to
`TelemetryUI.UpdateFps()` separately from the main telemetry path.

## UI architecture

```
FPVHUD              coordinates: owns the DroneController subscription,
                     computes/smooths FPS, forwards both to TelemetryUI.
                     The only class that talks to DroneController.

TelemetryUI          presentation only: FlightTelemetry -> TextMeshProUGUI
                     text + the horizon bar's rotation/offset. No physics,
                     no DroneController reference, no formatting logic of
                     its own beyond the horizon bar geometry.

TelemetryFormatter    static, pure string formatting (FlightMode -> "ANGLE",
                     bool -> "ARMED"/"DISARMED", float -> "25.4 m", etc.).
                     No MonoBehaviour, no TextMeshPro dependency — this is
                     what's unit-tested in TelemetryFormatterTests.cs.
```

Splitting `TelemetryFormatter` out of `TelemetryUI` mirrors the project's
established pure-math-core/thin-MonoBehaviour-wrapper pattern (see
`DroneFlightModel`/`DronePhysics` and `CameraSmoothing`/`FPVCameraController`
in Phase 3/4) — the actual conversion logic is testable without a Canvas,
and `TelemetryUI` stays a thin wiring layer.

### Crosshair & horizon indicator

Both are plain `UnityEngine.UI.Image` rectangles under the OSD Canvas — no
sprite assets, consistent with the project's primitive-fallback approach:

- **Crosshair**: two thin bars forming a "+", statically centered.
  `raycastTarget = false` on both, and there is no code anywhere that reads
  input through this object — it is pure decoration and structurally cannot
  affect aiming or physics.
- **Horizon bar**: one thin bar. `TelemetryUI` rotates its `RectTransform`
  by `-RollDeg` and offsets it vertically by `-PitchDeg * pixelsPerDegree`
  (clamped to `_maxHorizonOffsetPixels` so extreme pitch doesn't send it
  off-screen). This is deliberately minimal — a single line, not a full
  attitude ladder/tape. **Future improvement, not done here**: a proper
  ladder with pitch gradations, or replacing the flat clamp with a
  wrap/fade behavior past ±90°.

## Event subscription lifecycle

This is the part most likely to leak or misbehave once world regeneration
(Phase 7+) starts destroying and recreating drones, so it's worth being
explicit about:

- `FPVHUD._droneController` is a `[SerializeField]` — assigned by Editor
  tooling (or the Inspector) and, critically, **persists through a scene
  save**, unlike a plain runtime field or a live event subscription (Unity
  never serializes either of those). `Awake()` reads that serialized field
  and performs the actual subscription fresh, every time the scene loads —
  this is what makes the wiring survive `EditorSceneManager.SaveScene`.
- `SetDroneController(DroneController)` is the runtime re-wiring path: it
  always unsubscribes from whatever it was previously subscribed to first
  (tracked via a separate `_subscribedController` field, distinct from the
  serialized `_droneController`), then subscribes to the new one — including
  when the "new one" is `null` (clean detach) or the *same* controller
  (unsubscribe-then-resubscribe nets exactly one subscription, never two).
- `OnDestroy()` unsubscribes defensively, so a HUD destroyed without an
  explicit `SetDroneController(null)` first still can't leave a dangling
  subscription on a `DroneController` that outlives it.
- Symmetrically, nothing in `DroneController`/`DronePhysics`/`DroneInput`
  holds a reference back to `FPVHUD` or `FPVCameraController` — the
  dependency is one-directional, so destroying the UI can never affect the
  drone, and destroying the drone leaves the UI with a stale-but-harmless
  reference until something calls `SetDroneController` again (it simply
  stops receiving telemetry updates; it does not throw).

## Editor tooling (`DroneRigBuilder`)

Extended, not duplicated, per the existing Phase 3 approach of building
scene content through code rather than hand-authored `.unity`/prefab files:

| Menu item | Result |
|---|---|
| `FPV Sim/Create Drone Rig` | Drone rig only (now includes a `CameraMount` child). |
| `FPV Sim/Create FPV Camera` | Standalone FPV camera, auto-wired to a `CameraMount` found on the current selection or anywhere in the scene. |
| `FPV Sim/Create OSD Canvas` | Standalone OSD, auto-wired to a `DroneController` found in the scene. |
| `FPV Sim/Build Test Rig In Current Scene` | Ground + light + drone + camera + OSD, all wired, in whatever scene is currently open (not saved automatically). |
| `FPV Sim/Build Drone Test Scene (Save To Disk)` | The same full build, in a **new** scene, saved to `Assets/Scenes/DroneTestScene.unity`. Prompts before overwriting an existing scene file, and — since replacing the open scene would otherwise silently discard unsaved changes in whatever was open — prompts to save those first via `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()`, aborting the whole build if that's cancelled. |

All the `FindObjectOfType`/`GetComponentInChildren` lookups in this file run
once, when a menu command is clicked — never per-frame at runtime, so they
don't conflict with the project's "no expensive searches in Update" rule.

`AssignConfig` (Phase 3) was generalized to `AssignField(component,
fieldName, value)` and is now used for every wiring assignment (`_config`,
`_mount`, `_telemetryUI`, `_droneController`, and all of `TelemetryUI`'s
text/horizon-bar fields). It goes through `SerializedObject`, which is
**required** for edit-time wiring to actually persist: a plain (non-
`[SerializeField]`) field or an event subscription set by calling a public
method directly from an Editor script would not survive `SaveScene` at
all — Unity's serializer only persists `[SerializeField]` object references.
This is why `FPVHUD` exposes `_droneController` as a serialized field
(persists) rather than relying solely on `SetDroneController` being called
once at build time (would not persist).

## Manual Unity verification checklist

No Unity Editor was available while writing this phase, so none of the
following has actually been run — everything above is static/source-level
verification only. This is what to do in Unity 2022.3 LTS to verify it:

1. Open the project in Unity 2022.3 LTS. Let it resolve packages (Input
   System, TextMeshPro, Test Framework). **If Unity prompts to import "TMP
   Essential Resources"** the first time it encounters a TextMeshPro
   component, accept it — the HUD text will not render correctly without
   the default font asset.
2. `FPV Sim > Build Drone Test Scene (Save To Disk)` — builds and saves
   `Assets/Scenes/DroneTestScene.unity`. (Or `Build Test Rig In Current
   Scene` to build without saving, in whatever scene is open.)
3. Check the Console for errors/warnings immediately after building — the
   tooling logs a warning if a mount/controller couldn't be found to wire
   up; there should be none in a fresh build.
4. Open `DroneTestScene`, enter Play Mode.
5. **FPV camera follows the drone**: the Game view should show the drone's
   POV, not a third-person/default view. Throttle up (Space or right
   trigger) — the camera should move with the drone with no visible lag on
   position, only a slight smoothing on rotation.
6. **Controls still work** (unchanged from Phase 3): Backspace/gamepad
   Start to arm, Space/right trigger throttle, WASD/right stick pitch+roll,
   Q/E/left stick X yaw.
7. **Flight mode switching**: Tab/gamepad Y cycles Angle -> Horizon -> Acro
   -> Angle. Confirm the OSD's mode text updates to match and that the feel
   changes (Angle self-levels, Acro doesn't).
8. **OSD values update in real time** while flying: altitude, speed,
   vertical speed, throttle %, pitch/roll/yaw, angular rate, all changing
   plausibly as you fly. Cross-check altitude against the Y position shown
   in the Inspector's Transform for the Drone object.
9. **Armed/disarmed state**: OSD should show `DISARMED` at start (motors
   off, drone should be falling if above ground, or resting if it landed);
   after arming, `ARMED` and throttle becomes responsive.
10. **No Console errors** at any point above — NullReferenceExceptions here
    would most likely mean a wiring gap (an unassigned serialized field);
    check the DroneRigBuilder console warnings from step 3 first.
11. **Destroy/recreate the drone does not duplicate telemetry**: with the
    HUD in the scene, in Play Mode select the `Drone` GameObject and delete
    it (or write a quick throwaway test script that does
    `Object.Destroy(droneGO)` then re-runs `DroneRigBuilder.CreateDroneRig()`
    equivalent logic and calls `hud.GetComponent<FPVHUD>().SetDroneController(newController)`).
    Confirm the OSD does not show doubled/conflicting values and the
    Console shows no errors from the old (destroyed) `DroneController` still
    being referenced — this exercises the `SetDroneController`
    unsubscribe-before-resubscribe path described above. This scenario
    matters specifically because Phase 7+ world regeneration will do
    exactly this.

## Known limitations / future improvements

- Horizon bar is a single line, not a full attitude ladder — noted above.
- Camera tilt sign (`_tiltDeg`) is unverified against Unity's actual
  rotation direction; flip the sign if it looks wrong.
- The primitive drone visual has no dedicated "hide from its own camera"
  layer mask — at a `nearClipPlane` of 0.05 with the mount just past the
  body's edge, this is unlikely to be visible, but hasn't been confirmed
  visually. If the body is visible in the camera view, the fix is a
  dedicated layer + `Camera.cullingMask` on the FPV camera.
- Camera shake exists but defaults to off (amplitude 0) — Phase 4's brief
  was to keep the first pass minimal; a throttle/speed-linked shake curve
  would be a natural follow-up once the core loop is verified.
- No `EventSystem` in the OSD canvas — not needed yet since nothing in the
  HUD is interactive. Phase 8's prompt UI will need to add one.
