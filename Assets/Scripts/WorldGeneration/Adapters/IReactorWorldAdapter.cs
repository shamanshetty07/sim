using Sim.WorldGeneration.Models;

namespace Sim.WorldGeneration.Adapters
{
    /// <summary>
    /// Translates a world-generation backend's native result into Unity's normalized
    /// WorldSpecification. This is its own interface (not a static method) specifically so a
    /// different adapter can be swapped in if OpenWorld Reactor's real output shape, once
    /// known, turns out to need substantially different translation logic than the current
    /// best-effort implementation assumes.
    /// </summary>
    public interface IReactorWorldAdapter
    {
        /// <summary>
        /// Converts a ReactorWorldResult into a WorldSpecification. Must never throw on
        /// malformed backend data — a payload it cannot safely interpret should be reported
        /// back some other way (a future AdaptationResult wrapper, once there is a real
        /// payload shape to fail against) rather than crash the pipeline.
        /// </summary>
        WorldSpecification Adapt(ReactorWorldResult result, WorldGenerationRequest originalRequest);
    }
}
