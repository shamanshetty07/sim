namespace Sim.WorldGeneration.Validation
{
    public enum ValidationSeverity
    {
        /// <summary>Repaired in place (e.g. missing seed generated, an over-limit count clamped) — generation proceeds.</summary>
        Warning,

        /// <summary>Unrecoverable — generation must not proceed on this specification.</summary>
        Error
    }
}
