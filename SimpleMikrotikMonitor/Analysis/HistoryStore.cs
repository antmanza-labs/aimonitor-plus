using System.Text;
using System.Text.Json;
using SimpleMikrotikMonitor.Services;

namespace SimpleMikrotikMonitor.Analysis
{
    /// <summary>One finding as it was recorded in history, including what happened to its alert.</summary>
    public sealed class FindingRecord
    {
        public Severity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public string LikelyCause { get; set; } = string.Empty;
        public string SuggestedCheck { get; set; } = string.Empty;
        public int Occurrences { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public bool FromRules { get; set; }
        public bool IsRecovery { get; set; }
        /// <summary>e.g. "Sent to Telegram", "Not sent: below High", "Not sent: already reported".</summary>
        public string AlertStatus { get; set; } = string.Empty;
        public bool AlertSent { get; set; }

        public static FindingRecord From(Finding f) => new()
        {
            Severity = f.Severity,
            Title = f.Title,
            Impact = f.Impact,
            LikelyCause = f.LikelyCause,
            SuggestedCheck = f.SuggestedCheck,
            Occurrences = f.Occurrences,
            FirstSeen = f.FirstSeen,
            LastSeen = f.LastSeen,
            FromRules = f.FromRules,
            IsRecovery = f.IsRecovery,
            AlertStatus = f.AlertStatus,
            AlertSent = f.AlertSent
        };
    }

    /// <summary>One router check: what came in, what the AI said, and every finding.</summary>
    public sealed class CheckRecord
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Router { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int LinesReceived { get; set; }
        public int NewLines { get; set; }
        public int Filtered { get; set; }
        public int AlreadyKnown { get; set; }
        public int SentToAi { get; set; }
        public string? Engine { get; set; }
        public string? Summary { get; set; }
        public string? EngineError { get; set; }
        public string? Usage { get; set; }
        public List<FindingRecord> Findings { get; set; } = new();

        public Severity? TopSeverity => Findings.Where(f => !f.IsRecovery && f.Severity > Severity.Info)
                                                .Select(f => (Severity?)f.Severity).DefaultIfEmpty(null).Max();
        public int AlertsSent => Findings.Count(f => f.AlertSent);
        public bool HasFindings => Findings.Any(f => f.Severity > Severity.Info || f.IsRecovery);
    }

    /// <summary>
    /// Check history in %APPDATA%\AIMonitorPlus\history\history-yyyy-MM-dd.jsonl: one JSON line per check,
    /// append-only (cheap to write every few minutes), one file per day, old days deleted after the retention period.
    /// </summary>
    public sealed class HistoryStore
    {
        private static readonly JsonSerializerOptions LineOptions = new()
        {
            WriteIndented = false,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        private readonly object _lock = new();
        private readonly string _dir;
        private DateTime _lastPrune = DateTime.MinValue;

        public HistoryStore(string? dir = null)
        {
            _dir = dir ?? Path.Combine(AppPaths.DataDir, "history");
            Directory.CreateDirectory(_dir);
        }

        public int RetentionDays { get; set; } = 30;

        private string FileFor(DateTime day) => Path.Combine(_dir, $"history-{day:yyyy-MM-dd}.jsonl");

        public void Append(CheckRecord record)
        {
            var line = JsonSerializer.Serialize(record, LineOptions);
            lock (_lock)
            {
                File.AppendAllText(FileFor(record.Time), line + Environment.NewLine, Encoding.UTF8);
                if ((DateTime.Now - _lastPrune).TotalHours >= 6) Prune();
            }
        }

        /// <summary>Records from <paramref name="since"/> up to now, newest first. Router null = all routers.</summary>
        public List<CheckRecord> Load(DateTime since, string? router = null)
        {
            var result = new List<CheckRecord>();
            lock (_lock)
            {
                foreach (var file in FilesFrom(since))
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        CheckRecord? r;
                        try { r = JsonSerializer.Deserialize<CheckRecord>(line, LineOptions); }
                        catch (JsonException) { continue; }   // a half-written line after a crash
                        if (r == null || r.Time < since) continue;
                        if (router != null && !r.Router.Equals(router, StringComparison.OrdinalIgnoreCase)) continue;
                        result.Add(r);
                    }
                }
            }
            return result.OrderByDescending(r => r.Time).ToList();
        }

        /// <summary>Deletes history for one router (or all when null).</summary>
        public void Clear(string? router = null)
        {
            lock (_lock)
            {
                foreach (var file in Directory.GetFiles(_dir, "history-*.jsonl"))
                {
                    if (router == null) { File.Delete(file); continue; }
                    var keep = File.ReadLines(file).Where(l =>
                    {
                        try { return JsonSerializer.Deserialize<CheckRecord>(l, LineOptions)?.Router.Equals(router, StringComparison.OrdinalIgnoreCase) != true; }
                        catch (JsonException) { return false; }
                    }).ToList();
                    if (keep.Count == 0) File.Delete(file); else File.WriteAllLines(file, keep, Encoding.UTF8);
                }
            }
        }

        private IEnumerable<string> FilesFrom(DateTime since) =>
            Directory.GetFiles(_dir, "history-*.jsonl")
                     .Where(f => DayOf(f) is { } d && d >= since.Date)
                     .OrderBy(f => f);

        private void Prune()
        {
            _lastPrune = DateTime.Now;
            var cutoff = DateTime.Today.AddDays(-Math.Max(1, RetentionDays));
            foreach (var f in Directory.GetFiles(_dir, "history-*.jsonl"))
                if (DayOf(f) is { } d && d < cutoff)
                    try { File.Delete(f); } catch (IOException) { /* in use; next prune */ }
        }

        private static DateTime? DayOf(string file) =>
            DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file).Replace("history-", ""), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

        /// <summary>Moves v1/v2.0 history (stored inside settings.json) into the history files.</summary>
        public int ImportLegacy(AppSettings settings)
        {
            int moved = 0;
            foreach (var router in settings.MikrotikSettings.Routers)
            {
                if (router.History == null) continue;
                foreach (var h in router.History.Where(h => h.Message != "Router added to monitoring").OrderBy(h => h.Timestamp))
                {
                    Append(new CheckRecord
                    {
                        Time = h.Timestamp,
                        Router = router.Name,
                        Success = h.Success,
                        Message = h.Message,
                        NewLines = h.LogCount
                    });
                    moved++;
                }
                router.History = new List<RouterHistoryEntry>();
            }
            return moved;
        }
    }
}
