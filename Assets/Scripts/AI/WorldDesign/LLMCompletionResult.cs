namespace Sim.AI.WorldDesign
{
    /// <summary>Raw result of one LLM completion call — text only. LLMWorldDesigner is responsible for interpreting Text as JSON; this type makes no assumption about its content.</summary>
    public sealed class LLMCompletionResult
    {
        public bool Success { get; private set; }
        public string Text { get; private set; }
        public string ErrorMessage { get; private set; }

        private LLMCompletionResult() { }

        public static LLMCompletionResult Succeeded(string text) => new LLMCompletionResult { Success = true, Text = text };

        public static LLMCompletionResult Failed(string message) => new LLMCompletionResult { Success = false, ErrorMessage = message };
    }
}
