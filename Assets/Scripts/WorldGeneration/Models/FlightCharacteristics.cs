namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// Captures what the prompt implies about how the world should *fly*, independent of its
    /// visual biome. This is what stops "a tight technical FPV race through a dense forest"
    /// and "an open desert environment for high-speed FPV flying" from being forced through
    /// the same fixed template just because both prompts might otherwise map to a similar
    /// TerrainSpecification — the two need very different navigable space, obstacle density,
    /// and spacing regardless of terrain type.
    ///
    /// Populated by ReactorWorldAdapter from whatever OpenWorld Reactor infers about flight
    /// intent; Mock/the validator fall back to sensible defaults (open, moderate density) for
    /// a vague prompt like "make something cool" rather than failing.
    /// </summary>
    public sealed class FlightCharacteristics
    {
        public FlightStyle PreferredStyle { get; set; } = FlightStyle.Freestyle;

        /// <summary>0 = wide open (desert cruising), 1 = dense/technical (tight forest gates). Drives spacing between obstacles/environment objects.</summary>
        public float TightnessScore01 { get; set; } = 0.4f;

        /// <summary>0 = sparse, 1 = dense. How many obstacles/environment objects per unit area, independent of raw counts.</summary>
        public float ObstacleDensity01 { get; set; } = 0.4f;

        /// <summary>0 = flat, 1 = lots of climbing/diving/elevation change implied (e.g. mountains vs. flat desert).</summary>
        public float VerticalityScore01 { get; set; } = 0.3f;
    }
}
