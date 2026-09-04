namespace Sim.WorldGeneration.Validation
{
    /// <summary>One validation finding against a WorldSpecification field. Data only — WorldSpecificationValidator (not yet implemented, see docs/IMPLEMENTATION_PLAN.md) is what produces these.</summary>
    public sealed class ValidationError
    {
        /// <summary>Dotted path to the offending field, e.g. "Terrain.Width" or "Obstacles[3].Position".</summary>
        public string Field { get; set; }

        public string Message { get; set; }

        public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;

        public ValidationError() { }

        public ValidationError(string field, string message, ValidationSeverity severity = ValidationSeverity.Error)
        {
            Field = field;
            Message = message;
            Severity = severity;
        }
    }
}
