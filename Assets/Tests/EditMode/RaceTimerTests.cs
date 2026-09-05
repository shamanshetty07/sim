using NUnit.Framework;
using Sim.Gameplay;

namespace Sim.Tests.EditMode
{
    /// <summary>RaceTimer tests via a fake IGameplayClock that jumps to any value instantly — no test here sleeps for real seconds.</summary>
    public class RaceTimerTests
    {
        private sealed class FakeGameplayClock : IGameplayClock
        {
            public float NowSeconds { get; set; }
        }

        private FakeGameplayClock _clock;
        private RaceTimer _timer;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeGameplayClock();
            _timer = new RaceTimer(_clock);
        }

        [Test]
        public void Initial_IsNotRunning_ElapsedIsZero()
        {
            Assert.IsFalse(_timer.IsRunning);
            Assert.AreEqual(0f, _timer.ElapsedSeconds);
        }

        [Test]
        public void Start_SetsIsRunning()
        {
            _timer.Start();
            Assert.IsTrue(_timer.IsRunning);
        }

        [Test]
        public void Elapsed_IncreasesWithClockWhileRunning()
        {
            _timer.Start();
            _clock.NowSeconds += 3.5f;
            Assert.AreEqual(3.5f, _timer.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Stop_FreezesElapsedTime()
        {
            _timer.Start();
            _clock.NowSeconds += 4f;
            _timer.Stop();
            _clock.NowSeconds += 10f;

            Assert.IsFalse(_timer.IsRunning);
            Assert.AreEqual(4f, _timer.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Reset_ZeroesElapsedAndStops()
        {
            _timer.Start();
            _clock.NowSeconds += 4f;

            _timer.Reset();

            Assert.IsFalse(_timer.IsRunning);
            Assert.AreEqual(0f, _timer.ElapsedSeconds);
        }

        [Test]
        public void Reset_ThenStart_MeasuresFromZeroAgain()
        {
            _timer.Start();
            _clock.NowSeconds += 4f;
            _timer.Reset();

            _timer.Start();
            _clock.NowSeconds += 1.25f;

            Assert.AreEqual(1.25f, _timer.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Start_WhileAlreadyRunning_DoesNotResetElapsed()
        {
            _timer.Start();
            _clock.NowSeconds += 2f;
            _timer.Start(); // no-op — already running
            _clock.NowSeconds += 1f;

            Assert.AreEqual(3f, _timer.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Stop_WhileNotRunning_DoesNotThrow_LeavesElapsedUnchanged()
        {
            Assert.DoesNotThrow(() => _timer.Stop());
            Assert.AreEqual(0f, _timer.ElapsedSeconds);
        }

        [Test]
        public void StopStartStop_AccumulatesAcrossMultipleRuns()
        {
            _timer.Start();
            _clock.NowSeconds += 2f;
            _timer.Stop();

            _clock.NowSeconds += 100f; // time passing while stopped must not count

            _timer.Start();
            _clock.NowSeconds += 3f;
            _timer.Stop();

            Assert.AreEqual(5f, _timer.ElapsedSeconds, 0.0001f);
        }
    }
}
