using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.AI;
using Sim.Core;
using Sim.WorldGeneration.Adapters;
using Sim.WorldGeneration.Validation;

namespace Sim.Tests.EditMode
{
    public class WorldGenerationControllerTests
    {
        private MockWorldGenerationService _mockService;
        private WorldGenerationController _controller;

        [SetUp]
        public void SetUp()
        {
            _mockService = new MockWorldGenerationService();
            _controller = new WorldGenerationController(_mockService, new ReactorWorldAdapter(), new WorldSpecificationValidator());
        }

        [Test]
        public void Constructor_NullService_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new WorldGenerationController(null, new ReactorWorldAdapter(), new WorldSpecificationValidator()));
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
            _mockService.SimulatedDelayMilliseconds = 2000;

            Task generation = _controller.GenerateWorldAsync("Create a mountain course.");
            _controller.Cancel();
            await generation;

            Assert.AreEqual(WorldGenerationState.Cancelled, _controller.State);
        }

        [Test]
        public async Task GenerateWorldAsync_CalledAgain_SupersedesPreviousInFlightAttempt()
        {
            _mockService.SimulatedDelayMilliseconds = 500;
            Task first = _controller.GenerateWorldAsync("First prompt.");

            // Starting a second attempt cancels the first — it must not wait for it. Delay is
            // captured synchronously by the first call's Task.Delay before this line runs (see
            // this test's remarks in the implementation review), so changing it here only
            // affects the second call.
            _mockService.SimulatedDelayMilliseconds = 0;
            await _controller.GenerateWorldAsync("Second prompt.");

            await first; // let the now-superseded, now-cancelled first attempt finish quietly

            Assert.AreEqual(WorldGenerationState.Completed, _controller.State);
            Assert.AreEqual("Second prompt.", _controller.LastValidSpecification.OriginalPrompt);
        }
    }
}
