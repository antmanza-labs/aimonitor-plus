namespace SimpleMikrotikMonitor.Engines
{
    /// <param name="Escalation">Second look at High/Critical findings with the stronger model.</param>
    /// <param name="DeepReview">One-off config review: use the engine's review model, more effort and a longer timeout.</param>
    public sealed record EngineRequest(string SystemPrompt, string UserPrompt, string JsonSchema, bool Escalation = false, bool DeepReview = false);

    public sealed record EngineResponse(
        string Text,
        string ModelUsed,
        long InputTokens = 0,
        long OutputTokens = 0,
        long CacheReadTokens = 0,
        double? CostUsd = null);

    /// <summary>An AI backend that turns a prompt into a JSON answer. No tools, no side effects.</summary>
    public interface IAnalysisEngine
    {
        string DisplayName { get; }

        /// <summary>True when prompts leave this PC (used to decide whether to mask IPs/MACs/usernames).</summary>
        bool SendsDataOffPc { get; }

        /// <summary>True when a stronger model is configured for a second look at High/Critical findings.</summary>
        bool CanEscalate { get; }

        /// <summary>Model used for config reviews (DeepReview requests).</summary>
        string ReviewModelName { get; }

        /// <summary>Roughly how many input tokens one request can carry before content is cut off.</summary>
        int MaxInputTokens { get; }

        Task<EngineResponse> CompleteAsync(EngineRequest request, CancellationToken ct = default);

        /// <summary>Checks the engine is reachable and configured. Returns a short success message or throws <see cref="EngineException"/>.</summary>
        Task<string> TestAsync(CancellationToken ct = default);
    }

    /// <summary>An engine failure with a message fit to show the user.</summary>
    public sealed class EngineException : Exception
    {
        public EngineException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public static class EngineFactory
    {
        public static IAnalysisEngine Create(AppSettings s) => s.LLMSettings.Provider switch
        {
            LLMProvider.ClaudeCode => new ClaudeCodeEngine(s.ClaudeCodeSettings),
            LLMProvider.Anthropic => new ClaudeApiEngine(s.AnthropicSettings),
            LLMProvider.Ollama => new OllamaEngine(s.OllamaSettings),
            LLMProvider.OpenAI => new OpenAICompatibleEngine("OpenAI", "https://api.openai.com/v1", s.OpenAISettings.ApiKey, s.OpenAISettings.Model),
            LLMProvider.Google => new OpenAICompatibleEngine("Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", s.GoogleSettings.ApiKey, s.GoogleSettings.Model),
            LLMProvider.Custom => new OpenAICompatibleEngine("Custom (OpenAI-compatible)", s.LLMSettings.EndpointUrl, s.LLMSettings.ApiKey, s.LLMSettings.Model),
            _ => throw new EngineException($"Unknown AI engine: {s.LLMSettings.Provider}")
        };

        public static string DisplayName(LLMProvider p) => p switch
        {
            LLMProvider.ClaudeCode => "Claude Code on this PC (subscription, no GPU)",
            LLMProvider.Anthropic => "Claude API - cloud (pay per use, no GPU)",
            LLMProvider.Ollama => "Ollama - local model (uses this PC's GPU)",
            LLMProvider.OpenAI => "OpenAI API",
            LLMProvider.Google => "Google Gemini API",
            LLMProvider.Custom => "Custom OpenAI-compatible endpoint",
            _ => p.ToString()
        };
    }
}
