using UnityEngine;

namespace Sim.Utilities
{
    /// <summary>
    /// Destroys a Unity Object correctly regardless of whether the caller is running at Play
    /// time or Edit time — the two require different API calls
    /// (<see cref="Object.Destroy"/> is deferred to end-of-frame and is invalid outside Play
    /// mode; <see cref="Object.DestroyImmediate"/> is immediate but discouraged during normal
    /// Play-mode gameplay). Generator code in this project can legitimately run from either
    /// context (an Editor tool, or eventually a runtime "Generate World" button), so this
    /// check belongs in one shared place rather than being duplicated at every call site.
    /// </summary>
    public static class UnityLifecycleUtility
    {
        public static void DestroySafely(Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
    }
}
