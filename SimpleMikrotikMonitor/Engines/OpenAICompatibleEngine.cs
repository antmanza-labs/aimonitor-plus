using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleMikrotikMonitor.Engines
{
    /// <summary>
    /// Any endpoint that speaks the OpenAI chat-completions format: OpenAI, Google Gemini's
    /// OpenAI-compatible endpoint, LM Studio, OpenRouter, vLLM and similar.
    /// </summary>
    public sealed class OpenAICompatibleEngine : IAnalysisEngine
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
        private readonly string _name, _baseUrl, _apiKey, _model;

        public OpenAICompatibleEngine(string name, string baseUrl, string apiKey, string model)
        {
            _name = name;
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _apiKey = apiKey ?? string.Empty;
            _model = model ?? string.Empty;
        }

        public string DisplayName => $"{_name} ({_model})";
        public bool SendsDataOffPc => !(Uri.TryCreate(_baseUrl, UriKind.Absolute, out var u) && u.IsLoopback);
        public bool CanEscalate => false;
        public string ReviewModelName => _model;
        public int MaxInputTokens => 100_000;

        public async Task<EngineResponse> CompleteAsync(EngineRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl)) throw new EngineException($"{_name}: no endpoint URL set.");
            if (string.IsNullOrWhiteSpace(_model)) throw new EngineException($"{_name}: no model set.");

            var body = new JsonObject
            {
                ["model"] = _model,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt + "\n\nJSON schema to follow:\n" + request.JsonSchema },
                    new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt }
                },
                ["response_format"] = new JsonObject { ["type"] = "json_object" }
            };

            var resp = await Send(body, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // Some servers don't support response_format; retry without it.
                body.Remove("response_format");
                resp = await Send(body, ct);
            }

            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new EngineException($"{_name} error {(int)resp.StatusCode}: {Truncate(raw, 300)}");

            var root = JsonDocument.Parse(raw).RootElement;
            var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            long input = 0, output = 0;
            if (root.TryGetProperty("usage", out var u))
            {
                input = u.TryGetProperty("prompt_tokens", out var p) ? p.GetInt64() : 0;
                output = u.TryGetProperty("completion_tokens", out var c) ? c.GetInt64() : 0;
            }
            return new EngineResponse(text, _model, input, output);
        }

        public async Task<string> TestAsync(CancellationToken ct = default)
        {
            var schema = "{\"type\":\"object\",\"properties\":{\"ok\":{\"type\":\"boolean\"}},\"required\":[\"ok\"]}";
            var r = await CompleteAsync(new EngineRequest("Reply with the JSON object {\"ok\": true}.", "Connection test.", schema), ct);
            return $"Connected to {_name}, model {_model}. Reply: {Truncate(r.Text, 80)}";
        }

        private async Task<HttpResponseMessage> Send(JsonObject body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions") { Content = JsonContent.Create(body) };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
            try
            {
                return await Http.SendAsync(req, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new EngineException($"Can't reach {_name} at {_baseUrl}.", ex);
            }
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
    }
}
