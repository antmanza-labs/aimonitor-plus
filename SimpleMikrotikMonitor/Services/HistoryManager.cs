using SimpleMikrotikMonitor.Analysis;

namespace SimpleMikrotikMonitor.Services
{
    /// <summary>Records each router check, with all its findings, into the history files.</summary>
    public class HistoryManager
    {
        private readonly HistoryStore _store;

        public HistoryManager(AppSettings settings, HistoryStore? store = null)
        {
            _store = store ?? new HistoryStore();
            _store.RetentionDays = Math.Max(1, settings.AnalysisSettings.HistoryDays);
        }

        public HistoryStore Store => _store;

        /// <summary>A check that ended before analysis (no IP, connection failed...).</summary>
        public void AddHistoryEntry(RouterConfig router, bool success, string message, IEnumerable<Finding>? findings = null) =>
            _store.Append(new CheckRecord
            {
                Router = router.Name,
                Success = success,
                Message = message,
                Findings = findings?.Select(FindingRecord.From).ToList() ?? new()
            });

        /// <summary>A full check: counts, AI summary and every finding, sent or not.</summary>
        public void AddCheck(RouterConfig router, bool success, string message, AnalysisOutcome outcome, string engine, IEnumerable<Finding> extraFindings) =>
            _store.Append(new CheckRecord
            {
                Router = router.Name,
                Success = success,
                Message = message,
                LinesReceived = outcome.LinesReceived,
                NewLines = outcome.NewLines,
                Filtered = outcome.DroppedAsNoise,
                AlreadyKnown = outcome.SkippedAsKnown,
                SentToAi = outcome.SentToAi,
                Engine = outcome.SentToAi > 0 ? engine : null,
                Summary = outcome.EngineSummary,
                EngineError = outcome.EngineError,
                Usage = outcome.UsageNote,
                Findings = extraFindings.Concat(outcome.Findings).Select(FindingRecord.From).ToList()
            });
    }
}
