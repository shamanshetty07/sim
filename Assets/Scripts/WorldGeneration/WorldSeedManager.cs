using System;
using Sim.Utilities;

namespace Sim.WorldGeneration
{
    /// <summary>
    /// Turns one master seed into an independent, deterministic <see cref="System.Random"/>
    /// per generation stage ("terrain", "environment", "obstacles", ...). Each stage gets its
    /// own stream so that, say, adding an extra tree category doesn't reshuffle obstacle
    /// placement — the two stages never draw from the same sequence of numbers.
    ///
    /// Deliberately never touches <c>UnityEngine.Random</c> (global, mutable, shared state that
    /// any other code — including Unity's own internals — can perturb) — every generator in
    /// this pipeline takes a <see cref="System.Random"/> instance from here instead.
    /// </summary>
    public sealed class WorldSeedManager
    {
        private readonly int _masterSeed;

        public WorldSeedManager(int masterSeed)
        {
            _masterSeed = masterSeed;
        }

        /// <summary>A fresh, deterministic RNG for the named stage. Calling this twice with the same stage name in the same process returns two independent Random instances seeded identically — both will produce the same sequence.</summary>
        public Random GetRandomForStage(string stageName)
        {
            int stageSeed = StableHash.Fnv1a($"{_masterSeed}:{stageName}");
            return new Random(stageSeed);
        }
    }
}
