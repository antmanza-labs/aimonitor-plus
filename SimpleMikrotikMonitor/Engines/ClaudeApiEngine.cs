using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace SimpleMikrotikMonitor.Engines
{
    /// <summary>
    /// Claude via the Anthropic API (official SDK). Cheap model sorts every batch; an optional
    /// stronger model takes a second look at High/Critical findings. The fixed system prompt is
    /// cached, so repeated checks pay a fraction of its cost.
    /// </summary>
    public sealed class ClaudeApiEngine : IAnalysisEngine
    {
        private readonly AnthropicSettings _s;

        public ClaudeApiEngine(AnthropicSettings settings) => _s = settings;

        public string DisplayName => $"Claude API ({_s.Model})";
        public bool SendsDataOffPc => true;
        public bool CanEscalate => _s.EscalateHighSeverity && !string.IsNullOrWhiteSpace(_s.EscalationModel);
        public string ReviewModelName => string.IsNullOrWhiteSpace(_s.ReviewModel) ? _s.Model : _s.ReviewModel;
        public int MaxInputTokens => 400_000;   // 1M context; capped to keep a single review's cost sensible

        private AnthropicClient Client()
        {
            if (string.IsNullOrWhiteSpace(_s.ApiKey))
                throw new EngineException("No Claude API key set. Create one at https://platform.claude.com (API keys) and paste it in Settings > AI Engine.");
            return new AnthropicClient { ApiKey = _s.ApiKey.Trim() };
        }

        public async Task<EngineResponse> CompleteAsync(EngineRequest request, CancellationToken ct = default)
        {
            var model = request.DeepReview ? ReviewModelName : request.Escalation && CanEscalate ? _s.EscalationModel : _s.Model;
            var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.JsonSchema)!;

            try
            {
                var response = await Client().Messages.Create(new MessageCreateParams
                {
                    Model = model,
                    MaxTokens = 16000,
                    System = new List<TextBlockParam>
                    {
                        new() { Text = request.SystemPrompt, CacheControl = new CacheControlEphemeral() }
                    },
                    Messages = [new UserMessageParam { Content = request.UserPrompt }],
                    OutputConfig = new OutputConfig
                    {
                        // Triage is a classification job: low effort keeps it fast and cheap.
                        Effort = request.DeepReview || request.Escalation ? Effort.Medium : Effort.Low,
                        Format = new JsonOutputFormat { Schema = schema }
                    }
                }, ct);

                if (response.StopReason == "refusal")
                    throw new EngineException("Claude declined to analyse this batch (safety refusal).");
                if (response.StopReason == "max_tokens")
                    throw new EngineException("Claude's answer was cut off (too many events in one batch). Lower 'Max events per AI call'.");

                var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
                return new EngineResponse(text, model,
                    response.Usage.InputTokens + (response.Usage.CacheReadInputTokens ?? 0) + (response.Usage.CacheCreationInputTokens ?? 0),
                    response.Usage.OutputTokens,
                    response.Usage.CacheReadInputTokens ?? 0);
            }
            catch (EngineException) { throw; }
            catch (Exception ex) { throw Translate(ex, model); }
        }

        public async Task<string> TestAsync(CancellationToken ct = default)
        {
            try
            {
                // Looking up the models checks the key and model names without spending tokens.
                var client = Client();
                var triage = await client.Models.Retrieve(_s.Model, cancellationToken: ct);
                var msg = $"Connected. Triage model: {triage.DisplayName}.";
                if (CanEscalate)
                {
                    var esc = await client.Models.Retrieve(_s.EscalationModel, cancellationToken: ct);
                    msg += $" Second-look model: {esc.DisplayName}.";
                }
                return msg;
            }
            catch (EngineException) { throw; }
            catch (Exception ex) { throw Translate(ex, _s.Model); }
        }

        private static EngineException Translate(Exception ex, string model) => ex switch
        {
            AnthropicUnauthorizedException => new EngineException("Claude API key was rejected. Check the key in Settings > AI Engine.", ex),
            AnthropicNotFoundException => new EngineException($"Model '{model}' was not found. Check the model name in Settings > AI Engine.", ex),
            AnthropicRateLimitException => new EngineException("Claude API rate limit or spend limit reached. It will retry on the next check.", ex),
            Anthropic5xxException => new EngineException("Claude API is having problems right now (server error). It will retry on the next check.", ex),
            AnthropicApiException api => new EngineException($"Claude API error: {api.Message}", ex),
            HttpRequestException => new EngineException("Can't reach api.anthropic.com. Check this PC's internet connection or proxy.", ex),
            TaskCanceledException => new EngineException("Claude API call timed out.", ex),
            _ => new EngineException($"Claude API failed: {ex.Message}", ex)
        };
    }
}
