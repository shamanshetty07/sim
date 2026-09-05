using System;

namespace Sim.Gameplay
{
    /// <summary>
    /// Builds exactly one CourseResult snapshot per completed race, the instant
    /// CourseGameplayController.RaceFinished fires — the existing, authoritative finish event
    /// (Phase 11); this class creates no second finish detector and no second timer/checkpoint
    /// tracker. A plain C# class, not a MonoBehaviour — same "constructed once, never recreated"
    /// pattern as CourseGameplayController/DroneRecoveryController.
    ///
    /// Result lifecycle: <see cref="LastResult"/> is cleared (set to null) whenever
    /// CourseGameplayController's own state becomes anything other than Finished. That one rule,
    /// driven entirely by the already-existing StateChanged event, covers every case the brief
    /// calls out without any extra wiring: Restart (Reset() -&gt; Resetting -&gt; Waiting clears
    /// it), a fresh bind after regeneration (-&gt; Waiting clears it), Unbind on Clear World (-&gt;
    /// Waiting clears it), and a bind failure (-&gt; Failed clears it). A fresh result only ever
    /// reappears once a genuinely new RaceFinished fires.
    /// </summary>
    public sealed class CourseResultsController
    {
        private readonly CourseGameplayController _course;
        private readonly DroneRecoveryController _recovery;

        private int _worldSeed;

        /// <summary>The most recent completed run's result, or null if none exists yet or the current course state is not Finished. See class remarks for the exact clearing rule.</summary>
        public CourseResult LastResult { get; private set; }

        /// <summary>Raised exactly once per completed race, with the same instance LastResult now returns.</summary>
        public event Action<CourseResult> ResultsReady;

        public CourseResultsController(CourseGameplayController course, DroneRecoveryController recovery = null)
        {
            _course = course ?? throw new ArgumentNullException(nameof(course));
            _recovery = recovery;

            _course.StateChanged += HandleStateChanged;
            _course.RaceFinished += HandleRaceFinished;
        }

        /// <summary>
        /// Records the current generated world's seed so a result produced later can carry it.
        /// Called once per successful generation (WorldGenerationRuntimeService, alongside
        /// binding Course/Recovery) — deliberately just a plain int, not a WorldSpecification/
        /// WorldGenerationController reference, keeping this class's dependency surface limited
        /// to the two course-gameplay classes it actually consumes.
        /// </summary>
        public void SetWorldSeed(int seed) => _worldSeed = seed;

        private void HandleStateChanged(CourseState state)
        {
            if (state != CourseState.Finished)
                LastResult = null;
        }

        private void HandleRaceFinished()
        {
            var result = new CourseResult(
                elapsedSeconds: _course.ElapsedSeconds,
                completedCheckpoints: _course.CurrentCheckpointIndex,
                totalCheckpoints: _course.TotalCheckpoints,
                recoveryCount: _recovery?.RecoveryCountThisRun ?? 0,
                isCompleted: true,
                worldSeed: _worldSeed);

            LastResult = result;
            ResultsReady?.Invoke(result);
        }
    }
}
