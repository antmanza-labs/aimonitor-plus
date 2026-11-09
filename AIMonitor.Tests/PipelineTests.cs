using System.Runtime.CompilerServices;
using SimpleMikrotikMonitor;
using SimpleMikrotikMonitor.Analysis;
using SimpleMikrotikMonitor.Engines;
using SimpleMikrotikMonitor.Services;
using Xunit;

namespace AIMonitor.Tests
{
    internal static class TestInit
    {
        // Keep tests away from the real %APPDATA%\AIMonitorPlus.
        [ModuleInitializer]
        internal static void Init() =>
            Environment.SetEnvironmentVariable("AIMONITOR_DATA_DIR", Path.Combine(Path.GetTempPath(), "aimonitor-tests-" + Guid.NewGuid().ToString("N")));
    }

    /// <summary>Records prompts and returns a canned JSON answer.</summary>
    internal sealed class FakeEngine : IAnalysisEngine
    {
        public List<EngineRequest> Requests { get; } = new();
        public Func<EngineRequest, string> Answer { get; set; } = _ => "{\"summary\":\"ok\",\"findings\":[]}";
        public bool OffPc { get; set; }
        public Exception? Throw { get; set; }

        public string DisplayName => "fake";
        public bool SendsDataOffPc => OffPc;
        public bool CanEscalate => false;
        public string ReviewModelName => "fake-review";
        public int MaxInputTokens => 100_000;

        public Task<EngineResponse> CompleteAsync(EngineRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Throw != null) throw Throw;
            return Task.FromResult(new EngineResponse(Answer(request), "fake-model", 100, 20));
        }

        public Task<string> TestAsync(CancellationToken ct = default) => Task.FromResult("ok");
    }

    public class NoiseFilterTests
    {
        private static NoiseVerdict V(string topics, string message, string monitorUser = "aimonitor") =>
            MikrotikNoiseFilter.Classify(LogEvent.Create("08:10:22", topics, message), monitorUser);

        [Fact] public void Own_api_login_is_dropped() =>
            Assert.Equal(NoiseAction.Drop, V("system,info,account", "user aimonitor logged in from 10.0.0.5 via api").Action);

        [Fact] public void Other_tool_api_logins_are_aggregated() =>
            Assert.Equal(NoiseAction.Aggregate, V("system,info,account", "user splynx logged in from 10.0.0.9 via api").Action);

        [Fact] public void Winbox_login_is_kept_as_low()
        {
            var v = V("system,info,account", "user admin logged in from 41.1.2.3 via winbox");
            Assert.Equal(NoiseAction.Keep, v.Action);
            Assert.Equal(Severity.Low, v.Hint);
        }

        [Theory]
        [InlineData("dhcp,info", "dhcp1 assigned 192.168.88.254 for AA:BB:CC:DD:EE:FF ")]
        [InlineData("dhcp,info", "dhcp1 deassigned 192.168.88.254 to AA:BB:CC:DD:EE:FF")]
        [InlineData("ipsec,info", "acquired 10.1.1.1 address for 10.2.2.2")]
        [InlineData("system,debug", "anything")]
        public void Routine_lines_are_dropped(string topics, string message) =>
            Assert.Equal(NoiseAction.Drop, V(topics, message).Action);

        [Theory]
        [InlineData("pppoe,ppp,info", "<pppoe-john>: disconnected")]
        [InlineData("pppoe,ppp,info", "<pppoe-mary>: authenticated")]
        [InlineData("wireless,info", "AA:BB:CC:DD:EE:FF@wlan1: disconnected, extensive data loss")]
        [InlineData("firewall,info", "input: in:ether1 out:(unknown 0), proto TCP (SYN), 1.2.3.4:5555->5.6.7.8:22, len 60")]
        [InlineData("system,error,critical", "login failure for user admin from 1.2.3.4 via ssh")]
        public void Subscriber_churn_is_aggregated(string topics, string message) =>
            Assert.Equal(NoiseAction.Aggregate, V(topics, message).Action);

        [Fact] public void PPPoE_client_uplink_disconnect_is_kept() =>
            Assert.Equal(NoiseAction.Keep, V("pppoe,ppp,info", "pppoe-out1: disconnected").Action);

        [Theory]
        [InlineData("interface,info", "sfp-sfpplus1 link down", Severity.Medium)]
        [InlineData("system,error,critical", "router was rebooted without proper shutdown", Severity.High)]
        [InlineData("system,error,critical", "kernel failure in previous boot", Severity.Critical)]
        [InlineData("interface,warning", "ether5 detected loop from 11:22:33:44:55:66", Severity.High)]
        public void Serious_lines_get_hints(string topics, string message, Severity expected)
        {
            var v = V(topics, message);
            Assert.Equal(NoiseAction.Keep, v.Action);
            Assert.Equal(expected, v.Hint);
        }
    }

    public class FingerprintAndMaskingTests
    {
        [Fact] public void Different_interfaces_are_different_issues() =>
            Assert.NotEqual(Fingerprint.Of("r1", "interface,info", "ether1 link down"), Fingerprint.Of("r1", "interface,info", "ether2 link down"));

        [Fact] public void Changing_ips_and_numbers_are_the_same_issue() =>
            Assert.Equal(
                Fingerprint.Of("r1", "system,error", "login failure for user admin from 1.2.3.4 via ssh"),
                Fingerprint.Of("r1", "system,error", "login failure for user admin from 9.8.7.6 via ssh"));

        [Fact]
        public void Masking_round_trips_and_leaves_clock_times_alone()
        {
            var m = new SensitiveDataMasker();
            var masked = m.Mask("08:10:22 user admin logged in from 41.1.2.3 via winbox, <pppoe-john> MAC AA:BB:CC:DD:EE:FF");
            Assert.DoesNotContain("41.1.2.3", masked);
            Assert.DoesNotContain("pppoe-john", masked);
            Assert.DoesNotContain("AA:BB:CC:DD:EE:FF", masked);
            Assert.DoesNotContain("admin", masked);
            Assert.Contains("08:10:22", masked);
            Assert.Equal("Block 41.1.2.3", m.Unmask("Block IP-1"));
        }

        [Fact]
        public void Router_time_formats_parse()
        {
            Assert.Equal(new DateTime(2026, 11, 8, 23, 59, 1), LogEvent.ParseRouterTime("2026-11-08 23:59:01"));
            Assert.Equal(DateTime.Today.AddHours(0).Date, LogEvent.ParseRouterTime("00:00:01").Date);
            var v6 = LogEvent.ParseRouterTime("jan/02 03:04:05");
            Assert.Equal((1, 2, 3, 4, 5), (v6.Month, v6.Day, v6.Hour, v6.Minute, v6.Second));
        }
    }

    public class LogAnalyzerTests
    {
        private readonly AppSettings _settings = new();
        private readonly RouterConfig _router = new() { Name = "core1", RouterIP = "10.0.0.1", Username = "aimonitor" };
        private readonly AlertMemory _memory = new(Path.Combine(Path.GetTempPath(), $"mem-{Guid.NewGuid():N}.json"));
        private readonly FakeEngine _engine = new();
        private readonly LogAnalyzer _analyzer;

        public LogAnalyzerTests()
        {
            _router.Schedule.Priority = RouterPriority.Critical;
            _analyzer = new LogAnalyzer(() => _settings, _memory, _ => { }, _ => _engine);
        }

        private static string T(int minutesAgo) => DateTime.Now.AddMinutes(-minutesAgo).ToString("HH:mm:ss");

        private static List<LogEvent> NoisyBatch()
        {
            var lines = new List<LogEvent>();
            for (int i = 0; i < 25; i++)
            {
                lines.Add(LogEvent.Create(T(30 - i), "system,info,account", "user aimonitor logged in from 10.0.0.5 via api"));
                lines.Add(LogEvent.Create(T(30 - i), "system,info,account", "user aimonitor logged out from 10.0.0.5 via api"));
                lines.Add(LogEvent.Create(T(30 - i), "dhcp,info", $"dhcp1 assigned 192.168.88.{i + 10} for AA:BB:CC:DD:EE:{i + 10:00}"));
            }
            lines.Add(LogEvent.Create(T(3), "interface,info", "sfp-sfpplus1 link down"));
            lines.Add(LogEvent.Create(T(3), "route,bgp,info", "BGP session upstream1 closed, hold timer expired"));
            return lines;
        }

        private static string CriticalAnswer(EngineRequest _) =>
            "{\"summary\":\"Uplink lost\",\"findings\":[{\"event_ids\":[\"E1\",\"E2\"],\"severity\":\"critical\",\"title\":\"sfp-sfpplus1 uplink down, BGP lost\"," +
            "\"impact\":\"Upstream transit lost\",\"likely_cause\":\"Fibre or optic fault\",\"suggested_check\":\"/interface ethernet monitor sfp-sfpplus1 once\"}]}";

        [Fact]
        public async Task Noise_is_filtered_and_one_finding_comes_back()
        {
            _engine.Answer = CriticalAnswer;
            var o = await _analyzer.AnalyzeAsync(_router, NoisyBatch());

            Assert.Single(_engine.Requests);
            var prompt = _engine.Requests[0].UserPrompt;
            Assert.DoesNotContain("aimonitor logged", prompt);
            Assert.DoesNotContain("dhcp1 assigned", prompt);
            Assert.Contains("sfp-sfpplus1 link down", prompt);
            Assert.Equal(75, o.DroppedAsNoise);
            var f = Assert.Single(o.Findings);
            Assert.Equal(Severity.Critical, f.Severity);
            Assert.Equal(2, f.Fingerprints.Count);
        }

        [Fact]
        public async Task Same_lines_again_cost_nothing()
        {
            _engine.Answer = CriticalAnswer;
            var batch = NoisyBatch();
            await _analyzer.AnalyzeAsync(_router, batch);
            var again = await _analyzer.AnalyzeAsync(_router, batch);

            Assert.Single(_engine.Requests);
            Assert.Equal(0, again.NewLines);
        }

        [Fact]
        public async Task Repeat_of_a_known_issue_is_not_resent_or_realerted()
        {
            _engine.Answer = CriticalAnswer;
            var first = await _analyzer.AnalyzeAsync(_router, NoisyBatch());
            _memory.MarkNotified(first.Findings[0]);

            var repeat = await _analyzer.AnalyzeAsync(_router, new List<LogEvent> { LogEvent.Create(T(1), "interface,info", "sfp-sfpplus1 link down") });

            Assert.Single(_engine.Requests);
            Assert.Equal(1, repeat.SkippedAsKnown);
            Assert.False(_memory.ShouldNotify(first.Findings[0], cooldownHours: 6));
        }

        [Fact]
        public async Task Worse_severity_re_alerts_inside_the_cooldown()
        {
            _engine.Answer = CriticalAnswer;
            var first = await _analyzer.AnalyzeAsync(_router, NoisyBatch());
            var f = first.Findings[0];
            f.Severity = Severity.High;
            _memory.MarkNotified(f);

            f.Severity = Severity.Critical;
            Assert.True(_memory.ShouldNotify(f, cooldownHours: 6));
        }

        [Fact]
        public async Task Mass_pppoe_drop_becomes_one_summary_but_a_few_do_not()
        {
            var few = Enumerable.Range(1, 5).Select(i => LogEvent.Create(T(2), "pppoe,ppp,info", $"<pppoe-user{i}>: disconnected")).ToList();
            var o1 = await _analyzer.AnalyzeAsync(_router, few);
            Assert.Empty(_engine.Requests);
            Assert.Equal(5, o1.DroppedAsNoise);

            var many = Enumerable.Range(1, 40).Select(i => LogEvent.Create(T(1), "pppoe,ppp,info", $"<pppoe-cust{i}>: terminating... - peer is not responding")).ToList();
            await _analyzer.AnalyzeAsync(_router, many);
            var prompt = Assert.Single(_engine.Requests).UserPrompt;
            Assert.Contains("[summary] PPP subscriber sessions: 40 lines", prompt);
        }

        [Fact]
        public async Task Engine_failure_falls_back_to_rules_and_retries_lines()
        {
            _engine.Throw = new EngineException("down for test");
            var lines = new List<LogEvent> { LogEvent.Create(T(2), "system,error,critical", "router was rebooted without proper shutdown") };

            var o = await _analyzer.AnalyzeAsync(_router, lines);
            Assert.Equal("down for test", o.EngineError);
            var f = Assert.Single(o.Findings);
            Assert.True(f.FromRules);
            Assert.Equal(Severity.High, f.Severity);

            _engine.Throw = null;
            var retry = await _analyzer.AnalyzeAsync(_router, lines);
            Assert.Equal(1, retry.NewLines);
        }

        [Fact]
        public async Task Cloud_engine_gets_masked_data_and_answer_is_unmasked()
        {
            _engine.OffPc = true;
            _engine.Answer = r => "{\"summary\":\"s\",\"findings\":[{\"event_ids\":[\"E1\"],\"severity\":\"high\",\"title\":\"Winbox login from IP-1\",\"impact\":\"\",\"likely_cause\":\"\",\"suggested_check\":\"\"}]}";
            var o = await _analyzer.AnalyzeAsync(_router, new List<LogEvent> { LogEvent.Create(T(1), "system,info,account", "user admin logged in from 41.1.2.3 via winbox") });

            Assert.DoesNotContain("41.1.2.3", _engine.Requests[0].UserPrompt);
            Assert.Equal("Winbox login from 41.1.2.3", Assert.Single(o.Findings).Title);
        }

        [Fact]
        public void Unreachable_alerts_once_then_recovers()
        {
            _settings.AnalysisSettings.UnreachableAfterFailures = 2;
            Assert.Null(_analyzer.OnConnectFailed(_router, "timeout"));
            var down = _analyzer.OnConnectFailed(_router, "timeout");
            Assert.NotNull(down);
            Assert.Equal(Severity.Critical, down!.Severity);           // Critical-priority router
            Assert.Null(_analyzer.OnConnectFailed(_router, "timeout")); // no repeat
            Assert.True(_analyzer.OnConnected(_router)!.IsRecovery);
        }
    }

    public class SettingsAndTelegramTests
    {
        [Fact]
        public void Secrets_are_encrypted_on_disk()
        {
            var s = SettingsStore.CreateDefaults();
            s.MikrotikSettings.Routers.Add(new RouterConfig { Name = "r1", Password = "s3cret-router-pw" });
            s.AnthropicSettings.ApiKey = "sk-ant-test-key";
            SettingsStore.Save(s);

            var raw = File.ReadAllText(AppPaths.SettingsFile);
            Assert.DoesNotContain("s3cret-router-pw", raw);
            Assert.DoesNotContain("sk-ant-test-key", raw);
            Assert.Contains("dpapi:", raw);

            var loaded = SettingsStore.Load();
            Assert.Equal("s3cret-router-pw", loaded.MikrotikSettings.Routers.Single(r => r.Name == "r1").Password);
            Assert.Equal("sk-ant-test-key", loaded.AnthropicSettings.ApiKey);
        }

        [Fact]
        public void Telegram_message_is_html_escaped()
        {
            var html = TelegramService.FormatFindings("core<1>", new List<Finding>
            {
                new() { Severity = Severity.Critical, Title = "<pppoe-john> & co down", SuggestedCheck = "/ppp active print", Occurrences = 3, FirstSeen = DateTime.Now, LastSeen = DateTime.Now }
            });
            Assert.Contains("&lt;pppoe-john&gt; &amp; co down", html);
            Assert.Contains("core&lt;1&gt;", html);
            Assert.Contains("<code>/ppp active print</code>", html);
        }

        [Fact]
        public void Terms_are_embedded_for_the_about_screen()
        {
            using var s = typeof(SettingsStore).Assembly.GetManifestResourceStream("SimpleMikrotikMonitor.TERMS.md");
            Assert.NotNull(s);
            Assert.Contains("Antmanza Labs", new StreamReader(s!).ReadToEnd());
        }

        [Fact]
        public void Triage_prompt_is_embedded()
        {
            var prompt = LogAnalyzer.LoadTriagePrompt();
            Assert.Contains("RouterOS", prompt);
            Assert.Contains("<router_logs>", prompt);
        }
    }
}
