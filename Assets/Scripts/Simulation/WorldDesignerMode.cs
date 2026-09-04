namespace Sim.Simulation
{
    /// <summary>Which IWorldDesigner RuntimeSimulationBootstrap constructs. Mock requires zero external configuration (no internet, no API keys, no Reactor/OpenAI/Anthropic) — the only mode guaranteed to work out of the box.</summary>
    public enum WorldDesignerMode
    {
        Mock,
        LLM
    }

    /// <summary>Which ILLMClient backs LLMWorldDesigner when WorldDesignerMode.LLM is selected. All three are currently unconfigured stubs (Phase 7) — selecting any of them in LLM mode surfaces a clear "not configured" failure rather than a fake success, regardless of which is picked.</summary>
    public enum LLMProviderKind
    {
        OpenAI,
        Anthropic,
        Local
    }
}
