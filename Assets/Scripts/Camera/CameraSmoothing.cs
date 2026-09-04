using UnityEngine;

namespace Sim.Camera
{
    /// <summary>
    /// Pure position/rotation smoothing math — no Transform, no Camera, no MonoBehaviour.
    /// Kept separate from FPVCameraController so the smoothing behaviour is unit-testable
    /// without a scene, mirroring how DroneFlightModel is kept separate from DronePhysics.
    /// </summary>
    public static class CameraSmoothing
    {
        /// <summary>
        /// Exponential-decay smoothing: moves a fraction of the remaining distance to target
        /// each call, where that fraction is derived from <paramref name="smoothSpeed"/> and
        /// <paramref name="deltaTime"/> so the result is frame-rate independent (unlike a
        /// plain Lerp(a, b, constant), which visibly changes behaviour if the frame rate
        /// changes). smoothSpeed &lt;= 0 means "no smoothing" — jump straight to target, which
        /// is how the camera's position should normally behave (rigidly mounted).
        /// </summary>
        public static Vector3 SmoothPosition(Vector3 current, Vector3 target, float smoothSpeed, float deltaTime)
        {
            if (smoothSpeed <= 0f) return target;
            float t = 1f - Mathf.Exp(-smoothSpeed * deltaTime);
            return Vector3.Lerp(current, target, t);
        }

        /// <summary>Same exponential-decay approach as <see cref="SmoothPosition"/>, applied to rotation via Slerp.</summary>
        public static Quaternion SmoothRotation(Quaternion current, Quaternion target, float smoothSpeed, float deltaTime)
        {
            if (smoothSpeed <= 0f) return target;
            float t = 1f - Mathf.Exp(-smoothSpeed * deltaTime);
            return Quaternion.Slerp(current, target, t);
        }
    }
}
