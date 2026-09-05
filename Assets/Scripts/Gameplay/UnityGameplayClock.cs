using UnityEngine;

namespace Sim.Gameplay
{
    /// <summary>
    /// Production IGameplayClock — UnityEngine.Time.time (game time; pauses if Time.timeScale
    /// is ever set to 0, which this project never does). Nothing else in Sim.Gameplay reads
    /// UnityEngine.Time directly; this is the single place that boundary is crossed.
    /// </summary>
    public sealed class UnityGameplayClock : IGameplayClock
    {
        public float NowSeconds => Time.time;
    }
}
