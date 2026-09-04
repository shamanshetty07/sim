namespace Sim.Core
{
    /// <summary>Observable lifecycle state of one world-generation attempt. A future UI (Phase 8) drives its Generate/Cancel/Retry buttons and progress display off this alone.</summary>
    public enum WorldGenerationState
    {
        Idle,
        Requesting,
        Validating,
        Completed,
        Failed,
        Cancelled
    }
}
