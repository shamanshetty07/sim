using UnityEngine;

namespace Sim.Camera
{
    /// <summary>
    /// Drives an FPV camera from a drone's CameraMount. This script is intentionally NOT a
    /// child of the drone in the scene hierarchy — it is a standalone GameObject that reads
    /// the mount's Transform each LateUpdate and writes its own Transform. That is what
    /// guarantees the two requirements every other design here would have to work to
    /// preserve: it never touches the drone's Rigidbody, and it cannot control drone
    /// movement, because it has no reference to DronePhysics/DroneController/Rigidbody at
    /// all — only a Transform to read from.
    ///
    /// Position is rigidly copied from the mount by default (a real FPV camera is bolted to
    /// the frame — it doesn't lag behind it), while rotation can optionally be smoothed to
    /// take the edge off high-frequency physics jitter without meaningfully delaying how the
    /// pilot reads the drone's attitude. Both are configurable; set smoothing to 0 for a
    /// perfectly rigid mount on either axis.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class FPVCameraController : MonoBehaviour
    {
        [Header("Mount")]
        [Tooltip("The drone's CameraMount transform this camera follows. Assign via the Editor tooling or Inspector.")]
        [SerializeField] private Transform _mount;

        [Header("Lens")]
        [Tooltip("Field of view in degrees. FPV drones commonly run wide (100-150) for peripheral awareness at speed.")]
        [SerializeField, Range(60f, 170f)] private float _fieldOfView = 120f;

        [Tooltip("Additional tilt (degrees) applied on top of the mount's own orientation — most FPV pilots tilt the camera down somewhat for forward visibility at speed. Sign convention wasn't verified against Unity's rotation chirality without an Editor to test in; if 15 tilts the view up instead of down, use a negative value.")]
        [SerializeField, Range(-45f, 45f)] private float _tiltDeg = 15f;

        [Header("Smoothing")]
        [Tooltip("0 = rigidly follow the mount's position every frame (default, matches a physically-mounted camera). Higher = more lag.")]
        [SerializeField, Min(0f)] private float _positionSmoothSpeed = 0f;

        [Tooltip("0 = rigidly follow the mount's rotation every frame. Higher = smooths out high-frequency physics jitter; too low feels floaty/disconnected from the drone.")]
        [SerializeField, Min(0f)] private float _rotationSmoothSpeed = 20f;

        [Header("Shake (optional, off by default)")]
        [Tooltip("Positional shake amplitude in meters. 0 disables shake entirely. Keep small — this is meant to be felt, not to obscure the view.")]
        [SerializeField, Min(0f)] private float _shakeAmplitude = 0f;

        [Tooltip("How quickly the shake pattern varies. Higher = buzzier, lower = a slower wobble.")]
        [SerializeField, Min(0.01f)] private float _shakeFrequency = 20f;

        private UnityEngine.Camera _camera;
        private Vector3 _shakeSeed;

        public Transform Mount => _mount;
        public float FieldOfView => _fieldOfView;
        public float TiltDeg => _tiltDeg;

        /// <summary>Assigns the mount to follow. Safe to call at runtime to re-target a newly spawned drone.</summary>
        public void SetMount(Transform mount) => _mount = mount;

        private void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            // Distinct per-instance offsets into Perlin noise space so multiple FPV cameras
            // (unlikely today, but cheap to get right) don't shake in perfect unison.
            _shakeSeed = new Vector3(Random.value, Random.value, Random.value) * 100f;
            ApplyLensSettings();
        }

        // Re-applies FOV immediately when tweaked in the Inspector, without requiring Play mode.
        private void OnValidate()
        {
            if (_camera == null) _camera = GetComponent<UnityEngine.Camera>();
            ApplyLensSettings();
        }

        /// <summary>
        /// Pushes the configured field of view onto the Camera component. Public so Editor
        /// tooling and tests can trigger it directly. Lazily resolves _camera itself rather
        /// than assuming Awake/OnValidate already ran — Awake in particular does not fire for
        /// a component added via script in Edit mode (only in Play mode, or with
        /// [ExecuteInEditMode]), so a caller invoking this right after AddComponent cannot
        /// rely on that ordering.
        /// </summary>
        public void ApplyLensSettings()
        {
            if (_camera == null) _camera = GetComponent<UnityEngine.Camera>();
            if (_camera != null) _camera.fieldOfView = _fieldOfView;
        }

        // LateUpdate, not Update/FixedUpdate: runs after the drone's FixedUpdate (physics)
        // and after any Update-driven movement have both been applied for this frame, so the
        // camera reads a fully-settled pose rather than a half-updated one.
        private void LateUpdate()
        {
            if (_mount == null) return;

            Vector3 targetPosition = _mount.position + GetShakeOffset();
            Quaternion targetRotation = _mount.rotation * Quaternion.Euler(_tiltDeg, 0f, 0f);

            transform.position = CameraSmoothing.SmoothPosition(transform.position, targetPosition, _positionSmoothSpeed, Time.deltaTime);
            transform.rotation = CameraSmoothing.SmoothRotation(transform.rotation, targetRotation, _rotationSmoothSpeed, Time.deltaTime);
        }

        /// <summary>
        /// Smooth (Perlin, not per-frame-random) positional jitter, zero by default. Using
        /// Perlin noise sampled from a continuously advancing time offset — rather than
        /// Random.value each frame — avoids the harsh, flickery look of true white noise and
        /// costs no per-frame allocation.
        /// </summary>
        private Vector3 GetShakeOffset()
        {
            if (_shakeAmplitude <= 0f) return Vector3.zero;

            float t = Time.time * _shakeFrequency;
            float x = (Mathf.PerlinNoise(_shakeSeed.x, t) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(_shakeSeed.y, t) - 0.5f) * 2f;
            float z = (Mathf.PerlinNoise(_shakeSeed.z, t) - 0.5f) * 2f;
            return new Vector3(x, y, z) * _shakeAmplitude;
        }
    }
}
