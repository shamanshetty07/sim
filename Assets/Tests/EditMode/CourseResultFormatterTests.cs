using NUnit.Framework;
using Sim.UI;

namespace Sim.Tests.EditMode
{
    /// <summary>Pure formatting tests for the results panel — same pattern as CourseStatusFormatterTests. FormatFinalTime delegates to CourseStatusFormatter.FormatTimer (see that class's own tests for the full NaN/Infinity/negative/over-one-hour coverage); the values here are exactly the ones this phase's brief specifies.</summary>
    public class CourseResultFormatterTests
    {
        [Test]
        public void FormatFinalTime_Zero() => Assert.AreEqual("00:00.00", CourseResultFormatter.FormatFinalTime(0f));

        [Test]
        public void FormatFinalTime_OnePointTwoThreeFour() => Assert.AreEqual("00:01.23", CourseResultFormatter.FormatFinalTime(1.234f));

        [Test]
        public void FormatFinalTime_SixtyOnePointFive() => Assert.AreEqual("01:01.50", CourseResultFormatter.FormatFinalTime(61.5f));

        [Test]
        public void FormatFinalTime_OneHundredTwentyFivePointSixSevenEight() => Assert.AreEqual("02:05.68", CourseResultFormatter.FormatFinalTime(125.678f));

        [Test]
        public void FormatFinalTime_OverOneHour_MinutesDoNotWrap() => Assert.AreEqual("61:01.25", CourseResultFormatter.FormatFinalTime(3661.25f));

        [Test]
        public void FormatFinalTime_NaN_ReturnsSafeFallback() => Assert.AreEqual("--:--.--", CourseResultFormatter.FormatFinalTime(float.NaN));

        [Test]
        public void FormatFinalTime_Infinity_ReturnsSafeFallback() => Assert.AreEqual("--:--.--", CourseResultFormatter.FormatFinalTime(float.PositiveInfinity));

        [Test]
        public void FormatFinalTime_Negative_ReturnsSafeFallback() => Assert.AreEqual("--:--.--", CourseResultFormatter.FormatFinalTime(-5f));

        [Test]
        public void FormatCompletionCount_MatchesCompletedAndTotal() =>
            Assert.AreEqual("15 / 15", CourseResultFormatter.FormatCompletionCount(15, 15));

        [Test]
        public void FormatRecoveryCount_Zero() => Assert.AreEqual("0", CourseResultFormatter.FormatRecoveryCount(0));

        [Test]
        public void FormatRecoveryCount_NonZero() => Assert.AreEqual("2", CourseResultFormatter.FormatRecoveryCount(2));
    }
}
