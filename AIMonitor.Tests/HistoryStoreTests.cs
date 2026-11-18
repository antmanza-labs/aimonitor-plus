using SimpleMikrotikMonitor;
using SimpleMikrotikMonitor.Analysis;
using Xunit;

namespace AIMonitor.Tests
{
    public class HistoryStoreTests
    {
        private readonly HistoryStore _store = new(Path.Combine(Path.GetTempPath(), "hist-" + Guid.NewGuid().ToString("N")));

        private static CheckRecord Check(string router, DateTime time, params (Severity sev, string status, bool sent)[] findings) => new()
        {
            Router = router,
            Time = time,
            Success = true,
            Message = "test",
            Findings = findings.Select(f => new FindingRecord { Severity = f.sev, Title = $"{f.sev} thing", AlertStatus = f.status, AlertSent = f.sent }).ToList()
        };

        [Fact]
        public void Every_finding_is_kept_with_its_alert_status()
        {
            _store.Append(Check("core1", DateTime.Now,
                (Severity.Critical, "Sent to Telegram", true),
                (Severity.Medium, "Not sent: below High", false),
                (Severity.Info, "Routine, no alert", false)));

            var r = Assert.Single(_store.Load(DateTime.Now.AddHours(-1)));
            Assert.Equal(3, r.Findings.Count);
            Assert.Equal(Severity.Critical, r.TopSeverity);
            Assert.Equal(1, r.AlertsSent);
            Assert.Contains(r.Findings, f => f.AlertStatus == "Not sent: below High");
        }

        [Fact]
        public void Load_filters_by_router_and_period_newest_first()
        {
            _store.Append(Check("core1", DateTime.Now.AddDays(-3)));
            _store.Append(Check("core1", DateTime.Now.AddMinutes(-5)));
            _store.Append(Check("pop2", DateTime.Now.AddMinutes(-2)));

            Assert.Equal(3, _store.Load(DateTime.Now.AddDays(-7)).Count);
            Assert.Single(_store.Load(DateTime.Now.AddHours(-24), "core1"));
            var all = _store.Load(DateTime.Now.AddDays(-7));
            Assert.Equal("pop2", all[0].Router);
        }

        [Fact]
        public void Clear_one_router_keeps_the_others()
        {
            _store.Append(Check("core1", DateTime.Now));
            _store.Append(Check("pop2", DateTime.Now));
            _store.Clear("core1");

            var left = Assert.Single(_store.Load(DateTime.Now.AddHours(-1)));
            Assert.Equal("pop2", left.Router);
        }

        [Fact]
        public void Old_history_in_settings_is_imported_once()
        {
            var s = new AppSettings();
            var r = new RouterConfig { Name = "legacy" };
            r.History.Add(new RouterHistoryEntry { Timestamp = DateTime.Now.AddMinutes(-10), Success = false, Message = "Failed to connect" });
            s.MikrotikSettings.Routers.Add(r);

            Assert.Equal(1, _store.ImportLegacy(s));
            Assert.Empty(r.History);
            Assert.Equal(0, _store.ImportLegacy(s));
            Assert.Equal("Failed to connect", Assert.Single(_store.Load(DateTime.Now.AddHours(-1))).Message);
        }
    }
}
