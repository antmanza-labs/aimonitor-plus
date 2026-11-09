using SimpleMikrotikMonitor.Engines;
using SimpleMikrotikMonitor.Services;

namespace SimpleMikrotikMonitor.Controls
{
    /// <summary>Settings > AI Engine: pick the engine (PC / API / Ollama / others) and how alerts are filtered.</summary>
    public sealed class AiEngineSettingsPanel : UserControl
    {
        private readonly AppSettings _settings;
        private readonly ComboBox _engine = DropDown();
        private readonly Dictionary<LLMProvider, TableLayoutPanel> _groups = new();
        private readonly Label _engineInfo = new() { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 0, 3, 8) };
        private readonly Button _test = new() { Text = "Test engine", AutoSize = true };
        private readonly Label _testResult = new() { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(8, 8, 3, 3) };

        // Claude Code
        private readonly TextBox _ccPath = Text();
        private readonly ComboBox _ccModel = Editable("haiku", "sonnet", "opus");
        private readonly CheckBox _ccEscalate = new() { Text = "Second look at High/Critical findings with:", AutoSize = true };
        private readonly ComboBox _ccEscModel = Editable("sonnet", "opus");
        private readonly NumericUpDown _ccTimeout = Number(30, 900);
        private readonly ComboBox _ccReview = Editable("sonnet", "opus", "haiku");

        // Claude API
        private readonly TextBox _apiKey = Secret();
        private readonly ComboBox _apiModel = Editable("claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5");
        private readonly CheckBox _apiEscalate = new() { Text = "Second look at High/Critical findings with:", AutoSize = true };
        private readonly ComboBox _apiEscModel = Editable("claude-sonnet-5-5", "claude-opus-5-5");
        private readonly ComboBox _apiReview = Editable("claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5");

        // Ollama
        private readonly TextBox _olUrl = Text();
        private readonly TextBox _olModel = Text();
        private readonly CheckBox _olUnload = new() { Text = "Unload the model from the GPU after each analysis (recommended)", AutoSize = true };
        private readonly NumericUpDown _olCtx = Number(4096, 131072);

        // OpenAI / Google / Custom
        private readonly TextBox _oaKey = Secret(), _oaModel = Text();
        private readonly TextBox _ggKey = Secret(), _ggModel = Text();
        private readonly TextBox _cuUrl = Text(), _cuKey = Secret(), _cuModel = Text();

        // Alerting
        private readonly ComboBox _minSeverity = DropDown();
        private readonly NumericUpDown _cooldown = Number(1, 168);
        private readonly NumericUpDown _benignDays = Number(1, 90);
        private readonly NumericUpDown _massThreshold = Number(2, 10000);
        private readonly NumericUpDown _maxEvents = Number(10, 500);
        private readonly NumericUpDown _unreachable = Number(0, 20);
        private readonly CheckBox _noise = new() { Text = "Built-in MikroTik noise filter (DHCP leases, own API logins, subscriber churn, debug)", AutoSize = true };
        private readonly CheckBox _mask = new() { Text = "Mask IPs, MACs and usernames before sending to a cloud engine", AutoSize = true };

        public AiEngineSettingsPanel(AppSettings settings)
        {
            _settings = settings;
            Dock = DockStyle.Fill;
            AutoScroll = true;

            var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(10) };
            Controls.Add(root);

            root.Controls.Add(Header("AI engine"));
            foreach (var p in new[] { LLMProvider.ClaudeCode, LLMProvider.Anthropic, LLMProvider.Ollama, LLMProvider.OpenAI, LLMProvider.Google, LLMProvider.Custom })
                _engine.Items.Add(new EngineItem(p));
            _engine.Width = 420;
            root.Controls.Add(_engine);
            root.Controls.Add(_engineInfo);

            _groups[LLMProvider.ClaudeCode] = Group(
                ("claude.exe path", Row(_ccPath, Btn("Detect", DetectClaude), Btn("Browse...", BrowseClaude))),
                ("Model", _ccModel),
                ("", Row(_ccEscalate, _ccEscModel)),
                ("Config review model", _ccReview),
                ("Timeout (seconds)", _ccTimeout));
            _groups[LLMProvider.Anthropic] = Group(
                ("API key", _apiKey),
                ("Triage model", _apiModel),
                ("", Row(_apiEscalate, _apiEscModel)),
                ("Config review model", _apiReview));
            _groups[LLMProvider.Ollama] = Group(
                ("Ollama URL", _olUrl),
                ("Model", _olModel),
                ("", _olUnload),
                ("Context size (tokens)", _olCtx));
            _groups[LLMProvider.OpenAI] = Group(("API key", _oaKey), ("Model", _oaModel));
            _groups[LLMProvider.Google] = Group(("API key", _ggKey), ("Model", _ggModel));
            _groups[LLMProvider.Custom] = Group(("Base URL (…/v1)", _cuUrl), ("API key", _cuKey), ("Model", _cuModel));
            foreach (var g in _groups.Values) root.Controls.Add(g);

            root.Controls.Add(Row(_test, _testResult));
            _test.Click += async (_, _) => await TestAsync();

            root.Controls.Add(Header("Alerting"));
            foreach (Severity sv in Enum.GetValues(typeof(Severity))) _minSeverity.Items.Add(sv);
            root.Controls.Add(Group(
                ("Notify from severity", _minSeverity),
                ("Don't repeat an alert for (hours)", _cooldown),
                ("Remember harmless events for (days)", _benignDays),
                ("Report subscriber churn from (lines per check)", _massThreshold),
                ("Max distinct events per AI call", _maxEvents),
                ("Alert when unreachable for (checks, 0 = off)", _unreachable),
                ("", _noise),
                ("", _mask)));

            _engine.SelectedIndexChanged += (_, _) => ShowSelectedGroup();
            LoadFrom(settings);
        }

        private LLMProvider Selected => ((EngineItem)_engine.SelectedItem!).Provider;

        private void LoadFrom(AppSettings s)
        {
            _engine.SelectedItem = _engine.Items.Cast<EngineItem>().FirstOrDefault(i => i.Provider == s.LLMSettings.Provider) ?? _engine.Items[0];

            _ccPath.Text = s.ClaudeCodeSettings.ExecutablePath;
            _ccPath.PlaceholderText = "empty = auto-detect";
            _ccModel.Text = s.ClaudeCodeSettings.Model;
            _ccEscalate.Checked = s.ClaudeCodeSettings.EscalateHighSeverity;
            _ccEscModel.Text = s.ClaudeCodeSettings.EscalationModel;
            _ccTimeout.Value = Clamp(_ccTimeout, s.ClaudeCodeSettings.TimeoutSeconds);
            _ccReview.Text = s.ClaudeCodeSettings.ReviewModel;

            _apiKey.Text = s.AnthropicSettings.ApiKey;
            _apiKey.PlaceholderText = "sk-ant-...";
            _apiModel.Text = s.AnthropicSettings.Model;
            _apiEscalate.Checked = s.AnthropicSettings.EscalateHighSeverity;
            _apiEscModel.Text = s.AnthropicSettings.EscalationModel;
            _apiReview.Text = s.AnthropicSettings.ReviewModel;

            _olUrl.Text = s.OllamaSettings.Url;
            _olModel.Text = s.OllamaSettings.Model;
            _olUnload.Checked = s.OllamaSettings.UnloadAfterRun;
            _olCtx.Value = Clamp(_olCtx, s.OllamaSettings.ContextSize);

            _oaKey.Text = s.OpenAISettings.ApiKey; _oaModel.Text = s.OpenAISettings.Model;
            _ggKey.Text = s.GoogleSettings.ApiKey; _ggModel.Text = s.GoogleSettings.Model;
            _cuUrl.Text = s.LLMSettings.EndpointUrl; _cuKey.Text = s.LLMSettings.ApiKey; _cuModel.Text = s.LLMSettings.Model;

            var a = s.AnalysisSettings;
            _minSeverity.SelectedItem = a.NotifyMinSeverity;
            _cooldown.Value = Clamp(_cooldown, a.RepeatCooldownHours);
            _benignDays.Value = Clamp(_benignDays, a.BenignMemoryDays);
            _massThreshold.Value = Clamp(_massThreshold, a.MassEventThreshold);
            _maxEvents.Value = Clamp(_maxEvents, a.MaxEventsPerCall);
            _unreachable.Value = Clamp(_unreachable, a.UnreachableAfterFailures);
            _noise.Checked = a.UseBuiltInNoiseFilter;
            _mask.Checked = a.MaskSensitiveData;

            ShowSelectedGroup();
        }

        /// <summary>Copies the panel's values into <paramref name="s"/>.</summary>
        public void ApplyTo(AppSettings s)
        {
            s.LLMSettings.Provider = Selected;

            s.ClaudeCodeSettings.ExecutablePath = _ccPath.Text.Trim();
            s.ClaudeCodeSettings.Model = _ccModel.Text.Trim();
            s.ClaudeCodeSettings.EscalateHighSeverity = _ccEscalate.Checked;
            s.ClaudeCodeSettings.EscalationModel = _ccEscModel.Text.Trim();
            s.ClaudeCodeSettings.TimeoutSeconds = (int)_ccTimeout.Value;
            s.ClaudeCodeSettings.ReviewModel = _ccReview.Text.Trim();

            s.AnthropicSettings.ApiKey = _apiKey.Text.Trim();
            s.AnthropicSettings.Model = _apiModel.Text.Trim();
            s.AnthropicSettings.EscalateHighSeverity = _apiEscalate.Checked;
            s.AnthropicSettings.EscalationModel = _apiEscModel.Text.Trim();
            s.AnthropicSettings.ReviewModel = _apiReview.Text.Trim();

            s.OllamaSettings.Url = _olUrl.Text.Trim();
            s.OllamaSettings.Model = _olModel.Text.Trim();
            s.OllamaSettings.UnloadAfterRun = _olUnload.Checked;
            s.OllamaSettings.ContextSize = (int)_olCtx.Value;

            s.OpenAISettings.ApiKey = _oaKey.Text.Trim(); s.OpenAISettings.Model = _oaModel.Text.Trim();
            s.GoogleSettings.ApiKey = _ggKey.Text.Trim(); s.GoogleSettings.Model = _ggModel.Text.Trim();
            s.LLMSettings.EndpointUrl = _cuUrl.Text.Trim(); s.LLMSettings.ApiKey = _cuKey.Text.Trim(); s.LLMSettings.Model = _cuModel.Text.Trim();

            var a = s.AnalysisSettings;
            a.NotifyMinSeverity = (Severity)(_minSeverity.SelectedItem ?? Severity.High);
            a.RepeatCooldownHours = (int)_cooldown.Value;
            a.BenignMemoryDays = (int)_benignDays.Value;
            a.MassEventThreshold = (int)_massThreshold.Value;
            a.MaxEventsPerCall = (int)_maxEvents.Value;
            a.UnreachableAfterFailures = (int)_unreachable.Value;
            a.UseBuiltInNoiseFilter = _noise.Checked;
            a.MaskSensitiveData = _mask.Checked;
        }

        private void ShowSelectedGroup()
        {
            foreach (var (p, g) in _groups) g.Visible = p == Selected;
            _engineInfo.Text = Selected switch
            {
                LLMProvider.ClaudeCode => "Runs the Claude Code CLI on this PC with your Claude subscription. No API key, no GPU load. " +
                                          "Every check uses part of your plan's usage limits. Sign in once: run 'claude auth login' in a terminal.",
                LLMProvider.Anthropic => "Calls the Claude API directly. Billed per token from Anthropic Console credits (separate from a Claude subscription). " +
                                         "Haiku sorts each batch cheaply; the optional second look uses Sonnet only for High/Critical findings.",
                LLMProvider.Ollama => "Runs a model on this PC through Ollama. Nothing leaves the PC, but it uses the GPU while analysing. " +
                                      "Small models (2-4B) are weak at this task; 7B+ gives far better triage.",
                LLMProvider.Google => "Uses Gemini through Google's OpenAI-compatible endpoint.",
                LLMProvider.Custom => "Any OpenAI-compatible endpoint (LM Studio, OpenRouter, vLLM...). Enter the base URL ending in /v1.",
                _ => "Calls the OpenAI API."
            };
            _testResult.Text = "";
        }

        private async Task TestAsync()
        {
            _test.Enabled = false;
            _testResult.ForeColor = SystemColors.ControlText;
            _testResult.Text = "Testing...";
            try
            {
                var copy = SettingsStore.Deserialize(System.Text.Json.JsonSerializer.Serialize(_settings, SettingsStore.JsonOptions))!;
                ApplyTo(copy);
                var engine = EngineFactory.Create(copy);
                _testResult.Text = await engine.TestAsync();
                _testResult.ForeColor = Color.DarkGreen;
            }
            catch (EngineException ex)
            {
                _testResult.Text = ex.Message;
                _testResult.ForeColor = Color.Firebrick;
            }
            catch (Exception ex)
            {
                _testResult.Text = $"Test failed: {ex.Message}";
                _testResult.ForeColor = Color.Firebrick;
            }
            finally
            {
                _test.Enabled = true;
            }
        }

        private void DetectClaude()
        {
            var found = ClaudeCodeEngine.FindExecutable();
            if (found != null) _ccPath.Text = found;
            else MessageBox.Show("Claude Code was not found on PATH or in the usual install folders. Install it, or browse to claude.exe.",
                "Not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BrowseClaude()
        {
            using var dlg = new OpenFileDialog { Filter = "Claude Code|claude.exe;claude.cmd|All files|*.*", Title = "Find claude.exe" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _ccPath.Text = dlg.FileName;
        }

        // ---------- small layout helpers ----------

        private sealed record EngineItem(LLMProvider Provider)
        {
            public override string ToString() => EngineFactory.DisplayName(Provider);
        }

        private static Label Header(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold),
            Margin = new Padding(3, 12, 3, 6)
        };

        private static TableLayoutPanel Group(params (string Label, Control Control)[] rows)
        {
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 4) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var (label, control) in rows)
            {
                t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
                t.Controls.Add(control);
            }
            return t;
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            f.Controls.AddRange(controls);
            return f;
        }

        private static Button Btn(string text, Action onClick)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += (_, _) => onClick();
            return b;
        }

        private static TextBox Text() => new() { Width = 330 };
        private static TextBox Secret() => new() { Width = 330, UseSystemPasswordChar = true };
        private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private static ComboBox Editable(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200 };
            c.Items.AddRange(items);
            return c;
        }
        private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 90 };
        private static decimal Clamp(NumericUpDown n, int v) => Math.Min(n.Maximum, Math.Max(n.Minimum, v));
    }
}
