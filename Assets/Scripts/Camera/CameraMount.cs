using UnityEngine;

namespace Sim.Camera
{
    /// <summary>
    /// Marker component with no behaviour — identifies the transform on a drone rig where
    /// an FPV camera should attach. Lets other code find the mount via
    /// GetComponentInChildren&lt;CameraMount&gt;() instead of a fragile name-string lookup
    /// (e.g. "find the child called CameraMount"), and gives future world-generation/
    /// persistence code a stable way to locate it on whatever drone was just spawned.
    /// </summary>
    public class CameraMount : MonoBehaviour
    {
    }
}
