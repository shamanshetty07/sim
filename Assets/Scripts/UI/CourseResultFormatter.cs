namespace Sim.UI
{
    /// <summary>
    /// Pure formatting for the finished-race results panel — no MonoBehaviour, no UI
    /// dependency, unit-testable on its own (same pattern as TelemetryFormatter/
    /// WorldGenerationStatusFormatter/CourseStatusFormatter). CourseResultsUI is the only thing
    /// that calls into this class.
    ///
    /// Time formatting delegates straight to CourseStatusFormatter.FormatTimer — the exact same
    /// mm:ss.ff format (and the same NaN/Infinity/negative safety fallback) the live race HUD
    /// already uses, so a finished result's displayed time is never computed by a second,
    /// slightly-different formatter. Per this phase's explicit "do not duplicate the timer/
    /// progress logic" instruction.
    /// </summary>
    public static class CourseResultFormatter
    {
        public static string FormatFinalTime(float elapsedSeconds) => CourseStatusFormatter.FormatTimer(elapsedSeconds);

        /// <summary>e.g. "15 / 15" — the raw completed/total count, not the 0-based/capped "next required" semantics CourseStatusFormatter.FormatCheckpointProgress uses for the live HUD (a finished result's CompletedCheckpoints already equals TotalCheckpoints, so there's nothing to cap).</summary>
        public static string FormatCompletionCount(int completedCheckpoints, int totalCheckpoints) =>
            $"{completedCheckpoints} / {totalCheckpoints}";

        public static string FormatRecoveryCount(int recoveryCount) => recoveryCount.ToString();
    }
}
