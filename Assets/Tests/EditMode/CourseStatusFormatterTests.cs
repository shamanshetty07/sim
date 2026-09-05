using NUnit.Framework;
using Sim.Gameplay;
using Sim.UI;

namespace Sim.Tests.EditMode
{
    /// <summary>Pure formatting tests — same pattern as TelemetryFormatterTests/WorldGenerationStatusFormatterTests: no MonoBehaviour, no scene, just input -&gt; expected string.</summary>
    public class CourseStatusFormatterTests
    {
        [Test]
        public void FormatState_Waiting() => Assert.AreEqual("COURSE READY", CourseStatusFormatter.FormatState(CourseState.Waiting, null));

        [Test]
        public void FormatState_Countdown() => Assert.AreEqual("GET READY", CourseStatusFormatter.FormatState(CourseState.Countdown, null));

        [Test]
        public void FormatState_Racing() => Assert.AreEqual("RACING", CourseStatusFormatter.FormatState(CourseState.Racing, null));

        [Test]
        public void FormatState_Finished() => Assert.AreEqual("FINISHED", CourseStatusFormatter.FormatState(CourseState.Finished, null));

        [Test]
        public void FormatState_Failed_IncludesReason() =>
            Assert.AreEqual("COURSE UNAVAILABLE: no gates", CourseStatusFormatter.FormatState(CourseState.Failed, "no gates"));

        [Test]
        public void FormatState_Failed_NoReason_StillReadable() =>
            Assert.AreEqual("COURSE UNAVAILABLE", CourseStatusFormatter.FormatState(CourseState.Failed, null));

        [Test]
        public void FormatCountdown_ThreeSecondsRemaining_Shows3() =>
            Assert.AreEqual("3", CourseStatusFormatter.FormatCountdown(2.6f));

        [Test]
        public void FormatCountdown_ZeroRemaining_ShowsGo() =>
            Assert.AreEqual("GO!", CourseStatusFormatter.FormatCountdown(0f));

        [Test]
        public void FormatCheckpointProgress_BeforeAnyPass_ShowsOneBased() =>
            Assert.AreEqual("1 / 15", CourseStatusFormatter.FormatCheckpointProgress(0, 15));

        [Test]
        public void FormatCheckpointProgress_AfterSomePasses() =>
            Assert.AreEqual("4 / 15", CourseStatusFormatter.FormatCheckpointProgress(3, 15));

        [Test]
        public void FormatCheckpointProgress_AllPassed_CapsAtTotal() =>
            Assert.AreEqual("15 / 15", CourseStatusFormatter.FormatCheckpointProgress(15, 15));

        [Test]
        public void FormatCheckpointProgress_NoCheckpoints_ShowsPlaceholder() =>
            Assert.AreEqual("-- / --", CourseStatusFormatter.FormatCheckpointProgress(0, 0));

        [Test]
        public void FormatWrongCheckpoint_ShowsOneBasedRequiredIndex() =>
            Assert.AreEqual("Checkpoint 1 required", CourseStatusFormatter.FormatWrongCheckpoint(0));

        [Test]
        public void FormatTimer_Zero() => Assert.AreEqual("00:00.00", CourseStatusFormatter.FormatTimer(0f));

        [Test]
        public void FormatTimer_UnderOneMinute() => Assert.AreEqual("00:24.81", CourseStatusFormatter.FormatTimer(24.81f));

        [Test]
        public void FormatTimer_OverOneMinute() => Assert.AreEqual("01:42.37", CourseStatusFormatter.FormatTimer(102.37f));

        [Test]
        public void FormatTimer_RoundsUpToNextSecond_DoesNotProduce60() =>
            Assert.AreEqual("01:00.00", CourseStatusFormatter.FormatTimer(59.997f));

        [Test]
        public void FormatTimer_OverOneHour_MinutesDoNotWrap() =>
            Assert.AreEqual("61:01.25", CourseStatusFormatter.FormatTimer(3661.25f));

        [Test]
        public void FormatTimer_NaN_ReturnsSafeFallback() =>
            Assert.AreEqual("--:--.--", CourseStatusFormatter.FormatTimer(float.NaN));

        [Test]
        public void FormatTimer_PositiveInfinity_ReturnsSafeFallback() =>
            Assert.AreEqual("--:--.--", CourseStatusFormatter.FormatTimer(float.PositiveInfinity));

        [Test]
        public void FormatTimer_NegativeInfinity_ReturnsSafeFallback() =>
            Assert.AreEqual("--:--.--", CourseStatusFormatter.FormatTimer(float.NegativeInfinity));

        [Test]
        public void FormatTimer_Negative_ReturnsSafeFallback() =>
            Assert.AreEqual("--:--.--", CourseStatusFormatter.FormatTimer(-1f));

        [Test]
        public void IsStartAvailable_OnlyWhenWaiting()
        {
            Assert.IsTrue(CourseStatusFormatter.IsStartAvailable(CourseState.Waiting));
            Assert.IsFalse(CourseStatusFormatter.IsStartAvailable(CourseState.Countdown));
            Assert.IsFalse(CourseStatusFormatter.IsStartAvailable(CourseState.Racing));
            Assert.IsFalse(CourseStatusFormatter.IsStartAvailable(CourseState.Finished));
            Assert.IsFalse(CourseStatusFormatter.IsStartAvailable(CourseState.Failed));
        }

        [Test]
        public void IsResetAvailable_WhenCountdownRacingOrFinished()
        {
            Assert.IsFalse(CourseStatusFormatter.IsResetAvailable(CourseState.Waiting));
            Assert.IsTrue(CourseStatusFormatter.IsResetAvailable(CourseState.Countdown));
            Assert.IsTrue(CourseStatusFormatter.IsResetAvailable(CourseState.Racing));
            Assert.IsTrue(CourseStatusFormatter.IsResetAvailable(CourseState.Finished));
            Assert.IsFalse(CourseStatusFormatter.IsResetAvailable(CourseState.Failed));
        }
    }
}
