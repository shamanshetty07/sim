using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.WorldGeneration.Lighting
{
    /// <summary>
    /// Configures a single directional light (the sun) plus scene ambient lighting from
    /// LightingSpecification. Uses only Unity's built-in Standard render pipeline lighting —
    /// RenderSettings.ambientLight and a plain Light component — deliberately not URP/HDRP,
    /// matching the project's existing (unmodified) render pipeline configuration.
    /// </summary>
    public sealed class LightingGenerator
    {
        public GameObject Configure(LightingSpecification specification, Transform parent)
        {
            var lightObject = new GameObject("Sun");
            lightObject.transform.SetParent(parent, false);

            Light sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = Mathf.Max(specification.SunIntensity, 0f);
            sun.shadows = LightShadows.Soft;

            lightObject.transform.rotation = SunRotationForTimeOfDay(specification.TimeOfDayHours);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = specification.AmbientColor;

            return lightObject;
        }

        /// <summary>
        /// Maps a 24-hour time-of-day to a sun elevation angle: straight down at solar noon
        /// (12:00), grazing the horizon at sunrise/sunset (6:00/18:00), below the horizon at
        /// night. A single sine curve over the 24-hour cycle — not astronomically precise, a
        /// reasonable visual approximation for a prototype.
        /// </summary>
        private static Quaternion SunRotationForTimeOfDay(float timeOfDayHours)
        {
            float hours = Mathf.Repeat(timeOfDayHours, 24f);
            // -90 at midnight (sun straight up from below, i.e. fully night), +90 at noon
            // (sun straight down) — this is Unity's convention for a directional light's
            // pitch: the light shines along its local +Z, so a positive X-rotation pitches it
            // downward toward the ground.
            float elevationDeg = Mathf.Sin((hours / 24f) * Mathf.PI * 2f - Mathf.PI / 2f) * 90f;
            const float azimuthDeg = 45f; // fixed sun heading — a prototype simplification, not seasonally/geographically accurate
            return Quaternion.Euler(elevationDeg, azimuthDeg, 0f);
        }
    }
}
