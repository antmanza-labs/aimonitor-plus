using SimpleMikrotikMonitor.Analysis;

namespace SimpleMikrotikMonitor
{
    /// <summary>
    /// Check history. Top: one row per router check. Bottom: the selected check's AI summary and every
    /// finding, including ones that were not sent as alerts (and why).
    /// </summary>
    public partial class HistoryForm : Form
    {
        private const int MaxRows = 3000;

        private readonly AppSettings _settings;
        private readonly HistoryStore _store;
        private readonly ComboBox _router = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly CheckBox _onlyFindings = new() { Text = "Only checks with findings", AutoSize = true, Checked = true };
        private readonly Label _counts = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly DataGridView _grid = new();
        private readonly RichTextBox _details = new();
        private readonly SplitContainer _split = new();

        public HistoryForm(AppSettings settings, HistoryStore? store = null)
        {
            _settings = settings;
            _store = store ?? new HistoryStore();
            BuildUi();
            ThemeManager.ApplyTheme(this, _settings.UISettings.Theme);
            Shown += (_, _) => { _split.SplitterDistance = (int)(_split.Height * 0.5); LoadHistory(); };
        }

        private void BuildUi()
        {
            Text = "AIMonitor+ History";
            Size = new Size(1100, 760);
            MinimumSize = new Size(760, 500);
            StartPosition = FormStartPosition.CenterParent;
            Icon = Owner?.Icon ?? Icon;
            Font = new Font("Segoe UI", 9f);

            // Filters
            _router.Items.Add("All routers");
            foreach (var r in _settings.MikrotikSettings.Routers) _router.Items.Add(r.Name);
            _router.SelectedIndex = 0;
            _period.Items.AddRange(new object[] { "Last 24 hours", "Last 7 days", "Last 30 days" });
            _period.SelectedIndex = 1;
            _router.SelectedIndexChanged += (_, _) => LoadHistory();
            _period.SelectedIndexChanged += (_, _) => LoadHistory();
            _onlyFindings.CheckedChanged += (_, _) => LoadHistory();

            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
            filters.Controls.Add(new Label { Text = "Router:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            filters.Controls.Add(_router);
            filters.Controls.Add(new Label { Text = "Period:", AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
            filters.Controls.Add(_period);
            _onlyFindings.Margin = new Padding(14, 4, 0, 0);
            filters.Controls.Add(_onlyFindings);
            _counts.Margin = new Padding(16, 6, 0, 0);
            filters.Controls.Add(_counts);

            // Grid of checks
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.BorderStyle = BorderStyle.None;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 225, 250);
            _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            AddColumn("Time", 95);
            AddColumn("Router", 90);
            AddColumn("Result", 55);
            AddColumn("New lines", 55);
            AddColumn("Findings", 110);
            AddColumn("Alerts sent", 55);
            AddColumn("Summary", 330);
            _grid.SelectionChanged += (_, _) => ShowDetails(_grid.CurrentRow?.Tag as CheckRecord);

            // Details of the selected check
            _details.Dock = DockStyle.Fill;
            _details.ReadOnly = true;
            _details.BorderStyle = BorderStyle.None;
            _details.BackColor = Color.FromArgb(250, 250, 251);
            _details.Font = new Font("Segoe UI", 9.5f);
            _details.DetectUrls = false;

            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.Panel1.Controls.Add(_grid);
            _split.Panel2.Controls.Add(_details);
            _split.Panel2.Padding = new Padding(10, 6, 10, 6);

            // Buttons
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var clear = new Button { Text = "Clear history...", AutoSize = true };
            var copy = new Button { Text = "Copy details", AutoSize = true };
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            clear.Click += (_, _) => ClearHistory();
            copy.Click += (_, _) => { if (_details.TextLength > 0) Clipboard.SetText(_details.Text); };
            refresh.Click += (_, _) => LoadHistory();
            buttons.Controls.AddRange(new Control[] { close, clear, copy, refresh });
            CancelButton = close;

            Controls.Add(_split);
            Controls.Add(buttons);
            Controls.Add(filters);
        }

        private void AddColumn(string header, int weight) =>
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

        private void LoadHistory()
        {
            var since = _period.SelectedIndex switch { 0 => DateTime.Now.AddHours(-24), 1 => DateTime.Now.AddDays(-7), _ => DateTime.Now.AddDays(-30) };
            var router = _router.SelectedIndex <= 0 ? null : _router.SelectedItem!.ToString();

            List<CheckRecord> all;
            try
            {
                all = _store.Load(since, router);
            }
            catch (Exception ex)
            {
                _counts.Text = $"Could not read history: {ex.Message}";
                return;
            }

            var shown = _onlyFindings.Checked ? all.Where(r => r.HasFindings || !r.Success).ToList() : all;
            _counts.Text = $"{all.Count} checks · {all.Count(r => r.HasFindings)} with findings · {all.Sum(r => r.AlertsSent)} alerts sent" +
                           (shown.Count > MaxRows ? $" · showing newest {MaxRows}" : "");

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var r in shown.Take(MaxRows))
            {
                var findings = r.Findings.Where(f => f.Severity > Severity.Info || f.IsRecovery).ToList();
                var findingText = findings.Count == 0 ? "" :
                    r.TopSeverity is { } top ? $"{top} ({findings.Count})" : $"{findings.Count}";
                var summary = !string.IsNullOrWhiteSpace(r.EngineError) ? "AI engine problem: " + r.EngineError
                            : !string.IsNullOrWhiteSpace(r.Summary) ? r.Summary
                            : findings.FirstOrDefault()?.Title ?? r.Message;

                int i = _grid.Rows.Add(
                    r.Time.ToString("ddd dd MMM HH:mm"),
                    r.Router,
                    r.Success ? "OK" : "Failed",
                    r.NewLines > 0 ? r.NewLines.ToString() : "",
                    findingText,
                    r.AlertsSent > 0 ? r.AlertsSent.ToString() : "",
                    summary);
                var row = _grid.Rows[i];
                row.Tag = r;
                row.DefaultCellStyle.BackColor = RowColor(r);
            }
            _grid.ResumeLayout();

            if (_grid.Rows.Count > 0)
            {
                _grid.ClearSelection();
                _grid.Rows[0].Selected = true;
                _grid.CurrentCell = _grid.Rows[0].Cells[0];
            }
            else
            {
                ShowDetails(null);
            }
        }

        private static Color RowColor(CheckRecord r) => r.TopSeverity switch
        {
            Severity.Critical => Color.FromArgb(253, 226, 226),
            Severity.High => Color.FromArgb(255, 238, 210),
            Severity.Medium => Color.FromArgb(255, 250, 215),
            _ => r.Success ? Color.White : Color.FromArgb(242, 242, 244)
        };

        private static Color SeverityColor(Severity s) => s switch
        {
            Severity.Critical => Color.FromArgb(200, 40, 40),
            Severity.High => Color.FromArgb(214, 110, 0),
            Severity.Medium => Color.FromArgb(170, 130, 0),
            Severity.Low => Color.FromArgb(40, 110, 190),
            _ => Color.Gray
        };

        private void ShowDetails(CheckRecord? r)
        {
            _details.Clear();
            if (r == null)
            {
                Write("Select a check above to see its findings.", Color.DimGray);
                return;
            }

            Write($"{r.Router}", Color.Black, bold: true, size: 12f);
            Write($"   {r.Time:dddd dd MMM yyyy, HH:mm:ss}\n", Color.DimGray);
            Write($"{(r.Success ? "Check OK" : "Check failed")} · {r.Message}\n", r.Success ? Color.DimGray : Color.Firebrick);
            if (r.NewLines > 0 || r.LinesReceived > 0)
                Write($"{r.NewLines} new log lines · {r.Filtered} routine filtered out · {r.AlreadyKnown} already known · {r.SentToAi} sent to AI\n", Color.DimGray);
            if (r.Engine != null) Write($"AI engine: {r.Engine}" + (r.Usage != null ? $" · {r.Usage}" : "") + "\n", Color.DimGray);
            if (!string.IsNullOrWhiteSpace(r.EngineError)) Write($"AI engine problem: {r.EngineError}\n", Color.Firebrick);

            if (!string.IsNullOrWhiteSpace(r.Summary))
            {
                Write("\nAI summary\n", Color.Black, bold: true);
                Write(r.Summary + "\n", Color.Black);
            }

            var findings = r.Findings.OrderByDescending(f => f.IsRecovery ? Severity.Info : f.Severity).ToList();
            if (findings.Count == 0)
            {
                Write("\nNo findings in this check.\n", Color.DimGray);
                return;
            }

            Write($"\nFindings ({findings.Count})\n", Color.Black, bold: true);
            foreach (var f in findings)
            {
                Write("\n");
                Write(f.IsRecovery ? " RECOVERED " : $" {f.Severity.ToString().ToUpperInvariant()} ", Color.White, bold: true,
                      back: f.IsRecovery ? Color.FromArgb(34, 154, 84) : SeverityColor(f.Severity));
                Write("  " + f.Title + "\n", Color.Black, bold: true);
                if (!string.IsNullOrWhiteSpace(f.AlertStatus))
                    Write($"Alert: {f.AlertStatus}\n", f.AlertSent ? Color.FromArgb(34, 130, 70) : Color.DimGray, italic: true);
                if (!string.IsNullOrWhiteSpace(f.Impact)) { Write("Impact: ", Color.DimGray); Write(f.Impact + "\n", Color.Black); }
                if (!string.IsNullOrWhiteSpace(f.LikelyCause)) { Write("Likely cause: ", Color.DimGray); Write(f.LikelyCause + "\n", Color.Black); }
                if (!string.IsNullOrWhiteSpace(f.SuggestedCheck)) { Write("Check: ", Color.DimGray); Write(f.SuggestedCheck + "\n", Color.FromArgb(30, 60, 120), mono: true); }
                if (f.Occurrences > 1)
                    Write($"Seen {f.Occurrences}× between {f.FirstSeen:HH:mm} and {f.LastSeen:HH:mm}\n", Color.DimGray);
                if (f.FromRules) Write("Raised by the built-in rules (not the AI)\n", Color.DimGray, italic: true);
            }
            _details.SelectionStart = 0;
            _details.ScrollToCaret();
        }

        private void Write(string text, Color? color = null, bool bold = false, bool italic = false, bool mono = false, float? size = null, Color? back = null)
        {
            _details.SelectionStart = _details.TextLength;
            _details.SelectionLength = 0;
            var style = (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);
            _details.SelectionFont = new Font(mono ? "Consolas" : "Segoe UI", size ?? (mono ? 9f : 9.5f), style);
            _details.SelectionColor = color ?? Color.Black;
            _details.SelectionBackColor = back ?? _details.BackColor;
            _details.AppendText(text);
        }

        private void ClearHistory()
        {
            var router = _router.SelectedIndex <= 0 ? null : _router.SelectedItem!.ToString();
            var what = router == null ? "ALL history for every router" : $"all history for {router}";
            if (MessageBox.Show(this, $"Permanently delete {what}? This can't be undone.", "Clear history",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _store.Clear(router);
            LoadHistory();
        }
    }
}
