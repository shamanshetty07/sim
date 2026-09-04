namespace Sim.AI
{
    public enum WorldGenerationFailureReason
    {
        None,
        NotConfigured,
        NetworkError,
        Timeout,
        InvalidResponse,
        Cancelled,
        Unknown
    }
}
