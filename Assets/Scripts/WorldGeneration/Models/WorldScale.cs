namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// A coarse size hint for the requested world, e.g. from the user's "huge mountain FPV
    /// course" vs. "small technical track". Deliberately coarse (four buckets, not raw
    /// meters) — it's meant as a hint into WorldGenerationRequest for the world-generation
    /// backend to interpret however it sees fit, not a precise dimension. Precise terrain
    /// dimensions live on TerrainSpecification once a WorldSpecification exists.
    /// </summary>
    public enum WorldScale
    {
        Small,
        Medium,
        Large,
        Huge
    }
}
