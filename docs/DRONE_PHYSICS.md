# Drone Physics — Phase 3

## Credit / reference

Flight-mechanics *concepts* were studied from
[Venkatesan-M/UnityFPVDroneSimulator](https://github.com/Venkatesan-M/UnityFPVDroneSimulator)
— specifically its use of Rigidbody-based physics (not transform-driven fake
flight), the three-mode split (Angle/Acro/Horizon), deadzone + expo stick
shaping, and calling out yaw damping as its own tunable rather than folding
it into a generic rate PID. No code from that repository is copied; this
project's implementation, file layout, and control-loop structure are
written independently (see `Assets/Scripts/Drone/`).

## Architecture

```
DroneInput          reads Gamepad/Keyboard (Input System), applies deadzone
                     + expo, returns a DroneInputSample (shaped -1..1 / 0..1)
        │
        ▼
FlightModeController holds current FlightMode + Armed state, handles
                     mode-cycle / arm-toggle requests
        │
        ▼
DroneFlightModel     pure math (no Rigidbody/MonoBehaviour): input + mode +
                     current attitude/rates + DroneConfig → FlightOutput
                     (thrust newtons + local-space torque)
        │
        ▼
DronePhysics         applies FlightOutput to the Rigidbody in FixedUpdate,
                     plus manual air-resistance (linear + quadratic drag)
        │
        ▼
DroneController      orchestrates the above each FixedUpdate, publishes
                     FlightTelemetry for UI/OSD (Phase 4) to consume
```

`DroneFlightModel` takes no Unity physics types (only `Vector3`/`Mathf`), so
it is exercised directly by `Tests/EditMode/DroneFlightModelTests.cs` with no
scene, no Play mode, and no Rigidbody at all.

## Axis convention

Unity is left-handed, Y-up. This project uses:

- **Local X** = pitch axis (rotating around it tips the nose up/down)
- **Local Y** = yaw axis (rotating around it turns the nose left/right)
- **Local Z** = roll axis (rotating around it banks left/right; Z is "forward")

Positive pitch stick input (S / right-stick-down by default) pitches the
nose *down* — moving the drone forward — which is a **negative** rotation
around the pitch axis in this convention; `DroneFlightModel` flips that sign
internally (`invert: true` for pitch only) so config values and comments can
stay in terms of "positive = forward-feeling stick."

## Control loop, all three modes

Every mode uses the same cascaded structure a real flight controller uses:
an **outer loop** turns stick (and, for Angle/Horizon, current attitude)
into a *target angular rate*; a single **inner rate loop** turns
`(target rate − current rate)` into torque. Only the outer loop differs
between modes:

| Mode | Pitch/Roll outer loop | Yaw |
|---|---|---|
| **Acro** | Target rate = stick × `MaxPitchRollRateDegPerSec` (open loop, no self-level) | Always rate-only |
| **Angle** | Target angle = stick × `MaxTiltAngleDeg`; P-controller on angle error → target rate, clamped to max rate | Always rate-only |
| **Horizon** | Blends Angle-mode target rate near center stick to Acro-mode target rate at full deflection (`HorizonBlendStart` sets where the blend begins) | Always rate-only |

Yaw never gets an angle/heading-hold outer loop in any mode — this matches
real flight controllers, which only ever rate-control yaw. Yaw also carries
an extra damping term (`YawDamping`, proportional to current yaw rate) on
top of the rate-error term, called out as its own config field per the
project brief, because a bare P-on-rate-error term alone under-damps yaw in
practice (it tends to "wag").

## Air resistance

Modeled manually (Rigidbody's built-in `drag`/`angularDrag` are zeroed to
avoid double-damping): a linear term dominant at low speed, a quadratic term
that dominates at high speed and is what actually caps top speed (real
aerodynamic drag scales with velocity²), plus simple angular damping beyond
what the rate loop already removes.

## Safety / flight-state handling

- **Armed/disarmed** is tracked in `FlightModeController`. Disarmed, no
  thrust/torque is applied at all — the drone free-falls under
  gravity/drag like a real disarmed quad, rather than hovering motionless.
- Arming is refused while throttle input is above `MaxThrottleToArm`, so the
  drone can't lurch the instant it arms because a stick/key was already
  partway up.

## Controls (default bindings)

| Action | Gamepad | Keyboard |
|---|---|---|
| Throttle | Right trigger (rests at 0, like a real non-centering throttle stick) | Space = up, Left Ctrl = down (ramps at `KeyboardThrottleRatePerSecond`) |
| Pitch | Right stick Y | W (nose down/forward) / S (nose up/back) |
| Roll | Right stick X | A / D |
| Yaw | Left stick X | Q / E |
| Cycle flight mode | Y / Triangle (buttonNorth) | Tab |
| Arm / disarm | Start | Backspace |
| Reset to spawn | Select/Back | R |

Stick mapping is RC "Mode 2" (throttle+yaw on the left stick, pitch+roll on
the right) — the most common transmitter layout. A gamepad and the keyboard
can be used simultaneously (their raw axes are summed before shaping); the
keyboard is always live as a fallback even with a gamepad connected.

Pitch's sign was checked against the axis convention above (W = nose-down =
forward). Roll and yaw's absolute sign (whether "D"/right-stick-X-positive
banks toward world-right vs world-left) was not independently re-derived
from Unity's rotation chirality — it's internally consistent but, like a
real transmitter's channel-reverse setting, may come out feeling inverted
the first time this is actually flown in the Editor. If so it's a one-line
fix: flip the sign where `rawRoll`/`rawYaw` are accumulated in
`DroneInput.Sample`.

## Verifying in the Editor

No Unity install was available while writing this phase, so nothing here
has actually been compiled or played yet. To verify:

1. Open the project in Unity 2022.3 LTS and let it resolve packages.
2. **FPV Sim → Create Minimal Test Scene** (menu bar) — builds a ground
   plane, a light, and a drone rig (primitive-cube-and-cylinder visual, no
   art assets required) with a default `DroneConfig` asset created at
   `Assets/Settings/DefaultDroneConfig.asset`.
3. Press Play, **Backspace** to arm, then fly with the bindings above.
4. If it feels too twitchy/floaty, retune `DroneConfig` — the defaults are
   reasonable starting points for a 5" freestyle-style quad, not physically
   derived constants.

`FPV Sim → Create Drone Rig` builds just the rig (no ground/light), useful
once a real scene already exists.

## Known limitations / next phase

- No FPV camera yet (Phase 4) — flying currently means watching from the
  Scene/Game view's default camera.
- No OSD/telemetry UI yet (Phase 4) — `DroneController.TelemetryUpdated`
  is wired and ready to consume, just nothing subscribes to it yet.
- Pitch/roll angle extraction (`DronePhysics.ReadAttitude`) is valid for the
  ±90° range Angle/Horizon actually self-level within; Acro mode never reads
  it, so this doesn't affect Acro's behavior at any attitude.
