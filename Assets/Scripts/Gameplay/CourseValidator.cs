namespace Sim.Gameplay
{
    /// <summary>
    /// Gameplay-level validation only — WorldSpecificationValidator
    /// (Sim.WorldGeneration.Validation) remains the sole authority on WorldSpecification
    /// validity; this checks the one thing that validator cannot, because it runs before any
    /// Unity object exists: whether the CheckpointManager an actual generation produced is
    /// something a course can run on at all.
    ///
    /// Deliberately minimal. ObstacleGenerator always pairs a CheckpointDefinition with a real
    /// CheckpointTrigger in the same call (see its own remarks), and CheckpointManager counts
    /// live trigger components directly rather than re-reading specification entries — so "a
    /// checkpoint index exists in the spec but has no trigger/collider" or "a trigger exists on
    /// a destroyed/missing object" cannot happen by construction. The one thing actually left
    /// to check here is emptiness: zero checkpoints, or no manager at all.
    /// </summary>
    public static class CourseValidator
    {
        public static bool IsValid(CheckpointManager checkpointManager, out string failureReason)
        {
            if (checkpointManager == null)
            {
                failureReason = "No checkpoint data was generated for this world.";
                return false;
            }

            if (checkpointManager.TotalCheckpoints <= 0)
            {
                failureReason = "The generated world has no checkpoints — a course needs at least one gate.";
                return false;
            }

            failureReason = null;
            return true;
        }
    }
}
