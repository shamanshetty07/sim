using NUnit.Framework;
using Sim.Camera;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Covers what's testable about FPVCameraController without Play mode: default
    /// configuration values and that ApplyLensSettings pushes FieldOfView onto the actual
    /// Camera component. LateUpdate's follow/smoothing behaviour is not covered here — that
    /// requires a running Transform hierarchy over time, which is Play-mode territory (see
    /// docs/FPV_CAMERA_AND_OSD.md's manual verification checklist) — but its math is covered
    /// directly by CameraSmoothingTests.
    /// </summary>
    public class FPVCameraControllerTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void AddingComponent_AutoAddsCameraComponent_ViaRequireComponent()
        {
            _go = new GameObject("TestCamera");
            var controller = _go.AddComponent<FPVCameraController>();

            Assert.IsNotNull(_go.GetComponent<UnityEngine.Camera>());
            Assert.IsNotNull(controller);
        }

        [Test]
        public void DefaultConfiguration_IsWithinDocumentedRanges()
        {
            _go = new GameObject("TestCamera");
            var controller = _go.AddComponent<FPVCameraController>();

            Assert.GreaterOrEqual(controller.FieldOfView, 60f);
            Assert.LessOrEqual(controller.FieldOfView, 170f);
            Assert.IsNull(controller.Mount, "No mount should be assigned until SetMount is called.");
        }

        [Test]
        public void ApplyLensSettings_SetsCameraFieldOfView()
        {
            _go = new GameObject("TestCamera");
            var controller = _go.AddComponent<FPVCameraController>();
            var camera = _go.GetComponent<UnityEngine.Camera>();

            camera.fieldOfView = 40f; // deliberately wrong, to prove ApplyLensSettings corrects it
            controller.ApplyLensSettings();

            Assert.AreEqual(controller.FieldOfView, camera.fieldOfView, 0.001f);
        }

        [Test]
        public void SetMount_AssignsMount()
        {
            _go = new GameObject("TestCamera");
            var controller = _go.AddComponent<FPVCameraController>();
            var mountGO = new GameObject("Mount");

            controller.SetMount(mountGO.transform);

            Assert.AreEqual(mountGO.transform, controller.Mount);
            Object.DestroyImmediate(mountGO);
        }
    }
}
