namespace Sim.Gameplay
{
    /// <summary>
    /// A small stopwatch over IGameplayClock: Start/Stop/Reset/IsRunning/ElapsedSeconds. Exists
    /// so CourseGameplayController never scatters "Time.time - startTime" math of its own —
    /// per this phase's explicit "do not make gameplay depend directly on Time.time everywhere"
    /// instruction. Driven entirely through IGameplayClock, so it's testable with a fake clock
    /// that jumps to any value instantly rather than a test that sleeps for real seconds.
    /// Supports stop/resume (accumulating elapsed time across multiple Start/Stop pairs)
    /// even though CourseGameplayController currently only ever does one Start and one Stop
    /// per race — no reason to make that assumption load-bearing here.
    /// </summary>
    public sealed class RaceTimer
    {
        private readonly IGameplayClock _clock;

        private float _startedAtSeconds;
        private float _accumulatedSeconds;

        public bool IsRunning { get; private set; }

        public RaceTimer(IGameplayClock clock)
        {
            _clock = clock;
        }

        public float ElapsedSeconds => IsRunning
            ? _accumulatedSeconds + (_clock.NowSeconds - _startedAtSeconds)
            : _accumulatedSeconds;

        public void Start()
        {
            if (IsRunning) return;
            _startedAtSeconds = _clock.NowSeconds;
            IsRunning = true;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _accumulatedSeconds += _clock.NowSeconds - _startedAtSeconds;
            IsRunning = false;
        }

        public void Reset()
        {
            IsRunning = false;
            _accumulatedSeconds = 0f;
            _startedAtSeconds = 0f;
        }
    }
}
