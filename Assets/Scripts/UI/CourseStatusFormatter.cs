using Sim.Gameplay;
using UnityEngine;

namespace Sim.UI
{
    /// <summary>
    /// Pure state -&gt; display-text mapping for course gameplay — no MonoBehaviour, no UI
    /// dependency, so it's unit-testable on its own (same pattern as TelemetryFormatter and
    /// WorldGenerationStatusFormatter). CourseHUD is the only thing that calls into this class.
    /// </summary>
    public static class CourseStatusFormatter
    {
        public static string FormatState(CourseState state, string lastFailureReason)
        {
            switch (state)
            {
                case CourseState.Waiting: return "COURSE READY";
                case CourseState.Countdown: return "GET READY";
                case CourseState.Racing: return "RACING";
                case CourseState.Finished: return "FINISHED";
                case CourseState.Resetting: return "RESETTING";
                case CourseState.Failed:
                    return string.IsNullOrEmpty(lastFailureReason) ? "COURSE UNAVAILABLE" : $"COURSE UNAVAILABLE: {lastFailureReason}";
                default: return string.Empty;
            }
        }

        /// <summary>"3", "2", "1", "GO!" — remainingSeconds counts down from CourseGameplayController.CountdownDurationSeconds to 0.</summary>
        public static string FormatCountdown(float remainingSeconds)
        {
            int wholeSecondsLeft = Mathf.CeilToInt(remainingSeconds);
            return wholeSecondsLeft > 0 ? wholeSecondsLeft.ToString() : "GO!";
        }

        /// <summary>e.g. "3 / 15". currentIndex is 0-based (the next required checkpoint); displayed 1-based and capped at total.</summary>
        public static string FormatCheckpointProgress(int currentIndex, int totalCheckpoints)
        {
            if (totalCheckpoints <= 0) return "-- / --";
            int displayIndex = Mathf.Min(currentIndex + 1, totalCheckpoints);
            return $"{displayIndex} / {totalCheckpoints}";
        }

        /// <summary>"Checkpoint N required" — shown briefly after an out-of-order checkpoint attempt.</summary>
        public static string FormatWrongCheckpoint(int requiredIndex) => $"Checkpoint {requiredIndex + 1} required";

        /// <summary>
        /// mm:ss.ff, e.g. "00:24.81" — matches the format used throughout this phase's spec/
        /// docs. Rounds to whole centiseconds first, then splits, so 59.997s displays as
        /// "01:00.00" rather than rolling over to an invalid "00:60.00". Minutes are never
        /// wrapped — 3661.25s correctly displays as "61:01.25", not "01:01.25". NaN/Infinity/
        /// negative values (never legitimate elapsed time) return "--:--.--" rather than
        /// propagating garbage into the UI or crashing on the cast to int — added Phase 13 for
        /// the results panel, and applies here too since the live HUD calls this same method.
        /// </summary>
        public static string FormatTimer(float elapsedSeconds)
        {
            if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0f)
                return "--:--.--";

            int totalCentiseconds = Mathf.RoundToInt(elapsedSeconds * 100f);
            int minutes = totalCentiseconds / 6000;
            int seconds = (totalCentiseconds / 100) % 60;
            int centiseconds = totalCentiseconds % 100;
            return $"{minutes:D2}:{seconds:D2}.{centiseconds:D2}";
        }

        public static bool IsStartAvailable(CourseState state) => state == CourseState.Waiting;

        public static bool IsResetAvailable(CourseState state) =>
            state == CourseState.Racing || state == CourseState.Finished || state == CourseState.Countdown;
    }
}
