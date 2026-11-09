using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleMikrotikMonitor.Engines
{
    /// <summary>
    /// Local model through Ollama. Nothing leaves the PC, but it uses this PC's GPU/CPU.
    /// By default the model is unloaded right after each analysis, so the GPU is free between checks.
    /// </summary>
    public sealed class OllamaEngine : IAnalysisEngine
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
        private readonly OllamaSettings _s;

        public OllamaEngine(OllamaSettings settings) => _s = settings;

        public string DisplayName => $"Ollama ({_s.Model})";
        public bool SendsDataOffPc => !IsLocal(_s.Url);
        public bool CanEscalate => false;
        public string ReviewModelName => _s.Model;
        /// <summary>Ollama silently drops input beyond num_ctx; keep room for instructions and the answer.</summary>
        public int MaxInputTokens => Math.Max(2048, Math.Max(4096, _s.ContextSize) - 6000);
        public int ContextSize => Math.Max(4096, _s.ContextSize);

        private string BaseUrl => (_s.Url ?? "http://localhost:11434").TrimEnd('/');

        public async Task<EngineResponse> CompleteAsync(EngineRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_s.Model)) throw new EngineException("No Ollama model set.");

            var body = new JsonObject
            {
                ["model"] = _s.Model,
                ["stream"] = false,
                ["format"] = JsonNode.Parse(request.JsonSchema),    // structured output (Ollama 0.5+)
                ["keep_alive"] = _s.UnloadAfterRun ? 0 : "5m",
                ["options"] = new JsonObject { ["num_ctx"] = Math.Max(4096, _s.ContextSize), ["temperature"] = 0 },
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                    new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt }
                }
            };

            HttpResponseMessage resp;
            try
            {
                resp = await Http.PostAsJsonAsync($"{BaseUrl}/api/chat", body, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new EngineException($"Can't reach Ollama at {BaseUrl}. Is Ollama running?", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new EngineException("Ollama took longer than 10 minutes. Try a smaller model or fewer events per call.", ex);
            }

            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new EngineException($"Ollama error {(int)resp.StatusCode}: {Truncate(raw, 300)}");

            var root = JsonDocument.Parse(raw).RootElement;
            var text = root.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            long input = root.TryGetProperty("prompt_eval_count", out var pi) ? pi.GetInt64() : 0;
            long output = root.TryGetProperty("eval_count", out var po) ? po.GetInt64() : 0;
            return new EngineResponse(text, _s.Model, input, output);
        }

        public async Task<string> TestAsync(CancellationToken ct = default)
        {
            try
            {
                var tags = await Http.GetFromJsonAsync<JsonElement>($"{BaseUrl}/api/tags", ct);
                var models = tags.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "").ToList();
                if (!models.Any(n => n.Equals(_s.Model, StringComparison.OrdinalIgnoreCase) || n.Equals(_s.Model + ":latest", StringComparison.OrdinalIgnoreCase)))
                    throw new EngineException($"Ollama is running, but model '{_s.Model}' isn't installed. Installed: {string.Join(", ", models.Take(10))}. Run: ollama pull {_s.Model}");
                return $"Connected to Ollama at {BaseUrl}. Model '{_s.Model}' is installed.";
            }
            catch (HttpRequestException ex)
            {
                throw new EngineException($"Can't reach Ollama at {BaseUrl}. Is Ollama running?", ex);
            }
        }

        private static bool IsLocal(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
    }
}
