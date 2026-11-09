using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SimpleMikrotikMonitor.Controls
{
    /// <summary>
    /// Router status card on the dashboard.
    /// Orange = not checked yet, green = checked and fine, amber = checked with a recent High/Critical alert,
    /// red = unreachable. Fully owner-drawn so it stays crisp and doesn't flicker.
    /// </summary>
    public class DashboardWidget : UserControl
    {
        public enum WidgetState { NotChecked, Online, Alert, Offline, Disabled }

        private static readonly Color NotCheckedColor = Color.FromArgb(234, 120, 32);   // orange
        private static readonly Color OnlineColor = Color.FromArgb(34, 154, 84);        // green
        private static readonly Color AlertColor = Color.FromArgb(232, 170, 0);         // amber
        private static readonly Color OfflineColor = Color.FromArgb(206, 52, 52);       // red
        private static readonly Color DisabledColor = Color.FromArgb(150, 155, 162);    // grey
        private static readonly Color TextMain = Color.FromArgb(32, 33, 36);
        private static readonly Color TextDim = Color.FromArgb(105, 110, 118);

        private readonly RouterConfig _router;
        private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 30_000 };
        private readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 300 };

        private DateTime? _lastCheck;
        private bool _online;
        private string _uptime = "";
        private string? _error;
        private Severity? _alertSeverity;
        private string? _alertTitle;
        private DateTime? _alertTime;

        /// <summary>How long a High/Critical alert keeps the card amber.</summary>
        public TimeSpan AlertHold { get; set; } = TimeSpan.FromHours(6);

        public DashboardWidget(RouterConfig router)
        {
            _router = router;
            Size = new Size(270, 170);
            Margin = new Padding(6);
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Clear alert", null, (_, _) => ClearAlert());
            ContextMenuStrip = menu;

            _refresh.Tick += (_, _) => Invalidate();   // keeps "3 min ago" current
            _refresh.Start();
            UpdateTooltip();
        }

        public WidgetState State =>
            !_router.IsEnabled || !_router.Schedule.Enabled ? WidgetState.Disabled
            : _lastCheck == null ? WidgetState.NotChecked
            : !_online ? WidgetState.Offline
            : _alertTime != null && DateTime.Now - _alertTime.Value < AlertHold ? WidgetState.Alert
            : WidgetState.Online;

        /// <summary>Kept for existing callers: records a check result.</summary>
        public void UpdateInfo(bool isOnline, string lastCheck, string uptime) =>
            SetChecked(isOnline, isOnline ? uptime : "", isOnline ? null : uptime);

        public void SetChecked(bool online, string uptime, string? error = null)
        {
            if (InvokeRequired) { BeginInvoke(() => SetChecked(online, uptime, error)); return; }
            _lastCheck = DateTime.Now;
            _online = online;
            _uptime = FormatUptime(uptime);
            _error = error;
            UpdateTooltip();
            Invalidate();
        }

        public void SetAlert(Severity severity, string title)
        {
            if (InvokeRequired) { BeginInvoke(() => SetAlert(severity, title)); return; }
            _alertSeverity = severity;
            _alertTitle = title;
            _alertTime = DateTime.Now;
            UpdateTooltip();
            Invalidate();
        }

        public void ClearAlert()
        {
            _alertSeverity = null;
            _alertTitle = null;
            _alertTime = null;
            UpdateTooltip();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var state = State;
            var accent = state switch
            {
                WidgetState.NotChecked => NotCheckedColor,
                WidgetState.Online => OnlineColor,
                WidgetState.Alert => AlertColor,
                WidgetState.Disabled => DisabledColor,
                _ => OfflineColor
            };

            // Card with soft shadow, tinted background and a coloured bar on the left.
            var card = new Rectangle(2, 2, Width - 6, Height - 6);
            using (var shadow = RoundedRect(new Rectangle(card.X + 2, card.Y + 3, card.Width, card.Height), 10))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
                g.FillPath(shadowBrush, shadow);
            using (var path = RoundedRect(card, 10))
            {
                using (var bg = new SolidBrush(Blend(Color.White, accent, 0.07f)))
                    g.FillPath(bg, path);
                var oldClip = g.Clip;
                g.SetClip(path);
                using (var bar = new SolidBrush(accent))
                    g.FillRectangle(bar, card.X, card.Y, 6, card.Height);
                g.Clip = oldClip;
                using var border = new Pen(Blend(Color.White, accent, 0.35f));
                g.DrawPath(border, path);
            }

            int left = card.X + 18, right = card.Right - 12, y = card.Y + 10;

            // Header: name + priority
            using var titleFont = new Font("Segoe UI Semibold", 11f);
            using var smallFont = new Font("Segoe UI", 8f);
            using var smallBold = new Font("Segoe UI Semibold", 8f);
            var priority = _router.Schedule.Priority.ToString().ToUpperInvariant();
            var prioSize = g.MeasureString(priority, smallBold);
            DrawText(g, _router.Name, titleFont, TextMain, new RectangleF(left, y, right - left - prioSize.Width - 8, 22));
            using (var prioBrush = new SolidBrush(_router.Schedule.Priority >= RouterPriority.High ? Color.FromArgb(150, 60, 160) : TextDim))
                g.DrawString(priority, smallBold, prioBrush, right - prioSize.Width, y + 4);
            y += 26;

            // Status pill
            var (pillText, pillColor) = state switch
            {
                WidgetState.NotChecked => ("Waiting for first check", NotCheckedColor),
                WidgetState.Online => ("Online", OnlineColor),
                WidgetState.Alert => ($"{_alertSeverity?.ToString().ToUpperInvariant() ?? "ALERT"} alert", Darken(AlertColor, 0.25f)),
                WidgetState.Disabled => ("Monitoring off", Darken(DisabledColor, 0.2f)),
                _ => ("Unreachable", OfflineColor)
            };
            var pillSize = g.MeasureString(pillText, smallBold);
            var pill = new Rectangle(left, y, (int)pillSize.Width + 24, 20);
            using (var pillPath = RoundedRect(pill, 10))
            using (var pillBg = new SolidBrush(Blend(Color.White, pillColor, 0.18f)))
                g.FillPath(pillBg, pillPath);
            using (var dot = new SolidBrush(pillColor))
                g.FillEllipse(dot, pill.X + 8, pill.Y + 6, 8, 8);
            using (var pillTextBrush = new SolidBrush(Darken(pillColor, 0.15f)))
                g.DrawString(pillText, smallBold, pillTextBrush, pill.X + 19, pill.Y + 3);
            y += 28;

            // Details
            if (state == WidgetState.Alert && _alertTitle != null)
            {
                DrawText(g, _alertTitle, Font, TextMain, new RectangleF(left, y, right - left, 34), wrap: true);
                y += 36;
                DrawText(g, $"Raised {Ago(_alertTime!.Value)} · right-click to clear", smallFont, TextDim, new RectangleF(left, y, right - left, 16));
                y += 18;
            }
            else
            {
                string line1 = state switch
                {
                    WidgetState.Disabled => !_router.IsEnabled ? "Router is disabled" : "Schedule is off",
                    WidgetState.NotChecked => "Not checked yet",
                    WidgetState.Offline => $"Last try {Ago(_lastCheck!.Value)} ({_lastCheck:HH:mm})",
                    _ => $"Checked {Ago(_lastCheck!.Value)} ({_lastCheck:HH:mm})"
                };
                DrawText(g, line1, Font, TextMain, new RectangleF(left, y, right - left, 18));
                y += 20;
                string line2 = state == WidgetState.Disabled
                    ? (!_router.IsEnabled ? "Enable it in Settings > Routers" : "Turn it on in the router's Schedule")
                    : state == WidgetState.NotChecked
                    ? "Runs when monitoring starts"
                    : state == WidgetState.Offline
                    ? (_error ?? "No answer from the router API")
                    : string.IsNullOrEmpty(_uptime) ? "Uptime: unknown" : $"Up {_uptime}";
                DrawText(g, line2, Font, state == WidgetState.Offline ? Darken(OfflineColor, 0.1f) : TextMain, new RectangleF(left, y, right - left, 18));
                y += 22;
            }

            // Footer: address and next check
            var next = state == WidgetState.Disabled ? "not monitored"
                : _router.Schedule.Enabled && _router.Schedule.NextScheduledRun > DateTime.Now
                ? $"next {Until(_router.Schedule.NextScheduledRun)}"
                : _router.Schedule.Enabled ? "next check due" : "schedule off";
            var footer = $"{_router.RouterIP}:{_router.Port}{(_router.UseSSL ? " · SSL" : "")} · {next}";
            DrawText(g, footer, smallFont, TextDim, new RectangleF(left, card.Bottom - 22, right - left, 16));
        }

        private void UpdateTooltip()
        {
            var text = $"{_router.Name} ({_router.RouterIP})";
            if (_alertTitle != null) text += $"\n{_alertSeverity}: {_alertTitle}\nRaised {_alertTime:yyyy-MM-dd HH:mm}";
            if (_error != null) text += $"\nLast error: {_error}";
            _tip.SetToolTip(this, text);
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF box, bool wrap = false)
        {
            using var brush = new SolidBrush(color);
            using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
            if (!wrap) fmt.FormatFlags |= StringFormatFlags.NoWrap;
            g.DrawString(text, font, brush, box, fmt);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

        private static Color Darken(Color c, float amount) => Blend(c, Color.Black, amount);

        private static string Ago(DateTime t)
        {
            var s = DateTime.Now - t;
            if (s.TotalSeconds < 60) return "just now";
            if (s.TotalMinutes < 60) return $"{(int)s.TotalMinutes} min ago";
            if (s.TotalHours < 24) return $"{(int)s.TotalHours} h ago";
            return $"{(int)s.TotalDays} d ago";
        }

        private static string Until(DateTime t)
        {
            var s = t - DateTime.Now;
            return s.TotalMinutes < 1 ? "in <1 min" : s.TotalHours < 1 ? $"in {(int)Math.Ceiling(s.TotalMinutes)} min" : $"at {t:HH:mm}";
        }

        /// <summary>RouterOS uptime "3w2d4h12m5s" -> "23d 4h 12m".</summary>
        private static string FormatUptime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || raw == "Unknown") return "";
            var m = System.Text.RegularExpressions.Regex.Matches(raw, @"(\d+)([wdhms])");
            if (m.Count == 0) return raw;
            long secs = 0;
            foreach (System.Text.RegularExpressions.Match x in m)
            {
                long v = long.Parse(x.Groups[1].Value);
                secs += x.Groups[2].Value switch { "w" => v * 604800, "d" => v * 86400, "h" => v * 3600, "m" => v * 60, _ => v };
            }
            var ts = TimeSpan.FromSeconds(secs);
            return ts.TotalDays >= 1 ? $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m" : ts.TotalHours >= 1 ? $"{ts.Hours}h {ts.Minutes}m" : $"{ts.Minutes}m";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refresh.Dispose();
                _tip.Dispose();
                ContextMenuStrip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
