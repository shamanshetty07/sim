namespace Sim.Gameplay
{
    /// <summary>
    /// The one thing RaceTimer/CourseGameplayController need from "time": a monotonically
    /// increasing number of seconds. Its own interface (rather than reading UnityEngine.Time
    /// directly, scattered across gameplay code) specifically so both are unit-testable with a
    /// fake clock that jumps instantly to any value — tests must not sleep for real seconds to
    /// verify timer/countdown behaviour. UnityGameplayClock is the real, production
    /// implementation.
    /// </summary>
    public interface IGameplayClock
    {
        float NowSeconds { get; }
    }
}
