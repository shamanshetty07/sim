using UnityEngine;

namespace Sim.WorldGeneration.Models
{
    public sealed class LightingSpecification
    {
        /// <summary>0-24. Fractional hours are fine (e.g. 16.5 = 4:30pm).</summary>
        public float TimeOfDayHours { get; set; } = 12f;

        public float SunIntensity { get; set; } = 1.2f;

        public Color AmbientColor { get; set; } = new Color(0.5f, 0.5f, 0.55f);
    }
}
