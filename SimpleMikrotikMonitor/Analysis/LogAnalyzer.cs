using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimpleMikrotikMonitor.Engines;
using SimpleMikrotikMonitor.Services;

namespace SimpleMikrotikMonitor.Analysis
{
    /// <summary>
    /// The monitoring brain: new lines only -> user rules -> MikroTik noise filter -> collapse repeats
    /// -> skip what was analysed recently -> one AI call -> findings. No AI call when nothing new is left.
    /// </summary>
    public sealed class LogAnalyzer
    {
        public const string TriageSchema = """
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string" },
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "event_ids": { "type": "array", "items": { "type": "string" } },
                  "severity": { "type": "string", "enum": ["critical", "high", "medium", "low", "info"] },
                  "title": { "type": "string" },
                  "impact": { "type": "string" },
                  "likely_cause": { "type": "string" },
                  "suggested_check": { "type": "string" }
                },
                "required": ["event_ids", "severity", "title", "impact", "likely_cause", "suggested_check"],
                "additionalProperties": false
              }
            }
          },
          "required": ["summary", "findings"],
          "additionalProperties": false
        }
        """;

        private readonly Func<AppSettings> _settings;
        private readonly AlertMemory _memory;
        private readonly Action<string> _log;
        private readonly Dictionary<string, DisconnectTracker> _disconnects = new();
        private readonly Func<AppSettings, IAnalysisEngine> _engineFactory;

        public LogAnalyzer(Func<AppSettings> settings, AlertMemory memory, Action<string> log, Func<AppSettings, IAnalysisEngine>? engineFactory = null)
        {
            _settings = settings;
            _memory = memory;
            _log = log;
            _engineFactory = engineFactory ?? EngineFactory.Create;
        }

        public AlertMemory Memory => _memory;

        public async Task<AnalysisOutcome> AnalyzeAsync(RouterConfig router, IReadOnlyList<LogEvent> lines, CancellationToken ct = default)
        {
            var s = _settings();
            var a = s.AnalysisSettings;
            var outcome = new AnalysisOutcome { LinesReceived = lines.Count };

            // 1. Only lines not processed before. On the very first check of a router, ignore old history.
            var state = _memory.StateFor(router.Name);
            bool firstRun = state.SeenLines.Count == 0;
            var fresh = _memory.TakeNewLines(router.Name, lines);
            if (firstRun)
            {
                var since = DateTime.Now.AddMinutes(-Math.Max(60, router.Schedule.IntervalMinutes * 2));
                var before = fresh.Count;
                fresh = fresh.Where(l => l.Timestamp >= since).ToList();
                if (before > fresh.Count)
                    _log($"First check of {router.Name}: skipped {before - fresh.Count} older log lines (only the last hour is analysed).");
            }
            outcome.NewLines = fresh.Count;
            if (fresh.Count == 0) return outcome;

            // 2. User rules, 3. noise filter, 4. collapse.
            var groups = new Dictionary<string, EventGroup>();
            var aggregates = new Dictionary<string, List<LogEvent>>();
            var rules = s.RulesSettings.EnableRules ? s.RulesSettings.Rules.Where(r => r.IsEnabled).ToList() : new List<MonitoringRule>();
            var tracker = TrackerFor(router.Name);

            foreach (var line in fresh)
            {
                if (rules.Any(r => r.Action == RuleAction.Ignore && Matches(line, r)))
                {
                    outcome.DroppedAsNoise++;
                    continue;
                }

                var monitorRule = rules.FirstOrDefault(r => r.Action == RuleAction.Monitor && Matches(line, r));

                foreach (var rule in rules.Where(r => r.Action == RuleAction.MultipleDisconnects))
                    CheckDisconnectRule(router, line, rule, tracker, groups);

                NoiseVerdict verdict = monitorRule != null || !a.UseBuiltInNoiseFilter
                    ? new NoiseVerdict(NoiseAction.Keep)
                    : MikrotikNoiseFilter.Classify(line, router.Username);

                switch (verdict.Action)
                {
                    case NoiseAction.Drop:
                        outcome.DroppedAsNoise++;
                        continue;
                    case NoiseAction.Aggregate:
                        if (!aggregates.TryGetValue(verdict.Category!, out var list)) aggregates[verdict.Category!] = list = new();
                        list.Add(line);
                        continue;
                }

                var fp = Fingerprint.Of(router.Name, line.Topics, line.Message);
                if (!groups.TryGetValue(fp, out var g))
                    groups[fp] = g = new EventGroup { Fingerprint = fp, Topics = line.Topics, First = line.Timestamp, Last = line.Timestamp };
                g.Count++;
                g.SampleMessage = line.Message;
                if (line.Timestamp < g.First) g.First = line.Timestamp;
                if (line.Timestamp > g.Last) g.Last = line.Timestamp;
                if (verdict.Hint is { } h && (g.Hint == null || h > g.Hint)) g.Hint = h;
                if (monitorRule != null && !string.IsNullOrWhiteSpace(monitorRule.LlmInstructions))
                    g.RuleNote = $"Rule '{monitorRule.Name}': {monitorRule.LlmInstructions}";
            }

            // Subscriber churn only matters in bulk.
            foreach (var (category, list) in aggregates)
            {
                if (list.Count < Math.Max(1, a.MassEventThreshold))
                {
                    outcome.DroppedAsNoise += list.Count;
                    continue;
                }
                var fp = Fingerprint.Hash($"{router.Name}|summary|{category}");
                groups[fp] = new EventGroup
                {
                    Fingerprint = fp,
                    Topics = "summary",
                    SampleMessage = "[summary] " + MikrotikNoiseFilter.DescribeAggregate(category, list),
                    Count = list.Count,
                    First = list.Min(l => l.Timestamp),
                    Last = list.Max(l => l.Timestamp),
                    IsSynthetic = true
                };
            }

            // 5. Remember sightings; skip what was analysed recently.
            var toSend = new List<EventGroup>();
            foreach (var g in groups.Values)
            {
                g.PriorSightings = _memory.Get(g.Fingerprint)?.TimesSeen ?? 0;
                _memory.Observe(router.Name, g.Fingerprint, g.Count, g.First, g.Last);
                if (g.IsSynthetic || _memory.NeedsAnalysis(g.Fingerprint, a.RepeatCooldownHours, a.BenignMemoryDays))
                    toSend.Add(g);
                else
                    outcome.SkippedAsKnown += g.Count;
            }

            if (toSend.Count == 0) return outcome;

            // Most important first; cap the batch size.
            var ordered = toSend
                .OrderByDescending(g => g.Hint ?? Severity.Info)
                .ThenByDescending(g => g.RuleNote != null)
                .ThenByDescending(g => g.Topics.Contains("critical") || g.Topics.Contains("error") || g.Topics.Contains("warning"))
                .ThenByDescending(g => g.Count)
                .ToList();
            var batch = ordered.Take(Math.Max(10, a.MaxEventsPerCall)).ToList();
            int omitted = ordered.Count - batch.Count;
            for (int i = 0; i < batch.Count; i++) batch[i].Id = $"E{i + 1}";
            outcome.SentToAi = batch.Sum(g => g.Count);

            // 6. Ask the AI.
            IAnalysisEngine engine;
            try
            {
                engine = _engineFactory(s);
            }
            catch (EngineException ex)
            {
                outcome.EngineError = ex.Message;
                outcome.Findings.AddRange(Fallback(router, batch));
                _memory.ForgetLines(router.Name, fresh);
                return outcome;
            }

            var masker = engine.SendsDataOffPc && a.MaskSensitiveData ? new SensitiveDataMasker() : null;
            var system = BuildSystemPrompt(s);
            var user = BuildUserPrompt(router, batch, fresh.Count, omitted, masker);

            ParsedAnswer parsed;
            try
            {
                var resp = await engine.CompleteAsync(new EngineRequest(system, user, TriageSchema), ct);
                Track(outcome, engine, resp);
                parsed = Parse(resp.Text);
            }
            catch (Exception ex) when (ex is EngineException or JsonException or InvalidOperationException)
            {
                outcome.EngineError = ex is EngineException ? ex.Message : $"The AI answer could not be read ({ex.Message}).";
                outcome.Findings.AddRange(Fallback(router, batch));
                _memory.ForgetLines(router.Name, fresh);   // retry these lines on the next check
                return outcome;
            }

            var findings = ToFindings(router, parsed, batch, masker);

            // 7. Optional second look by a stronger model.
            var serious = findings.Where(f => f.Severity >= Severity.High).ToList();
            if (engine.CanEscalate && serious.Count > 0)
            {
                try
                {
                    var ids = serious.SelectMany(f => f.Fingerprints).ToHashSet();
                    var subset = batch.Where(g => ids.Contains(g.Fingerprint)).ToList();
                    var escUser = BuildUserPrompt(router, subset, fresh.Count, 0, masker) +
                                  "\n\nA first-pass triage rated these events High or Critical. Re-check them carefully: confirm or correct the severity, and sharpen impact, likely cause and the read-only check.";
                    var resp = await engine.CompleteAsync(new EngineRequest(system, escUser, TriageSchema, Escalation: true), ct);
                    Track(outcome, engine, resp);
                    var refined = ToFindings(router, Parse(resp.Text), subset, masker);
                    if (refined.Count > 0)
                    {
                        findings.RemoveAll(f => serious.Contains(f));
                        findings.AddRange(refined);
                    }
                }
                catch (Exception ex) when (ex is EngineException or JsonException or InvalidOperationException)
                {
                    _log($"Second-look model failed, keeping first-pass result: {ex.Message}");
                }
            }

            // 8. Remember verdicts. Events the AI did not mention count as routine.
            foreach (var f in findings)
                foreach (var fp in f.Fingerprints)
                    _memory.MarkAnalyzed(fp, f.Severity, f.Title);
            foreach (var g in batch.Where(g => !findings.Any(f => f.Fingerprints.Contains(g.Fingerprint))))
                _memory.MarkAnalyzed(g.Fingerprint, Severity.Info, "");

            outcome.EngineSummary = masker?.Unmask(parsed.Summary) ?? parsed.Summary;
            outcome.Findings.AddRange(findings.OrderByDescending(f => f.Severity));
            return outcome;
        }

        /// <summary>Called when a router can't be reached. Returns a finding once the failure threshold is hit.</summary>
        public Finding? OnConnectFailed(RouterConfig router, string error)
        {
            var threshold = _settings().AnalysisSettings.UnreachableAfterFailures;
            var st = _memory.StateFor(router.Name);
            st.ConsecutiveFailures++;
            if (threshold <= 0 || st.ConsecutiveFailures < threshold || st.UnreachableNotified) return null;

            st.UnreachableNotified = true;
            return new Finding
            {
                RouterName = router.Name,
                Severity = router.Schedule.Priority >= RouterPriority.High ? Severity.Critical : Severity.High,
                Title = $"Router unreachable ({st.ConsecutiveFailures} checks in a row)",
                Impact = "The monitor can't reach the router's API. The router may be down, or only the management path is broken.",
                LikelyCause = error,
                SuggestedCheck = $"Ping {router.RouterIP}; check the upstream device and the router's power. If it pings, check /ip service (api{(router.UseSSL ? "-ssl" : "")}) and firewall input rules.",
                Fingerprints = { Fingerprint.Hash($"{router.Name}|unreachable") },
                FirstSeen = DateTime.Now,
                LastSeen = DateTime.Now,
                FromRules = true
            };
        }

        /// <summary>Called after a successful connection. Returns a recovery finding if "unreachable" was reported.</summary>
        public Finding? OnConnected(RouterConfig router)
        {
            var st = _memory.StateFor(router.Name);
            var failures = st.ConsecutiveFailures;
            st.ConsecutiveFailures = 0;
            if (!st.UnreachableNotified) return null;

            st.UnreachableNotified = false;
            return new Finding
            {
                RouterName = router.Name,
                Severity = Severity.Info,
                Title = "Router reachable again",
                Impact = $"API connection restored after {failures} failed checks.",
                Fingerprints = { Fingerprint.Hash($"{router.Name}|unreachable") },
                FirstSeen = DateTime.Now,
                LastSeen = DateTime.Now,
                FromRules = true,
                IsRecovery = true
            };
        }

        // ---------- prompts ----------

        public static string LoadTriagePrompt()
        {
            if (File.Exists(AppPaths.PromptOverrideFile))
                return File.ReadAllText(AppPaths.PromptOverrideFile);

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SimpleMikrotikMonitor.Prompts.mikrotik-triage.md")
                ?? throw new InvalidOperationException("Built-in triage prompt missing from the build.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string BuildSystemPrompt(AppSettings s)
        {
            var sb = new StringBuilder(LoadTriagePrompt());
            if (!string.IsNullOrWhiteSpace(s.LLMSettings.Instructions))
            {
                sb.AppendLine().AppendLine().AppendLine("## Operator notes about this network");
                sb.AppendLine(s.LLMSettings.Instructions.Trim());
            }
            return sb.ToString();
        }

        private static string BuildUserPrompt(RouterConfig router, List<EventGroup> batch, int newLines, int omitted, SensitiveDataMasker? masker)
        {
            string M(string t) => masker?.Mask(t) ?? t;

            var sb = new StringBuilder();
            sb.AppendLine($"Router: {router.Name}");
            sb.AppendLine($"Monitoring priority: {router.Schedule.Priority}");
            sb.AppendLine($"Local time on the monitoring PC: {DateTime.Now:yyyy-MM-dd HH:mm} ({DateTime.Now:dddd})");
            sb.AppendLine($"This check: {newLines} new log lines, {batch.Count} distinct events after filtering" +
                          (omitted > 0 ? $"; {omitted} lower-priority events left out to keep the batch small." : "."));
            sb.AppendLine();
            sb.AppendLine("<router_logs>");
            foreach (var g in batch)
            {
                var span = g.First == g.Last ? $"{g.First:MM-dd HH:mm:ss}" : $"{g.First:MM-dd HH:mm:ss}..{g.Last:HH:mm:ss}";
                var line = $"{g.Id} | x{g.Count} | {span} | {g.Topics} | {M(g.SampleMessage)}";
                if (g.Hint != null) line += $" | hint: {g.Hint.ToString()!.ToLowerInvariant()}";
                if (g.PriorSightings > 0) line += $" | seen before: {g.PriorSightings}";
                sb.AppendLine(line.Replace("</router_logs>", "</router-logs>", StringComparison.OrdinalIgnoreCase));
            }
            sb.AppendLine("</router_logs>");

            var notes = batch.Where(g => g.RuleNote != null).ToList();
            if (notes.Count > 0)
            {
                sb.AppendLine().AppendLine("Operator rule notes (trusted, from the app's rule list):");
                foreach (var g in notes) sb.AppendLine($"- {g.Id}: {g.RuleNote}");
            }
            return sb.ToString();
        }

        // ---------- answer handling ----------

        private sealed record ParsedFinding(List<string> EventIds, Severity Severity, string Title, string Impact, string LikelyCause, string SuggestedCheck);
        private sealed record ParsedAnswer(string Summary, List<ParsedFinding> Findings);

        private static ParsedAnswer Parse(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) throw new JsonException("no JSON object in the answer");
            var root = JsonDocument.Parse(text[start..(end + 1)]).RootElement;

            string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            var list = new List<ParsedFinding>();
            if (root.TryGetProperty("findings", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in arr.EnumerateArray())
                {
                    var ids = f.TryGetProperty("event_ids", out var idArr) && idArr.ValueKind == JsonValueKind.Array
                        ? idArr.EnumerateArray().Select(x => x.ToString().Trim()).ToList()
                        : new List<string>();
                    var sev = Enum.TryParse<Severity>(Str(f, "severity"), true, out var parsedSev) ? parsedSev : Severity.Medium;
                    list.Add(new ParsedFinding(ids, sev, Str(f, "title"), Str(f, "impact"), Str(f, "likely_cause"), Str(f, "suggested_check")));
                }
            }
            return new ParsedAnswer(Str(root, "summary"), list);
        }

        private static List<Finding> ToFindings(RouterConfig router, ParsedAnswer parsed, List<EventGroup> batch, SensitiveDataMasker? masker)
        {
            string U(string t) => masker?.Unmask(t) ?? t;
            var byId = batch.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
            var result = new List<Finding>();

            foreach (var pf in parsed.Findings)
            {
                var groups = pf.EventIds.Select(id => byId.TryGetValue(id, out var g) ? g : null).Where(g => g != null).Cast<EventGroup>().ToList();
                if (groups.Count == 0) continue;

                var severity = pf.Severity;
                // Safety net: never let a kernel failure / out-of-memory be filed as routine.
                if (groups.Any(g => g.Hint == Severity.Critical) && severity < Severity.High) severity = Severity.High;

                result.Add(new Finding
                {
                    RouterName = router.Name,
                    Severity = severity,
                    Title = U(pf.Title),
                    Impact = U(pf.Impact),
                    LikelyCause = U(pf.LikelyCause),
                    SuggestedCheck = U(pf.SuggestedCheck),
                    Fingerprints = groups.Select(g => g.Fingerprint).Distinct().ToList(),
                    Occurrences = groups.Sum(g => g.Count),
                    FirstSeen = groups.Min(g => g.First),
                    LastSeen = groups.Max(g => g.Last)
                });
            }
            return result;
        }

        /// <summary>When the AI is unavailable, still alert on events the rule engine considers serious.</summary>
        private static IEnumerable<Finding> Fallback(RouterConfig router, List<EventGroup> batch) =>
            batch.Where(g => g.Hint >= Severity.High).Select(g => new Finding
            {
                RouterName = router.Name,
                Severity = g.Hint!.Value,
                Title = Truncate(g.SampleMessage, 80),
                Impact = "Not analysed: the AI engine is unavailable. Raised by the built-in MikroTik rules.",
                LikelyCause = "",
                SuggestedCheck = "",
                Fingerprints = { g.Fingerprint },
                Occurrences = g.Count,
                FirstSeen = g.First,
                LastSeen = g.Last,
                FromRules = true
            });

        private void Track(AnalysisOutcome outcome, IAnalysisEngine engine, EngineResponse r)
        {
            _memory.AddTokenUsage(r.InputTokens, r.OutputTokens, r.CacheReadTokens);
            var note = $"{r.ModelUsed}: {r.InputTokens:N0} in ({r.CacheReadTokens:N0} cached) / {r.OutputTokens:N0} out" +
                       (r.CostUsd is { } c ? $", ${c:0.0000}" : "");
            outcome.UsageNote = outcome.UsageNote == null ? note : outcome.UsageNote + "; " + note;
        }

        // ---------- user rules ----------

        private DisconnectTracker TrackerFor(string router)
        {
            if (!_disconnects.TryGetValue(router, out var t)) _disconnects[router] = t = new DisconnectTracker();
            t.CleanupOldRecords(24 * 60);
            return t;
        }

        private static void CheckDisconnectRule(RouterConfig router, LogEvent line, MonitoringRule rule, DisconnectTracker tracker, Dictionary<string, EventGroup> groups)
        {
            if (!line.Message.Contains("disconnect", StringComparison.OrdinalIgnoreCase)) return;
            var device = DisconnectTracker.ExtractDeviceId(line.Message);
            if (string.IsNullOrEmpty(device)) return;
            if (!string.IsNullOrEmpty(rule.DevicePattern) && !SafeRegex(device, rule.DevicePattern)) return;

            tracker.TrackDisconnect(device, line.Timestamp);
            if (!tracker.HasExceededThreshold(device, rule.DisconnectThreshold, rule.TimeWindowMinutes)) return;

            var fp = Fingerprint.Hash($"{router.Name}|disconnects|{device.ToLowerInvariant()}");
            var count = tracker.GetDisconnectCount(device, rule.TimeWindowMinutes);
            groups[fp] = new EventGroup
            {
                Fingerprint = fp,
                Topics = "rule",
                SampleMessage = $"[summary] Device {device} disconnected {count} times in the last {rule.TimeWindowMinutes} minutes. Last: {line.Message}",
                Count = count,
                First = line.Timestamp,
                Last = line.Timestamp,
                Hint = Severity.Medium,
                RuleNote = string.IsNullOrWhiteSpace(rule.LlmInstructions) ? $"Rule '{rule.Name}'" : $"Rule '{rule.Name}': {rule.LlmInstructions}",
                IsSynthetic = true
            };
        }

        private static bool Matches(LogEvent line, MonitoringRule rule)
        {
            if (string.IsNullOrEmpty(rule.Pattern)) return false;
            var text = line.Raw;
            return rule.IsRegex ? SafeRegex(text, rule.Pattern) : text.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SafeRegex(string input, string pattern)
        {
            try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)); }
            catch (ArgumentException) { return input.Contains(pattern, StringComparison.OrdinalIgnoreCase); }
            catch (RegexMatchTimeoutException) { return false; }
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
    }
}
