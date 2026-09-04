using NUnit.Framework;
using Sim.Drone;
using Sim.UI;

namespace Sim.Tests.EditMode
{
    public class TelemetryFormatterTests
    {
        [TestCase(FlightMode.Angle, "ANGLE")]
        [TestCase(FlightMode.Acro, "ACRO")]
        [TestCase(FlightMode.Horizon, "HORIZON")]
        public void FormatMode_ReturnsUppercaseModeName(FlightMode mode, string expected)
        {
            Assert.AreEqual(expected, TelemetryFormatter.FormatMode(mode));
        }

        [Test]
        public void FormatArmed_TrueIsArmed_FalseIsDisarmed()
        {
            Assert.AreEqual("ARMED", TelemetryFormatter.FormatArmed(true));
            Assert.AreEqual("DISARMED", TelemetryFormatter.FormatArmed(false));
        }

        [TestCase(0f, "0%")]
        [TestCase(0.5f, "50%")]
        [TestCase(1f, "100%")]
        [TestCase(1.5f, "100%")] // clamped, even though the raw value is out of range
        [TestCase(-0.5f, "0%")]  // clamped
        public void FormatPercent_ClampsAndRounds(float value01, string expected)
        {
            Assert.AreEqual(expected, TelemetryFormatter.FormatPercent(value01));
        }

        [Test]
        public void FormatMeters_OneDecimalPlace()
        {
            Assert.AreEqual("25.4 m", TelemetryFormatter.FormatMeters(25.4f));
        }

        [Test]
        public void FormatVerticalSpeed_AlwaysSigned()
        {
            Assert.AreEqual("+2.4 m/s", TelemetryFormatter.FormatVerticalSpeed(2.4f));
            Assert.AreEqual("-1.1 m/s", TelemetryFormatter.FormatVerticalSpeed(-1.1f));
            Assert.AreEqual("+0.0 m/s", TelemetryFormatter.FormatVerticalSpeed(0f));
        }

        [TestCase(12.4f, "12°")]
        [TestCase(-4.6f, "-5°")]
        [TestCase(182f, "182°")]
        public void FormatDegrees_RoundsToWholeDegree(float degrees, string expected)
        {
            Assert.AreEqual(expected, TelemetryFormatter.FormatDegrees(degrees));
        }

        [Test]
        public void FormatFps_RoundsToWholeNumber()
        {
            Assert.AreEqual("60 FPS", TelemetryFormatter.FormatFps(59.6f));
            Assert.AreEqual("144 FPS", TelemetryFormatter.FormatFps(144.4f));
        }
    }
}
