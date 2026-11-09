using System.Text.Json;
using SimpleMikrotikMonitor.Services;

namespace SimpleMikrotikMonitor.Analysis
{
    /// <summary>
    /// Remembers, across restarts, which log lines were already processed and what was already
    /// reported, so the same issue is not re-analysed or re-alerted every poll.
    /// </summary>
    public sealed class AlertMemory
    {
        public sealed class Record
        {
            public string Router { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public Severity Severity { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public DateTime? LastAnalyzed { get; set; }
            public DateTime? LastNotified { get; set; }
            public Severity? NotifiedSeverity { get; set; }
            public int TimesSeen { get; set; }
        }

        public sealed class RouterState
        {
            /// <summary>Hashes of log lines already processed (capped).</summary>
            public List<string> SeenLines { get; set; } = new();
            public int ConsecutiveFailures { get; set; }
            public bool UnreachableNotified { get; set; }
        }

        private sealed class FileModel
        {
            public Dictionary<string, Record> Records { get; set; } = new();
            public Dictionary<string, RouterState> Routers { get; set; } = new();
            public Dictionary<string, long[]> TokenUsageByDay { get; set; } = new();
        }

        private const int MaxSeenLinesPerRouter = 5000;
        private readonly object _lock = new();
        private readonly string _path;
        private FileModel _data;

        public AlertMemory(string? path = null)
        {
            _path = path ?? AppPaths.MemoryFile;
            _data = TryLoad() ?? new FileModel();
        }

        public RouterState StateFor(string router)
        {
            lock (_lock)
            {
                if (!_data.Routers.TryGetValue(router, out var st))
                    _data.Routers[router] = st = new RouterState();
                return st;
            }
        }

        public static string LineHash(string router, LogEvent e) => Fingerprint.Hash($"{router}|{e.Time}|{e.Topics}|{e.Message}");

        /// <summary>Returns only lines not processed before, and records them as processed.</summary>
        public List<LogEvent> TakeNewLines(string router, IEnumerable<LogEvent> lines)
        {
            lock (_lock)
            {
                var st = StateFor(router);
                var seen = new HashSet<string>(st.SeenLines);
                var fresh = new List<LogEvent>();
                foreach (var l in lines)
                {
                    var h = LineHash(router, l);
                    if (seen.Add(h))
                    {
                        fresh.Add(l);
                        st.SeenLines.Add(h);
                    }
                }
                if (st.SeenLines.Count > MaxSeenLinesPerRouter)
                    st.SeenLines.RemoveRange(0, st.SeenLines.Count - MaxSeenLinesPerRouter);
                return fresh;
            }
        }

        /// <summary>Un-marks lines so the next check retries them (used when the AI call failed).</summary>
        public void ForgetLines(string router, IEnumerable<LogEvent> lines)
        {
            lock (_lock)
            {
                var hashes = lines.Select(l => LineHash(router, l)).ToHashSet();
                StateFor(router).SeenLines.RemoveAll(hashes.Contains);
            }
        }

        public Record? Get(string fingerprint)
        {
            lock (_lock) return _data.Records.TryGetValue(fingerprint, out var r) ? r : null;
        }

        /// <summary>
        /// Should this group go to the AI? No if it was analysed recently: harmless verdicts are
        /// trusted for <paramref name="benignDays"/>, others for <paramref name="cooldownHours"/>.
        /// </summary>
        public bool NeedsAnalysis(string fingerprint, int cooldownHours, int benignDays)
        {
            var r = Get(fingerprint);
            if (r?.LastAnalyzed == null) return true;
            var age = DateTime.Now - r.LastAnalyzed.Value;
            return r.Severity <= Severity.Low ? age.TotalDays >= benignDays : age.TotalHours >= cooldownHours;
        }

        public void Observe(string router, string fingerprint, int count, DateTime first, DateTime last)
        {
            lock (_lock)
            {
                if (!_data.Records.TryGetValue(fingerprint, out var r))
                    _data.Records[fingerprint] = r = new Record { Router = router, FirstSeen = first, Severity = Severity.Info };
                r.TimesSeen += count;
                if (last > r.LastSeen) r.LastSeen = last;
            }
        }

        public void MarkAnalyzed(string fingerprint, Severity severity, string title)
        {
            lock (_lock)
            {
                if (!_data.Records.TryGetValue(fingerprint, out var r)) return;
                r.LastAnalyzed = DateTime.Now;
                r.Severity = severity;
                if (!string.IsNullOrWhiteSpace(title)) r.Title = title;
            }
        }

        /// <summary>
        /// First time, or the issue got worse, or the cooldown passed and it is still happening.
        /// </summary>
        public bool ShouldNotify(Finding f, int cooldownHours)
        {
            lock (_lock)
            {
                var records = f.Fingerprints.Select(fp => _data.Records.TryGetValue(fp, out var r) ? r : null).Where(r => r != null).ToList();
                if (records.Count == 0) return true;
                if (records.Any(r => r!.LastNotified == null)) return true;
                if (records.Any(r => r!.NotifiedSeverity < f.Severity)) return true;
                return records.All(r => (DateTime.Now - r!.LastNotified!.Value).TotalHours >= cooldownHours);
            }
        }

        public void MarkNotified(Finding f)
        {
            lock (_lock)
            {
                foreach (var fp in f.Fingerprints)
                {
                    if (!_data.Records.TryGetValue(fp, out var r))
                        _data.Records[fp] = r = new Record { Router = f.RouterName, FirstSeen = f.FirstSeen, Title = f.Title };
                    r.LastNotified = DateTime.Now;
                    r.NotifiedSeverity = f.Severity;
                    r.Severity = f.Severity;
                }
            }
        }

        public void AddTokenUsage(long input, long output, long cacheRead)
        {
            lock (_lock)
            {
                var key = DateTime.Today.ToString("yyyy-MM-dd");
                if (!_data.TokenUsageByDay.TryGetValue(key, out var u)) _data.TokenUsageByDay[key] = u = new long[3];
                u[0] += input; u[1] += output; u[2] += cacheRead;
            }
        }

        public long[] TokenUsageToday()
        {
            lock (_lock)
                return _data.TokenUsageByDay.TryGetValue(DateTime.Today.ToString("yyyy-MM-dd"), out var u) ? (long[])u.Clone() : new long[3];
        }

        public void Save()
        {
            lock (_lock)
            {
                // Forget records not seen for 30 days and usage older than 90 days.
                foreach (var k in _data.Records.Where(kv => (DateTime.Now - kv.Value.LastSeen).TotalDays > 30).Select(kv => kv.Key).ToList())
                    _data.Records.Remove(k);
                foreach (var k in _data.TokenUsageByDay.Keys.Where(k => DateTime.TryParse(k, out var d) && (DateTime.Today - d).TotalDays > 90).ToList())
                    _data.TokenUsageByDay.Remove(k);

                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_data, SettingsStore.JsonOptions));
                File.Move(tmp, _path, overwrite: true);
            }
        }

        private FileModel? TryLoad()
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), SettingsStore.JsonOptions) : null;
            }
            catch
            {
                return null; // corrupt file: start fresh rather than crash the monitor
            }
        }
    }
}
