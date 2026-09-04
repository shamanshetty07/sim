using System.Collections.Generic;
using System.Linq;
using Sim.WorldGeneration.Models;

namespace Sim.WorldGeneration.Validation
{
    /// <summary>
    /// Outcome of validating a WorldSpecification: whether it's usable, the possibly-repaired
    /// specification to actually use, and every finding along the way (both the Warnings that
    /// were auto-repaired and any unrecoverable Errors). Data only — the actual validation
    /// logic (limits, clamping, repair rules) is not yet implemented; this type exists so the
    /// pipeline's shape is complete even though one stage is still a placeholder.
    /// </summary>
    public sealed class ValidationResult
    {
        public bool IsValid => Errors.All(e => e.Severity != ValidationSeverity.Error);

        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();

        /// <summary>The specification to actually use — identical to the input if nothing needed repair, or a corrected copy if it did. Null if IsValid is false.</summary>
        public WorldSpecification RepairedSpecification { get; set; }
    }
}
