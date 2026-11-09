using SimpleMikrotikMonitor;
using SimpleMikrotikMonitor.Analysis;
using SimpleMikrotikMonitor.Engines;
using Xunit;
using Xunit.Abstractions;

namespace AIMonitor.Tests
{
    /// <summary>
    /// Calls real engines. Skipped unless AIMONITOR_LIVE is set, e.g.
    ///   AIMONITOR_LIVE=claudecode  [AIMONITOR_CLAUDE_EXE=path\to\claude.exe]
    ///   AIMONITOR_LIVE=claudeapi   ANTHROPIC_API_KEY=sk-ant-...
    ///   AIMONITOR_LIVE=ollama      [AIMONITOR_OLLAMA_MODEL=qwen3:8b]
    /// </summary>
    public class LiveEngineTests
    {
        private readonly ITestOutputHelper _out;
        public LiveEngineTests(ITestOutputHelper output) => _out = output;

        private static string? Live => Environment.GetEnvironmentVariable("AIMONITOR_LIVE");

        [SkippableFact]
        public async Task Real_engine_triages_a_realistic_batch()
        {
            Skip.If(string.IsNullOrEmpty(Live), "Set AIMONITOR_LIVE to run against a real engine.");

            var settings = new AppSettings();
            switch (Live!.ToLowerInvariant())
            {
                case "claudecode":
                    settings.LLMSettings.Provider = LLMProvider.ClaudeCode;
                    settings.ClaudeCodeSettings.ExecutablePath = Environment.GetEnvironmentVariable("AIMONITOR_CLAUDE_EXE") ?? "";
                    break;
                case "claudeapi":
                    settings.LLMSettings.Provider = LLMProvider.Anthropic;
                    settings.AnthropicSettings.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
                    break;
                case "ollama":
                    settings.LLMSettings.Provider = LLMProvider.Ollama;
                    settings.OllamaSettings.Model = Environment.GetEnvironmentVariable("AIMONITOR_OLLAMA_MODEL") ?? "qwen3:8b";
                    break;
            }

            var engine = EngineFactory.Create(settings);
            _out.WriteLine("Test: " + await engine.TestAsync());

            var router = new RouterConfig { Name = "core-ccr1", RouterIP = "10.0.0.1", Username = "aimonitor" };
            router.Schedule.Priority = RouterPriority.Critical;
            var memory = new AlertMemory(Path.Combine(Path.GetTempPath(), $"live-{Guid.NewGuid():N}.json"));
            var analyzer = new LogAnalyzer(() => settings, memory, m => _out.WriteLine(m));

            string T(int m) => DateTime.Now.AddMinutes(-m).ToString("HH:mm:ss");
            var lines = new List<LogEvent>();
            for (int i = 0; i < 20; i++)
            {
                lines.Add(LogEvent.Create(T(40 - i), "system,info,account", "user aimonitor logged in from 10.0.0.5 via api"));
                lines.Add(LogEvent.Create(T(40 - i), "dhcp,info", $"dhcp1 assigned 192.168.88.{i + 20} for AA:BB:CC:00:00:{i + 10:00}"));
            }
            lines.Add(LogEvent.Create(T(12), "system,info", "address added by admin"));
            lines.Add(LogEvent.Create(T(6), "interface,info", "sfp-sfpplus1 link down"));
            lines.Add(LogEvent.Create(T(6), "route,bgp,error", "BGP session transit-1 (196.10.20.1) closed, hold timer expired"));
            lines.Add(LogEvent.Create(T(5), "interface,info", "ether7 link down"));
            lines.Add(LogEvent.Create(T(5), "interface,info", "ether7 link up (speed 1G, full duplex)"));
            lines.Add(LogEvent.Create(T(4), "system,info,account", "user admin logged in from 41.13.22.9 via winbox"));
            for (int i = 0; i < 30; i++)
                lines.Add(LogEvent.Create(T(5), "pppoe,ppp,info", $"<pppoe-cust{i}>: terminating... - peer is not responding"));

            var o = await analyzer.AnalyzeAsync(router, lines);

            _out.WriteLine($"lines {o.NewLines}, dropped {o.DroppedAsNoise}, sent {o.SentToAi}, usage: {o.UsageNote}");
            _out.WriteLine("summary: " + o.EngineSummary);
            foreach (var f in o.Findings)
                _out.WriteLine($"[{f.Severity}] {f.Title} | impact: {f.Impact} | cause: {f.LikelyCause} | check: {f.SuggestedCheck}");

            Assert.Null(o.EngineError);
            Assert.Contains(o.Findings, f => f.Severity >= Severity.High && f.Title.Contains("sfp", StringComparison.OrdinalIgnoreCase));
        }
    }
}
