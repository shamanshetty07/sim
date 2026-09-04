using Sim.WorldGeneration.Models;
using UnityEngine;

namespace Sim.WorldGeneration.Weather
{
    /// <summary>
    /// Configures atmosphere from WeatherSpecification: Unity's built-in fog for
    /// fog/cloudy/rain, a simple particle-system rain effect, and a WindZone for wind. Kept
    /// deliberately simple per this phase's scope — not a claim of AAA weather fidelity.
    /// </summary>
    public sealed class WeatherGenerator
    {
        public GameObject Configure(WeatherSpecification specification, Transform parent, float terrainWidth, float terrainDepth)
        {
            var root = new GameObject("Weather");
            root.transform.SetParent(parent, false);

            string type = (specification.Type ?? "clear").Trim().ToLowerInvariant();

            float fogDensity = Mathf.Clamp01(specification.FogDensity01);
            if (type.Contains("fog")) fogDensity = Mathf.Max(fogDensity, 0.02f);
            if (type.Contains("rain") || type.Contains("cloud")) fogDensity = Mathf.Max(fogDensity, 0.008f);

            RenderSettings.fog = fogDensity > 0f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor = type.Contains("rain") ? new Color(0.55f, 0.58f, 0.62f) : new Color(0.75f, 0.78f, 0.82f);

            if (type.Contains("rain"))
                BuildRainEffect(root.transform, terrainWidth, terrainDepth);

            if (specification.WindStrength01 > 0f)
                BuildWindZone(root.transform, specification.WindStrength01, specification.WindDirection);

            return root;
        }

        /// <summary>A wide, high emission box raining particles straight down — a simple fallback, not a full weather-VFX system.</summary>
        private static void BuildRainEffect(Transform parent, float terrainWidth, float terrainDepth)
        {
            var rainObject = new GameObject("Rain");
            rainObject.transform.SetParent(parent, false);
            rainObject.transform.localPosition = new Vector3(0f, 60f, 0f);

            var particles = rainObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = 3f;
            main.startSpeed = 25f;
            main.startSize = 0.05f;
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 800f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Mathf.Max(terrainWidth, 50f), 1f, Mathf.Max(terrainDepth, 50f));

            // Straight-down motion via constant downward gravity-like acceleration, rather than
            // relying on Unity's gravityModifier (which uses Physics.gravity's direction —
            // fine here since that's already straight down, but VelocityOverLifetime keeps this
            // effect self-contained regardless of the project's global gravity setting).
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(-25f);
        }

        private static void BuildWindZone(Transform parent, float windStrength01, Vector3 windDirection)
        {
            var windObject = new GameObject("Wind");
            windObject.transform.SetParent(parent, false);

            Vector3 direction = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector3.forward;
            windObject.transform.rotation = Quaternion.LookRotation(direction);

            WindZone wind = windObject.AddComponent<WindZone>();
            wind.mode = WindZoneMode.Directional;
            wind.windMain = Mathf.Clamp01(windStrength01) * 3f;
            wind.windTurbulence = Mathf.Clamp01(windStrength01) * 0.5f;
        }
    }
}
