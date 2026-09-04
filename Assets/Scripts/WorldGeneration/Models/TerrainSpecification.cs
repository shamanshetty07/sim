namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// Terrain intent for a generated world. <see cref="TerrainType"/> is a free-form string,
    /// not an enum — an enum would be exactly the kind of "restrictive replacement for the
    /// actual generated world" this phase was explicitly told to avoid: it would force
    /// OpenWorld Reactor's/the prompt's terrain description into a fixed list decided in
    /// advance. The trade-off is that terrain-type validity (an allow-list, or at least
    /// sensible fallback behaviour for an unrecognized value) is the Validator's job, not the
    /// type system's — deliberate, not an oversight.
    /// </summary>
    public sealed class TerrainSpecification
    {
        /// <summary>Free-form hint, e.g. "mountain", "desert", "forest", "canyon", "city", "hybrid". Terrain generation (Phase 9) decides how to interpret it, falling back to a reasonable default for anything unrecognized.</summary>
        public string TerrainType { get; set; } = "hills";

        public float Width { get; set; } = 1000f;
        public float Depth { get; set; } = 1000f;
        public float MaxHeight { get; set; } = 100f;

        /// <summary>0 = flat, 1 = extreme height variation.</summary>
        public float HeightVariation01 { get; set; } = 0.4f;

        public bool HasWater { get; set; }

        /// <summary>Free-form hint when HasWater is true, e.g. "waterfalls", "river", "lake". Null/empty if not applicable.</summary>
        public string WaterFeatureHint { get; set; }
    }
}
