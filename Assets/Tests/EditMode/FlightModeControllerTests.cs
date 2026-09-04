using NUnit.Framework;
using Sim.Drone;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    public class FlightModeControllerTests
    {
        private DroneConfig _config;
        private FlightModeController _controller;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<DroneConfig>();
            _controller = new FlightModeController(_config);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void StartsUnarmed_InAngleMode()
        {
            Assert.IsFalse(_controller.Armed);
            Assert.AreEqual(FlightMode.Angle, _controller.Mode);
        }

        [Test]
        public void CycleMode_GoesAngleThenHorizonThenAcroThenBackToAngle()
        {
            Assert.AreEqual(FlightMode.Angle, _controller.Mode);
            _controller.CycleMode();
            Assert.AreEqual(FlightMode.Horizon, _controller.Mode);
            _controller.CycleMode();
            Assert.AreEqual(FlightMode.Acro, _controller.Mode);
            _controller.CycleMode();
            Assert.AreEqual(FlightMode.Angle, _controller.Mode);
        }

        [Test]
        public void ToggleArmed_Refused_WhenThrottleAboveSafetyThreshold()
        {
            bool accepted = _controller.ToggleArmed(currentThrottle01: _config.MaxThrottleToArm + 0.2f);
            Assert.IsFalse(accepted);
            Assert.IsFalse(_controller.Armed);
        }

        [Test]
        public void ToggleArmed_Accepted_WhenThrottleAtZero()
        {
            bool accepted = _controller.ToggleArmed(currentThrottle01: 0f);
            Assert.IsTrue(accepted);
            Assert.IsTrue(_controller.Armed);
        }

        [Test]
        public void ToggleArmed_Disarm_AlwaysAccepted_EvenAtFullThrottle()
        {
            _controller.ToggleArmed(0f); // arm first
            bool disarmAccepted = _controller.ToggleArmed(currentThrottle01: 1f);
            Assert.IsTrue(disarmAccepted);
            Assert.IsFalse(_controller.Armed);
        }

        [Test]
        public void ForceDisarm_IsIdempotent_AndAlwaysSucceeds()
        {
            Assert.DoesNotThrow(() => _controller.ForceDisarm());
            _controller.ToggleArmed(0f);
            _controller.ForceDisarm();
            Assert.IsFalse(_controller.Armed);
        }

        [Test]
        public void ModeChanged_Event_FiresOnCycle()
        {
            FlightMode? received = null;
            _controller.ModeChanged += m => received = m;
            _controller.CycleMode();
            Assert.AreEqual(FlightMode.Horizon, received);
        }

        [Test]
        public void ArmedChanged_Event_FiresOnArmAndDisarm()
        {
            int fireCount = 0;
            _controller.ArmedChanged += _ => fireCount++;
            _controller.ToggleArmed(0f);
            _controller.ToggleArmed(0f);
            Assert.AreEqual(2, fireCount);
        }
    }
}
