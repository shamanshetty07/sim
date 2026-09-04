namespace Sim.WorldGeneration.Models
{
    /// <summary>The kind of FPV flying a generated world is meant to support. Drives generation density/spacing, not drone physics — DroneConfig is unaffected.</summary>
    public enum FlightStyle
    {
        /// <summary>Fast, open, minimal obstacles — e.g. "an open desert environment for high-speed FPV flying."</summary>
        Cruise,

        /// <summary>Gate/checkpoint sequence, moderate obstacle density.</summary>
        Race,

        /// <summary>Open space for tricks/stunts around obstacles, not necessarily a fixed course.</summary>
        Freestyle,

        /// <summary>Tight, dense, precision-focused — e.g. "a tight technical FPV race through a dense forest."</summary>
        Technical
    }
}
