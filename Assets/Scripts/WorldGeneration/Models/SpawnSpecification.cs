using System.Collections.Generic;
using UnityEngine;

namespace Sim.WorldGeneration.Models
{
    public sealed class SpawnSpecification
    {
        public Vector3 Position { get; set; } = new Vector3(0f, 5f, 0f);
        public Vector3 RotationEuler { get; set; } = Vector3.zero;

        /// <summary>
        /// Optional fallback candidates SpawnGenerator (Phase 9) can try, in order, if the
        /// primary position turns out to be unsafe (inside terrain/an obstacle). Empty list is
        /// fine — the generator's own safe-fallback logic (world origin, above terrain) is the
        /// last resort regardless.
        /// </summary>
        public List<Vector3> AlternateSpawnPoints { get; set; } = new List<Vector3>();
    }
}
