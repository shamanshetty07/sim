using UnityEngine;

namespace Sim.WorldGeneration.Environment
{
    /// <summary>
    /// Maps a free-form category/type string (from ObjectSpecification.Category or
    /// ObstacleSpecification.Type) to an instantiated GameObject. Its own interface so a real
    /// asset-backed implementation can be swapped in later without touching
    /// EnvironmentGenerator/ObstacleGenerator — neither ever calls Resources.Load or hardcodes
    /// an asset path itself; only an implementation of this interface does.
    /// </summary>
    public interface IWorldPrefabRegistry
    {
        /// <summary>
        /// Creates and parents a new instance for <paramref name="category"/> under
        /// <paramref name="parent"/>, at the origin (callers set position/rotation/scale
        /// afterward). Never returns null — an unrecognized category still gets a primitive
        /// fallback (a small labeled placeholder), per the project's asset-placement rule.
        /// </summary>
        GameObject CreateInstance(string category, Transform parent);
    }
}
