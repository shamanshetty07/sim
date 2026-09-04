using NUnit.Framework;
using Sim.Drone;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Exercises DroneFlightModel.Compute directly — no Rigidbody, no scene, no Play mode.
    /// This is possible specifically because the flight model is pure math (see the
    /// class-level remarks on DroneFlightModel).
    /// </summary>
    public class DroneFlightModelTests
    {
        private DroneConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<DroneConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        private static DroneInputSample Sample(float throttle = 0f, float pitch = 0f, float roll = 0f, float yaw = 0f)
            => new DroneInputSample(throttle, pitch, roll, yaw, false, false, false);

        private static readonly DroneAttitudeState LevelAtRest = new DroneAttitudeState(0f, 0f, Vector3.zero);

        [Test]
        public void ZeroThrottle_ProducesZeroThrust()
        {
            FlightOutput output = DroneFlightModel.Compute(Sample(throttle: 0f), FlightMode.Angle, LevelAtRest, _config);
            Assert.AreEqual(0f, output.ThrustForceNewtons, 0.0001f);
        }

        [Test]
        public void FullThrottle_ProducesMaxThrust()
        {
            FlightOutput output = DroneFlightModel.Compute(Sample(throttle: 1f), FlightMode.Angle, LevelAtRest, _config);
            Assert.AreEqual(_config.MaxThrustForce, output.ThrustForceNewtons, 0.0001f);
        }

        [Test]
        public void NoInput_LevelAttitude_AtRest_ProducesNoTorque()
        {
            // The one true equilibrium: level, stationary, centered sticks -> the rate loop's
            // target and current rate are both zero on every axis, so torque must be exactly zero.
            foreach (FlightMode mode in new[] { FlightMode.Angle, FlightMode.Acro, FlightMode.Horizon })
            {
                FlightOutput output = DroneFlightModel.Compute(Sample(), mode, LevelAtRest, _config);
                Assert.AreEqual(Vector3.zero, output.LocalTorque, $"Mode {mode} should be at equilibrium with no input.");
            }
        }

        [Test]
        public void AcroMode_RollInput_ProducesTorqueInStickDirection()
        {
            FlightOutput positive = DroneFlightModel.Compute(Sample(roll: 1f), FlightMode.Acro, LevelAtRest, _config);
            FlightOutput negative = DroneFlightModel.Compute(Sample(roll: -1f), FlightMode.Acro, LevelAtRest, _config);

            Assert.Greater(positive.LocalTorque.z, 0f);
            Assert.Less(negative.LocalTorque.z, 0f);
            // Symmetric stick input should produce symmetric torque magnitude.
            Assert.AreEqual(positive.LocalTorque.z, -negative.LocalTorque.z, 0.0001f);
        }

        [Test]
        public void AcroMode_IgnoresCurrentAttitude_OnlyRates()
        {
            // Acro is open-loop on angle: a large existing tilt with zero input and zero
            // angular velocity should still produce zero torque, unlike Angle/Horizon.
            var tiltedButStill = new DroneAttitudeState(45f, 45f, Vector3.zero);
            FlightOutput output = DroneFlightModel.Compute(Sample(), FlightMode.Acro, tiltedButStill, _config);
            Assert.AreEqual(0f, output.LocalTorque.x, 0.0001f);
            Assert.AreEqual(0f, output.LocalTorque.z, 0.0001f);
        }

        [Test]
        public void AngleMode_SelfLevels_WhenTiltedWithNoInput()
        {
            // Tilted with centered sticks: Angle mode must generate corrective torque back
            // toward level, i.e. toward the opposite sign of the existing tilt.
            var tiltedRight = new DroneAttitudeState(0f, 20f, Vector3.zero);
            FlightOutput output = DroneFlightModel.Compute(Sample(), FlightMode.Angle, tiltedRight, _config);
            Assert.Less(output.LocalTorque.z, 0f, "Angle mode should correct a positive roll tilt with negative roll torque.");
        }

        [Test]
        public void AngleMode_TargetRate_IsClampedToMaxRate()
        {
            // A huge angle error (e.g. spawned upside down) must not command an unbounded rate.
            var extremeTilt = new DroneAttitudeState(0f, -179f, Vector3.zero);
            FlightOutput output = DroneFlightModel.Compute(Sample(roll: 1f), FlightMode.Angle, extremeTilt, _config);
            float impliedRateError = output.LocalTorque.z / _config.RollRateGain;
            Assert.LessOrEqual(Mathf.Abs(impliedRateError), _config.MaxPitchRollRateDegPerSec + 0.001f);
        }

        [Test]
        public void HorizonMode_AtCenterStick_MatchesAngleMode()
        {
            var tilted = new DroneAttitudeState(0f, 15f, Vector3.zero);
            FlightOutput angle = DroneFlightModel.Compute(Sample(), FlightMode.Angle, tilted, _config);
            FlightOutput horizon = DroneFlightModel.Compute(Sample(), FlightMode.Horizon, tilted, _config);
            Assert.AreEqual(angle.LocalTorque.z, horizon.LocalTorque.z, 0.0001f);
        }

        [Test]
        public void HorizonMode_AtFullStick_MatchesAcroMode()
        {
            FlightOutput acro = DroneFlightModel.Compute(Sample(roll: 1f), FlightMode.Acro, LevelAtRest, _config);
            FlightOutput horizon = DroneFlightModel.Compute(Sample(roll: 1f), FlightMode.Horizon, LevelAtRest, _config);
            Assert.AreEqual(acro.LocalTorque.z, horizon.LocalTorque.z, 0.0001f);
        }

        [Test]
        public void YawMode_IsRateControlled_InEveryFlightMode()
        {
            // Yaw never gets an angle outer loop — identical yaw input/attitude should produce
            // identical yaw torque regardless of flight mode.
            FlightOutput angle = DroneFlightModel.Compute(Sample(yaw: 0.5f), FlightMode.Angle, LevelAtRest, _config);
            FlightOutput acro = DroneFlightModel.Compute(Sample(yaw: 0.5f), FlightMode.Acro, LevelAtRest, _config);
            FlightOutput horizon = DroneFlightModel.Compute(Sample(yaw: 0.5f), FlightMode.Horizon, LevelAtRest, _config);

            Assert.AreEqual(angle.LocalTorque.y, acro.LocalTorque.y, 0.0001f);
            Assert.AreEqual(angle.LocalTorque.y, horizon.LocalTorque.y, 0.0001f);
        }

        [Test]
        public void YawDamping_OpposesExistingYawRate_WithNoInput()
        {
            var spinning = new DroneAttitudeState(0f, 0f, new Vector3(0f, 100f, 0f));
            FlightOutput output = DroneFlightModel.Compute(Sample(), FlightMode.Acro, spinning, _config);
            Assert.Less(output.LocalTorque.y, 0f, "Existing positive yaw rate with no input should be damped, not amplified.");
        }

        [Test]
        public void NullConfig_ReturnsZeroOutput_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                FlightOutput output = DroneFlightModel.Compute(Sample(throttle: 1f, pitch: 1f), FlightMode.Acro, LevelAtRest, null);
                Assert.AreEqual(FlightOutput.Zero.ThrustForceNewtons, output.ThrustForceNewtons);
            });
        }
    }
}
