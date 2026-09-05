using NUnit.Framework;
using Sim.Gameplay;
using UnityEngine;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// Unit tests for CheckpointManager in isolation, over a hand-built hierarchy of real
    /// CheckpointTrigger components (this class needs nothing more — no DroneController, no
    /// Rigidbody, so none of the usual Edit-mode Awake() limitations apply here). Broader
    /// coverage of checkpoint generation itself (auto-layout, style-driven spacing) already
    /// lives in WorldGeneratorTests; these tests are specifically about CheckpointManager's own
    /// progression/event contract.
    /// </summary>
    public class CheckpointManagerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private CheckpointManager BuildManager(int count)
        {
            _root = new GameObject("Obstacles");
            for (int i = count - 1; i >= 0; i--) // reverse creation order — order must come from Configure(), not creation/sibling order
            {
                var go = new GameObject($"gate_{i}");
                go.transform.SetParent(_root.transform);
                go.AddComponent<BoxCollider>(); // CheckpointTrigger requires a concrete Collider — see ObstacleGenerator's own identical ordering
                go.AddComponent<CheckpointTrigger>().Configure(i);
            }
            return new CheckpointManager(_root);
        }

        [Test]
        public void Constructor_NullRoot_TotalCheckpointsIsZero()
        {
            var manager = new CheckpointManager(null);
            Assert.AreEqual(0, manager.TotalCheckpoints);
        }

        [Test]
        public void Constructor_CountsAllTriggersRegardlessOfHierarchyOrder()
        {
            CheckpointManager manager = BuildManager(5);
            Assert.AreEqual(5, manager.TotalCheckpoints);
        }

        [Test]
        public void ReportCheckpointPassed_CorrectIndex_AdvancesCurrentAndCompleted()
        {
            CheckpointManager manager = BuildManager(3);
            manager.ReportCheckpointPassed(0);

            Assert.AreEqual(1, manager.CurrentCheckpointIndex);
            Assert.AreEqual(1, manager.CompletedCheckpoints);
        }

        [Test]
        public void ReportCheckpointPassed_WrongIndex_DoesNotAdvance()
        {
            CheckpointManager manager = BuildManager(3);
            manager.ReportCheckpointPassed(1); // gate 2 attempted while gate 1 (index 0) is required

            Assert.AreEqual(0, manager.CurrentCheckpointIndex);
            Assert.AreEqual(0, manager.CompletedCheckpoints);
        }

        [Test]
        public void ReportCheckpointPassed_WrongIndex_RaisesWrongCheckpointAttempted()
        {
            CheckpointManager manager = BuildManager(3);
            int attempted = -1, required = -1;
            manager.WrongCheckpointAttempted += (a, r) => { attempted = a; required = r; };

            manager.ReportCheckpointPassed(2);

            Assert.AreEqual(2, attempted);
            Assert.AreEqual(0, required);
        }

        [Test]
        public void ReportCheckpointPassed_CorrectIndex_DoesNotRaiseWrongCheckpointAttempted()
        {
            CheckpointManager manager = BuildManager(3);
            bool raised = false;
            manager.WrongCheckpointAttempted += (a, r) => raised = true;

            manager.ReportCheckpointPassed(0);

            Assert.IsFalse(raised);
        }

        [Test]
        public void ReportCheckpointPassed_FinalCheckpoint_RaisesRaceFinished()
        {
            CheckpointManager manager = BuildManager(2);
            int finishedCount = 0;
            manager.RaceFinished += () => finishedCount++;

            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);

            Assert.AreEqual(1, finishedCount);
            Assert.IsTrue(manager.IsFinished);
        }

        [Test]
        public void ReportCheckpointPassed_AfterFinished_DoesNotReFireOrAdvance()
        {
            CheckpointManager manager = BuildManager(1);
            int finishedCount = 0;
            manager.RaceFinished += () => finishedCount++;

            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(0); // finished already — must be a no-op

            Assert.AreEqual(1, finishedCount);
            Assert.AreEqual(1, manager.CompletedCheckpoints);
        }

        [Test]
        public void ZeroCheckpoints_IsNeverFinished()
        {
            CheckpointManager manager = BuildManager(0);
            Assert.IsFalse(manager.IsFinished);
        }

        [Test]
        public void Reset_ReturnsIndexAndCompletedToZero()
        {
            CheckpointManager manager = BuildManager(2);
            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);

            manager.Reset();

            Assert.AreEqual(0, manager.CurrentCheckpointIndex);
            Assert.AreEqual(0, manager.CompletedCheckpoints);
            Assert.IsFalse(manager.IsFinished);
        }

        [Test]
        public void Reset_AllowsPassingCheckpointsAgainInOrder()
        {
            CheckpointManager manager = BuildManager(2);
            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);
            manager.Reset();

            int finishedCount = 0;
            manager.RaceFinished += () => finishedCount++;
            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);

            Assert.AreEqual(1, finishedCount);
        }

        // ------------------------------------------------------------------
        // Phase 12 — SetSuppressed, used by DroneRecoveryController to guarantee a mid-race
        // respawn teleport can never accidentally register as passing a checkpoint.
        // ------------------------------------------------------------------

        [Test]
        public void SetSuppressed_True_ReportCheckpointPassed_IsCompleteNoOp()
        {
            CheckpointManager manager = BuildManager(3);
            bool passedRaised = false, wrongRaised = false;
            manager.CheckpointPassed += _ => passedRaised = true;
            manager.WrongCheckpointAttempted += (a, r) => wrongRaised = true;

            manager.SetSuppressed(true);
            manager.ReportCheckpointPassed(0); // correct index — would normally advance
            manager.ReportCheckpointPassed(2); // wrong index — would normally raise WrongCheckpointAttempted

            Assert.AreEqual(0, manager.CurrentCheckpointIndex);
            Assert.AreEqual(0, manager.CompletedCheckpoints);
            Assert.IsFalse(passedRaised);
            Assert.IsFalse(wrongRaised);
        }

        [Test]
        public void SetSuppressed_False_ResumesNormalProcessing()
        {
            CheckpointManager manager = BuildManager(3);
            manager.SetSuppressed(true);
            manager.SetSuppressed(false);

            manager.ReportCheckpointPassed(0);

            Assert.AreEqual(1, manager.CurrentCheckpointIndex);
        }

        [Test]
        public void IsSuppressed_ReflectsSetSuppressed()
        {
            CheckpointManager manager = BuildManager(1);
            Assert.IsFalse(manager.IsSuppressed);

            manager.SetSuppressed(true);
            Assert.IsTrue(manager.IsSuppressed);
        }

        [Test]
        public void CheckpointOrder_DrivenByConfiguredIndex_NotSiblingIndexOrName()
        {
            // BuildManager adds gate_(count-1) first and gate_0 last, so sibling index 0 in the
            // hierarchy is actually the *last* gate, and "gate_0" is alphabetically first only
            // by coincidence of naming, not by structural meaning. Passing indices in the real
            // required order (0, 1, 2) must still work correctly.
            CheckpointManager manager = BuildManager(3);

            manager.ReportCheckpointPassed(0);
            manager.ReportCheckpointPassed(1);
            manager.ReportCheckpointPassed(2);

            Assert.IsTrue(manager.IsFinished);
        }
    }
}
