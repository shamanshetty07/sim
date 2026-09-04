using NUnit.Framework;
using Sim.Camera;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    public class CameraSmoothingTests
    {
        [Test]
        public void SmoothPosition_ZeroSpeed_JumpsStraightToTarget()
        {
            Vector3 result = CameraSmoothing.SmoothPosition(Vector3.zero, new Vector3(5f, 0f, 0f), 0f, 0.016f);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), result);
        }

        [Test]
        public void SmoothPosition_PositiveSpeed_MovesTowardTarget_ButNotPastIt()
        {
            Vector3 current = Vector3.zero;
            Vector3 target = new Vector3(10f, 0f, 0f);
            Vector3 result = CameraSmoothing.SmoothPosition(current, target, 5f, 0.016f);

            Assert.Greater(result.x, current.x, "Should move toward the target.");
            Assert.Less(result.x, target.x, "A single small step should not overshoot the target.");
        }

        [Test]
        public void SmoothPosition_LargerDeltaTime_MovesFartherTowardTarget()
        {
            Vector3 target = new Vector3(10f, 0f, 0f);
            Vector3 smallStep = CameraSmoothing.SmoothPosition(Vector3.zero, target, 5f, 0.016f);
            Vector3 largeStep = CameraSmoothing.SmoothPosition(Vector3.zero, target, 5f, 0.5f);

            Assert.Greater(largeStep.x, smallStep.x);
        }

        [Test]
        public void SmoothPosition_VeryLargeDeltaTime_ConvergesNearTarget()
        {
            Vector3 target = new Vector3(10f, -3f, 2f);
            Vector3 result = CameraSmoothing.SmoothPosition(Vector3.zero, target, 5f, 10f);
            Assert.Less(Vector3.Distance(result, target), 0.01f);
        }

        [Test]
        public void SmoothRotation_ZeroSpeed_JumpsStraightToTarget()
        {
            Quaternion target = Quaternion.Euler(0f, 90f, 0f);
            Quaternion result = CameraSmoothing.SmoothRotation(Quaternion.identity, target, 0f, 0.016f);
            Assert.AreEqual(target, result);
        }

        [Test]
        public void SmoothRotation_PositiveSpeed_MovesPartwayToTarget()
        {
            Quaternion target = Quaternion.Euler(0f, 90f, 0f);
            Quaternion result = CameraSmoothing.SmoothRotation(Quaternion.identity, target, 5f, 0.016f);

            float angleToTarget = Quaternion.Angle(result, target);
            Assert.Greater(angleToTarget, 0f, "Should not have reached the target in a single small step.");
            Assert.Less(angleToTarget, 90f, "Should have moved at least somewhat toward the target.");
        }
    }
}
