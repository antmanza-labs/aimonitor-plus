using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SimpleMikrotikMonitor.Services;

namespace SimpleMikrotikMonitor.Engines
{
    /// <summary>
    /// Runs the Claude Code CLI on this PC in print mode ("claude -p"). Uses the Claude
    /// subscription the CLI is signed in with, so there is no API key and no GPU load.
    /// All tools, MCP servers, skills and project settings are switched off: the model only
    /// reads the prompt and returns JSON.
    /// </summary>
    public sealed class ClaudeCodeEngine : IAnalysisEngine
    {
        private readonly ClaudeCodeSettings _s;

        public ClaudeCodeEngine(ClaudeCodeSettings settings) => _s = settings;

        public string DisplayName => $"Claude Code on this PC ({_s.Model})";
        public bool SendsDataOffPc => true;
        public bool CanEscalate => _s.EscalateHighSeverity && !string.IsNullOrWhiteSpace(_s.EscalationModel);
        public string ReviewModelName => string.IsNullOrWhiteSpace(_s.ReviewModel) ? _s.Model : _s.ReviewModel;
        public int MaxInputTokens => 150_000;   // stays inside the standard Claude Code context window

        public async Task<EngineResponse> CompleteAsync(EngineRequest request, CancellationToken ct = default)
        {
            var model = request.DeepReview ? ReviewModelName : request.Escalation && CanEscalate ? _s.EscalationModel : _s.Model;
            var promptFile = Path.Combine(AppPaths.ClaudeCodeWorkDir, "system-prompt.md");
            await File.WriteAllTextAsync(promptFile, request.SystemPrompt, ct);

            var args = new List<string>
            {
                "-p",
                "--output-format", "json",
                "--model", model,
                "--system-prompt-file", promptFile,
                "--json-schema", CompactJson(request.JsonSchema),
                "--tools", "",
                "--strict-mcp-config",
                "--disable-slash-commands",
                "--no-session-persistence",
                "--max-turns", "3"
            };

            // Config reviews read a whole export and write a long report: allow at least 15 minutes.
            var seconds = Math.Max(30, _s.TimeoutSeconds);
            if (request.DeepReview) seconds = Math.Max(seconds, 900);
            var (exit, stdout, stderr) = await RunAsync(args, request.UserPrompt, TimeSpan.FromSeconds(seconds), ct);
            return ParseResult(exit, stdout, stderr, model);
        }

        public async Task<string> TestAsync(CancellationToken ct = default)
        {
            var exe = ResolveExecutable();
            var (exit, stdout, stderr) = await RunAsync(new List<string> { "--version" }, null, TimeSpan.FromSeconds(30), ct);
            if (exit != 0) throw new EngineException($"Claude Code did not start: {FirstLine(stderr)}");
            var version = stdout.Trim();

            // A tiny real request proves the CLI is signed in.
            var schema = "{\"type\":\"object\",\"properties\":{\"ok\":{\"type\":\"boolean\"}},\"required\":[\"ok\"],\"additionalProperties\":false}";
            var resp = await CompleteAsync(new EngineRequest("Reply with the JSON object {\"ok\": true}.", "Connection test.", schema), ct);
            if (!resp.Text.Contains("true", StringComparison.OrdinalIgnoreCase))
                throw new EngineException($"Claude Code answered unexpectedly: {Truncate(resp.Text, 200)}");
            return $"Connected. Claude Code {version} at {exe}, model {_s.Model}.";
        }

        private EngineResponse ParseResult(int exit, string stdout, string stderr, string model)
        {
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(stdout).RootElement;
            }
            catch (JsonException)
            {
                var detail = FirstLine(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
                if (detail.Contains("login", StringComparison.OrdinalIgnoreCase) || detail.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                    detail.Contains("sign in", StringComparison.OrdinalIgnoreCase) || detail.Contains("expired", StringComparison.OrdinalIgnoreCase))
                    throw new EngineException($"Claude Code is not signed in ({detail}). Open a terminal and run: claude auth login");
                throw new EngineException($"Claude Code failed (exit {exit}): {detail}");
            }

            if (root.TryGetProperty("is_error", out var isErr) && isErr.ValueKind == JsonValueKind.True)
            {
                var msg = root.TryGetProperty("result", out var r) ? r.ToString() : stdout;
                if (msg.Contains("limit", StringComparison.OrdinalIgnoreCase))
                    throw new EngineException($"Claude subscription usage limit reached: {Truncate(msg, 200)}");
                throw new EngineException($"Claude Code error: {Truncate(msg, 300)}");
            }

            // With --json-schema the validated object is returned separately from the text result.
            string text = root.TryGetProperty("structured_output", out var so) && so.ValueKind == JsonValueKind.Object
                ? so.GetRawText()
                : root.TryGetProperty("result", out var res) ? res.GetString() ?? string.Empty : string.Empty;

            long input = 0, output = 0, cacheRead = 0;
            if (root.TryGetProperty("usage", out var u))
            {
                input = Long(u, "input_tokens") + Long(u, "cache_read_input_tokens") + Long(u, "cache_creation_input_tokens");
                output = Long(u, "output_tokens");
                cacheRead = Long(u, "cache_read_input_tokens");
            }
            double? cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;

            return new EngineResponse(text, model, input, output, cacheRead, cost);
        }

        private async Task<(int Exit, string StdOut, string StdErr)> RunAsync(List<string> args, string? stdin, TimeSpan timeout, CancellationToken ct)
        {
            var exe = ResolveExecutable();
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = AppPaths.ClaudeCodeWorkDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false)
            };

            // npm installs a .cmd shim; run its JavaScript entry point with node instead,
            // so arguments containing JSON are passed through without cmd.exe quoting problems.
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                var cli = Path.Combine(Path.GetDirectoryName(exe)!, "node_modules", "@anthropic-ai", "claude-code", "cli.js");
                if (!File.Exists(cli)) throw new EngineException($"Found {exe} but not its cli.js. Install the native Claude Code build or set the path to claude.exe.");
                psi.FileName = "node";
                psi.ArgumentList.Add(cli);
            }
            else
            {
                psi.FileName = exe;
            }
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                throw new EngineException($"Could not start Claude Code ({psi.FileName}): {ex.Message}", ex);
            }

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            if (stdin != null) await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                throw new EngineException(ct.IsCancellationRequested
                    ? "Claude Code call was cancelled."
                    : $"Claude Code did not answer within {timeout.TotalSeconds:0} seconds.");
            }

            return (proc.ExitCode, await outTask, await errTask);
        }

        /// <summary>Configured path, else PATH, else the usual install locations.</summary>
        public string ResolveExecutable()
        {
            if (!string.IsNullOrWhiteSpace(_s.ExecutablePath))
            {
                if (File.Exists(_s.ExecutablePath)) return _s.ExecutablePath;
                throw new EngineException($"Claude Code not found at '{_s.ExecutablePath}'. Fix the path or clear it to auto-detect.");
            }

            var found = FindExecutable();
            return found ?? throw new EngineException(
                "Claude Code CLI not found. Install it (https://claude.com/claude-code), run 'claude' once to sign in, " +
                "or set the path to claude.exe in Settings > AI Engine.");
        }

        public static string? FindExecutable()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new List<string>();

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(Path.Combine(dir.Trim(), "claude.exe"));
                candidates.Add(Path.Combine(dir.Trim(), "claude.cmd"));
            }
            candidates.Add(Path.Combine(home, ".local", "bin", "claude.exe"));
            candidates.Add(Path.Combine(appData, "npm", "claude.cmd"));

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string CompactJson(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
        private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        private static string FirstLine(string s) => Truncate((s ?? string.Empty).Trim().Split('\n').FirstOrDefault() ?? string.Empty, 300);
        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
    }
}
