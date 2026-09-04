using Sim.WorldGeneration.Models;

namespace Sim.WorldGeneration.Validation
{
    /// <summary>
    /// Validates (and where possible repairs) a WorldSpecification before it's allowed to
    /// reach WorldGenerator (Phase 7+). This is the boundary that stops invalid generated
    /// data — from a bug in ReactorWorldAdapter, a malformed backend response, or anything
    /// else upstream — from ever reaching Unity object-creation code and crashing/misbehaving.
    /// </summary>
    public interface IWorldSpecificationValidator
    {
        ValidationResult Validate(WorldSpecification specification);
    }
}
