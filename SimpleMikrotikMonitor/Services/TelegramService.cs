using System.Net;
using System.Net.Http.Json;
using System.Text;
using SimpleMikrotikMonitor.Analysis;

namespace SimpleMikrotikMonitor.Services
{
    public class TelegramService
    {
        private const int TelegramLimit = 4096;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private readonly TelegramSettings _settings;

        public TelegramService(TelegramSettings settings) => _settings = settings;

        public bool IsConfigured =>
            _settings.EnableNotifications && !string.IsNullOrWhiteSpace(_settings.BotToken) && !string.IsNullOrWhiteSpace(_settings.ChatId);

        /// <summary>Sends one message per router with all its findings, most severe first.</summary>
        public Task<(bool Ok, string? Error)> SendFindingsAsync(string routerName, IReadOnlyList<Finding> findings)
            => SendHtmlAsync(FormatFindings(routerName, findings));

        public static string FormatFindings(string routerName, IReadOnlyList<Finding> findings)
        {
            var sb = new StringBuilder();
            var top = findings.Max(f => f.Severity);
            bool allRecovery = findings.All(f => f.IsRecovery);
            sb.Append(allRecovery ? "✅" : Icon(top)).Append(" <b>").Append(H(routerName)).Append("</b>");
            sb.Append(allRecovery ? " — recovered" : $" — {findings.Count} issue{(findings.Count == 1 ? "" : "s")}").Append('\n');

            foreach (var f in findings.OrderByDescending(f => f.Severity))
            {
                sb.Append('\n');
                sb.Append(f.IsRecovery ? "✅" : Icon(f.Severity)).Append(' ');
                if (!f.IsRecovery) sb.Append("<b>").Append(f.Severity.ToString().ToUpperInvariant()).Append("</b> · ");
                sb.Append("<b>").Append(H(f.Title)).Append("</b>\n");
                if (!string.IsNullOrWhiteSpace(f.Impact)) sb.Append("Impact: ").Append(H(f.Impact)).Append('\n');
                if (!string.IsNullOrWhiteSpace(f.LikelyCause)) sb.Append("Likely cause: ").Append(H(f.LikelyCause)).Append('\n');
                if (!string.IsNullOrWhiteSpace(f.SuggestedCheck)) sb.Append("Check: <code>").Append(H(f.SuggestedCheck)).Append("</code>\n");
                if (f.Occurrences > 1) sb.Append("<i>Seen ").Append(f.Occurrences).Append("× ").Append(f.FirstSeen.ToString("HH:mm")).Append("–").Append(f.LastSeen.ToString("HH:mm")).Append("</i>\n");
            }

            var text = sb.ToString();
            return text.Length <= TelegramLimit ? text : text[..(TelegramLimit - 40)] + "\n<i>…message shortened</i>";
        }

        public async Task<(bool Ok, string? Error)> SendHtmlAsync(string html)
        {
            if (!IsConfigured) return (false, "Telegram is not enabled or not configured.");
            try
            {
                var url = $"https://api.telegram.org/bot{_settings.BotToken.Trim()}/sendMessage";
                var resp = await Http.PostAsJsonAsync(url, new
                {
                    chat_id = _settings.ChatId.Trim(),
                    text = html,
                    parse_mode = "HTML",
                    disable_web_page_preview = true
                });
                if (resp.IsSuccessStatusCode) return (true, null);
                var body = await resp.Content.ReadAsStringAsync();
                return (false, $"Telegram rejected the message ({(int)resp.StatusCode}): {body}");
            }
            catch (Exception ex)
            {
                return (false, $"Telegram send failed: {ex.Message}");
            }
        }

        // Kept for the Settings "Test" button and older callers.
        public async Task<bool> SendMessage(string message) => (await SendHtmlAsync(H(message))).Ok;

        public async Task<bool> TestConnection()
        {
            var wasEnabled = _settings.EnableNotifications;
            _settings.EnableNotifications = true;
            try
            {
                return (await SendHtmlAsync("✅ <b>AIMonitor+</b> test message. Telegram notifications are working.")).Ok;
            }
            finally
            {
                _settings.EnableNotifications = wasEnabled;
            }
        }

        private static string Icon(Severity s) => s switch
        {
            Severity.Critical => "🔴",
            Severity.High => "🟠",
            Severity.Medium => "🟡",
            Severity.Low => "🔵",
            _ => "⚪"
        };

        private static string H(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
    }
}
