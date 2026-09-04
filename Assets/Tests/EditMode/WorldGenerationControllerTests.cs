using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI.WorldDesign;
using Sim.Core;
using Sim.WorldGeneration.Validation;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Migrated Phase 8 to drive MockWorldDesigner instead of the Reactor-shaped
    /// MockWorldGenerationService/ReactorWorldAdapter — see WorldGenerationController's own
    /// class remarks for why. Test names/coverage are otherwise unchanged from Phase 6.
    /// </summary>
    public class WorldGenerationControllerTests
    {
        private MockWorldDesigner _mockDesigner;
        private WorldGenerationController _controller;

        [SetUp]
        public void SetUp()
        {
            _mockDesigner = new MockWorldDesigner();
            _controller = new WorldGenerationController(_mockDesigner, new WorldSpecificationValidator());
        }

        [Test]
        public void Constructor_NullDesigner_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new WorldGenerationController(null, new WorldSpecificationValidator()));
        }

        [Test]
        public void Constructor_NullValidator_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new WorldGenerationController(new MockWorldDesigner(), null));
        }

        [Test]
        public void InitialState_IsIdle()
        {
            Assert.AreEqual(WorldGenerationState.Idle, _controller.State);
        }

        [Test]
        public async Task GenerateWorldAsync_Success_EndsInCompleted_WithSpecification()
        {
            await _controller.GenerateWorldAsync("Create a mountain FPV course with cliffs and rocks.");

            Assert.AreEqual(WorldGenerationState.Completed, _controller.State);
            Assert.IsNotNull(_controller.LastValidSpecification);
        }

        [Test]
        public async Task GenerateWorldAsync_PreservesOriginalPromptThroughFullPipeline()
        {
            const string prompt = "Create a desert canyon FPV racing course with tunnels, large rocks and 12 gates.";
            await _controller.GenerateWorldAsync(prompt);

            Assert.AreEqual(prompt, _controller.LastValidSpecification.OriginalPrompt);
        }

        [Test]
        public async Task GenerateWorldAsync_EmptyPrompt_EndsInFailed_DoesNotThrow()
        {
            await _controller.GenerateWorldAsync("");

            Assert.AreEqual(WorldGenerationState.Failed, _controller.State);
            Assert.IsNotNull(_controller.LastErrorMessage);
        }

        [Test]
        public async Task GenerateWorldAsync_StateTransitions_FireInOrder()
        {
            var seen = new List<WorldGenerationState>();
            _controller.StateChanged += s => seen.Add(s);

            await _controller.GenerateWorldAsync("Create a forest obstacle course.");

            CollectionAssert.AreEqual(
                new[] { WorldGenerationState.Requesting, WorldGenerationState.Validating, WorldGenerationState.Completed },
                seen);
        }

        [Test]
        public void Cancel_WithNothingInFlight_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.Cancel());
        }

        [Test]
        public async Task GenerateWorldAsync_CancelledBeforeCompletion_EndsInCancelled()
        {
            _mockDesigner.SimulatedDelayMilliseconds = 2000;

            Task generation = _controller.GenerateWorldAsync("Create a mountain course.");
            _controller.Cancel();
            await generation;

            Assert.AreEqual(WorldGenerationState.Cancelled, _controller.State);
        }

        [Test]
        public async Task GenerateWorldAsync_CalledAgain_SupersedesPreviousInFlightAttempt()
        {
            _mockDesigner.SimulatedDelayMilliseconds = 500;
            Task first = _controller.GenerateWorldAsync("First prompt.");

            // Starting a second attempt cancels the first — it must not wait for it. Delay is
            // captured synchronously by the first call's Task.Delay before this line runs, so
            // changing it here only affects the second call (see MockWorldGenerationServiceTests
            // for the same reasoning spelled out in more detail).
            _mockDesigner.SimulatedDelayMilliseconds = 0;
            await _controller.GenerateWorldAsync("Second prompt.");

            await first; // let the now-superseded, now-cancelled first attempt finish quietly

            Assert.AreEqual(WorldGenerationState.Completed, _controller.State);
            Assert.AreEqual("Second prompt.", _controller.LastValidSpecification.OriginalPrompt);
        }
    }
}
