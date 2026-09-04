using UnityEngine;

namespace Sim.WorldGeneration.Models
{
    public sealed class WeatherSpecification
    {
        /// <summary>Free-form: "clear", "cloudy", "rain", "fog", "wind". Validator (Phase 6+) is responsible for allow-listing/defaulting an unrecognized value — kept a string here for the same expressiveness reasons as TerrainSpecification.TerrainType.</summary>
        public string Type { get; set; } = "clear";

        public float FogDensity01 { get; set; }
        public float WindStrength01 { get; set; }
        public Vector3 WindDirection { get; set; } = Vector3.forward;
    }
}
