using System.Globalization;
using System.Text.RegularExpressions;

namespace SimpleMikrotikMonitor.Analysis
{
    /// <summary>One line from a router's /log, as returned by the API.</summary>
    public sealed class LogEvent
    {
        public string Time { get; init; } = string.Empty;
        public string Topics { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;

        /// <summary>Best-effort parsed timestamp (RouterOS logs often omit the date or year).</summary>
        public DateTime Timestamp { get; init; } = DateTime.Now;

        public string Raw => $"[{Time}] {Topics}: {Message}";

        public bool HasTopic(string topic) =>
            Topics.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                  .Any(t => t.Equals(topic, StringComparison.OrdinalIgnoreCase));

        public static LogEvent Create(string time, string topics, string message) => new()
        {
            Time = time ?? string.Empty,
            Topics = topics ?? string.Empty,
            Message = message ?? string.Empty,
            Timestamp = ParseRouterTime(time)
        };

        // RouterOS v7: "2026-11-09 08:10:22" (older days) or "08:10:22" (today).
        // RouterOS v6: "nov/09 08:10:22" or "nov/09/2025 08:10:22" or "08:10:22".
        private static readonly Regex V6Date = new(@"^(?<mon>[a-z]{3})/(?<day>\d{1,2})(?:/(?<year>\d{4}))?\s+(?<time>\d{1,2}:\d{2}:\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static DateTime ParseRouterTime(string? time)
        {
            if (string.IsNullOrWhiteSpace(time)) return DateTime.Now;
            time = time.Trim();

            if (DateTime.TryParseExact(time, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
                return full;

            if (TimeSpan.TryParseExact(time, @"h\:mm\:ss", CultureInfo.InvariantCulture, out var tod) ||
                TimeSpan.TryParseExact(time, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out tod))
            {
                var today = DateTime.Today.Add(tod);
                // A time "later than now" means it was logged yesterday.
                return today > DateTime.Now.AddMinutes(5) ? today.AddDays(-1) : today;
            }

            var m = V6Date.Match(time);
            if (m.Success &&
                DateTime.TryParseExact($"{m.Groups["mon"].Value} {m.Groups["day"].Value} {(m.Groups["year"].Success ? m.Groups["year"].Value : DateTime.Now.Year.ToString())} {m.Groups["time"].Value}",
                    new[] { "MMM d yyyy H:mm:ss", "MMM dd yyyy H:mm:ss", "MMM d yyyy HH:mm:ss", "MMM dd yyyy HH:mm:ss" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var v6))
            {
                return v6 > DateTime.Now.AddDays(1) ? v6.AddYears(-1) : v6;
            }

            return DateTime.Now;
        }
    }

    /// <summary>Identical (normalised) log lines collapsed into one entry.</summary>
    public sealed class EventGroup
    {
        public string Id { get; set; } = string.Empty;          // E1, E2... (per AI call)
        public string Fingerprint { get; init; } = string.Empty;
        public string Topics { get; init; } = string.Empty;
        public string SampleMessage { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTime First { get; set; }
        public DateTime Last { get; set; }
        public Severity? Hint { get; set; }                     // built-in severity hint, if any
        public string? RuleNote { get; set; }                   // instructions from a matching user rule
        public bool IsSynthetic { get; init; }                   // summary line created by the app (e.g. mass PPPoE drop)
        public int PriorSightings { get; set; }
    }

    /// <summary>One issue worth (maybe) telling a human about.</summary>
    public sealed class Finding
    {
        public string RouterName { get; set; } = string.Empty;
        public Severity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public string LikelyCause { get; set; } = string.Empty;
        public string SuggestedCheck { get; set; } = string.Empty;
        public List<string> Fingerprints { get; set; } = new();
        public int Occurrences { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        /// <summary>True when this finding came from app logic, not the AI (engine down, router unreachable).</summary>
        public bool FromRules { get; set; }
        public bool IsRecovery { get; set; }
    }

    public sealed class AnalysisOutcome
    {
        public List<Finding> Findings { get; } = new();
        public int LinesReceived { get; set; }
        public int NewLines { get; set; }
        public int DroppedAsNoise { get; set; }
        public int SkippedAsKnown { get; set; }
        public int SentToAi { get; set; }
        public string? EngineSummary { get; set; }
        public string? EngineError { get; set; }
        public string? UsageNote { get; set; }
    }
}
