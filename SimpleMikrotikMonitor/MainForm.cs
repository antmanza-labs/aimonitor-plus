using System.Text.Json;
using Serilog;
using tik4net;
using SimpleMikrotikMonitor.Analysis;
using SimpleMikrotikMonitor.Services;

namespace SimpleMikrotikMonitor
{
    public partial class MainForm : Form
    {
        private readonly string _configPath;
        private AppSettings _settings;
        private ILogger _logger;
        private bool _isMonitoring = false;
        private System.Windows.Forms.Timer _monitorTimer;
        private AlertMemory _memory;
        private LogAnalyzer _analyzer;
        private string? _lastConnectError;

        // UI Controls
        private Button _startButton;
        private Button _settingsButton;
        private Button _historyButton;
        private RichTextBox _logTextBox;
        private Label _statusLabel;
        private MenuStrip _mainMenu;
        private ToolStripMenuItem _helpMenu;
        private ToolStripMenuItem _aboutMenuItem;
        private FlowLayoutPanel _dashboardPanel;
        private Dictionary<string, Controls.DashboardWidget> _routerWidgets;

        // Service managers
        private Services.ScheduleManager _scheduleManager;
        private Services.HistoryManager _historyManager;

        public MainForm()
        {
            try
            {
                // Settings live in %APPDATA%\AIMonitorPlus, secrets encrypted with DPAPI
                _configPath = AppPaths.SettingsFile;

                // Load settings
                _settings = LoadConfiguration();

                // Setup logging
                string logDir = Path.GetDirectoryName(_settings.LogSettings.FilePath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                _logger = new LoggerConfiguration()
                    .WriteTo.File(_settings.LogSettings.FilePath, rollingInterval: RollingInterval.Day)
                    .CreateLogger();

                // Alert memory survives restarts, so the same issue isn't re-analysed or re-alerted
                _memory = new AlertMemory();
                _analyzer = new LogAnalyzer(() => _settings, _memory, m => AppendLog(m));

                // Initialize UI
                InitializeUI();

                if (SettingsStore.MigrationNotice != null)
                    AppendLog(SettingsStore.MigrationNotice);
                AppendLog($"AI engine: {Engines.EngineFactory.DisplayName(_settings.LLMSettings.Provider)}. Data folder: {AppPaths.DataDir}");

                // Always apply light theme
                _settings.UISettings.Theme = ThemeMode.Light;
                ThemeManager.ApplyTheme(this, ThemeMode.Light);

                // Initialize timer
                _monitorTimer = new System.Windows.Forms.Timer
                {
                    Interval = 30000 // Check every 30 seconds for scheduled routers
                    // This doesn't mean routers will be checked every 30 seconds
                    // It just means we'll check if any routers are due to be checked every 30 seconds
                };
                _monitorTimer.Tick += MonitorTimer_Tick;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing application: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeUI()
        {
            // Form properties
            Text = "AIMonitor+";
            Size = new Size(1000, 800);  // Increased size to accommodate dashboard
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;  // Allow resizing
            MaximizeBox = true;
            MinimizeBox = true;

            MinimumSize = new Size(700, 500);

            // aimonitor.ico is the v2 icon. A new file name than v1's logo.ico, so Windows' icon cache can't serve the old image.
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aimonitor.ico");
            if (!File.Exists(logoPath)) logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
            if (File.Exists(logoPath))
            {
                try
                {
                    Icon = new Icon(logoPath);
                }
                catch
                {
                    Icon = SystemIcons.Application;
                }
            }
            else
            {
                Icon = SystemIcons.Application;
            }

            // Create menu
            _mainMenu = new MenuStrip();

            // File menu
            var fileMenu = new ToolStripMenuItem("File");
            var backupMenuItem = new ToolStripMenuItem("Backup/Restore Configuration");
            backupMenuItem.Click += BackupRestoreMenuItem_Click;
            fileMenu.DropDownItems.Add(backupMenuItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            var exitMenuItem = new ToolStripMenuItem("Exit");
            exitMenuItem.Click += (s, e) => Close();
            fileMenu.DropDownItems.Add(exitMenuItem);
            _mainMenu.Items.Add(fileMenu);

            // Tools menu
            var toolsMenu = new ToolStripMenuItem("Tools");
            var configAnalyzerMenuItem = new ToolStripMenuItem("Router Config Analyzer");
            configAnalyzerMenuItem.Click += ConfigAnalyzerMenuItem_Click;
            toolsMenu.DropDownItems.Add(configAnalyzerMenuItem);
            _mainMenu.Items.Add(toolsMenu);

            // Help menu
            _helpMenu = new ToolStripMenuItem("Help");
            _aboutMenuItem = new ToolStripMenuItem("About");
            _aboutMenuItem.Click += AboutMenuItem_Click;
            _helpMenu.DropDownItems.Add(_aboutMenuItem);
            _mainMenu.Items.Add(_helpMenu);
            Controls.Add(_mainMenu);
            MainMenuStrip = _mainMenu;

            // Create controls
            _startButton = new Button
            {
                Text = "Start Monitoring",
                Location = new Point(10, 40), // Adjusted for menu
                Size = new Size(120, 30)
            };
            _startButton.Click += StartButton_Click;

            _settingsButton = new Button
            {
                Text = "Settings",
                Location = new Point(140, 40), // Adjusted for menu
                Size = new Size(80, 30)
            };
            _settingsButton.Click += SettingsButton_Click;

            _historyButton = new Button
            {
                Text = "History",
                Location = new Point(230, 40), // Adjusted for menu
                Size = new Size(80, 30)
            };
            _historyButton.Click += HistoryButton_Click;

            _statusLabel = new Label
            {
                Text = "Status: Ready",
                Location = new Point(320, 45), // Adjusted for menu
                Size = new Size(200, 20),
                AutoSize = true
            };

            _logTextBox = new RichTextBox
            {
                Location = new Point(10, 290),
                Size = new Size(960, 460),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                Font = new Font("Consolas", 9F),
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.WindowText
            };

            // Create dashboard panel
            _dashboardPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 80),
                Size = new Size(960, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                BorderStyle = BorderStyle.None,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(5),
                Margin = new Padding(1)
            };

            // Initialize router widgets dictionary
            _routerWidgets = new Dictionary<string, Controls.DashboardWidget>();

            // Create widgets for each router
            foreach (var router in _settings.MikrotikSettings.Routers)
            {
                AddRouterWidget(router);
            }

            // Add controls to form
            Controls.AddRange(new Control[] { _startButton, _settingsButton, _historyButton, _statusLabel, _dashboardPanel, _logTextBox });

            // Fit cards and log to the window, including maximised / full screen
            Resize += (_, _) => LayoutMain();
            Shown += (_, _) => LayoutMain();

            // Initialize service managers
            _scheduleManager = new Services.ScheduleManager(_settings);
            _historyManager = new Services.HistoryManager(_settings);
        }

        private AppSettings LoadConfiguration()
        {
            try
            {
                return SettingsStore.Load();
            }
            catch (Exception ex)
            {
                // Never delete the user's settings on a read error; run with defaults and say so.
                MessageBox.Show($"Error loading settings from {AppPaths.SettingsFile}:\n{ex.Message}\n\nStarting with default settings. The file was left untouched.",
                    "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return SettingsStore.CreateDefaults();
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                SettingsStore.Save(_settings);
                _memory?.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (!_isMonitoring)
            {
                StartMonitoring();
                _startButton.Text = "Stop Monitoring";
            }
            else
            {
                StopMonitoring();
                _startButton.Text = "Start Monitoring";
            }
        }

        private void SettingsButton_Click(object sender, EventArgs e)
        {
            var settingsForm = new SettingsForm(_settings);
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                _settings = settingsForm.Settings;
                SaveConfiguration();
                RefreshDashboard();  // Refresh dashboard after settings change

                // Reinitialize the schedule manager with the updated settings
                _scheduleManager = new Services.ScheduleManager(_settings);

                // If monitoring is active, restart it
                if (_isMonitoring)
                {
                    StopMonitoring();
                    StartMonitoring();
                }
            }
        }

        private void HistoryButton_Click(object sender, EventArgs e)
        {
            using var historyForm = new HistoryForm(_settings);
            historyForm.ShowDialog();
        }

        private void AboutMenuItem_Click(object sender, EventArgs e)
        {
            using var aboutForm = new AboutForm();
            aboutForm.ShowDialog();
        }

        private void ConfigAnalyzerMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using var analyzerForm = new RouterConfigAnalyzerForm(_settings, _logger);

                // Apply current theme to the form
                ThemeManager.ApplyTheme(analyzerForm, _settings.UISettings.Theme);

                analyzerForm.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Router Config Analyzer: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _logger.Error(ex, "Error opening Router Config Analyzer");
            }
        }

        private void ChangeTheme(ThemeMode theme)
        {
            // Always use light theme
            theme = ThemeMode.Light;

            // Save theme setting
            _settings.UISettings.Theme = theme;
            SaveConfiguration();

            // Apply theme to main form
            ThemeManager.ApplyTheme(this, theme);

            // Refresh dashboard widgets
            RefreshDashboard();

            // Apply theme to all open forms
            foreach (Form form in Application.OpenForms)
            {
                if (form != this)
                {
                    ThemeManager.ApplyTheme(form, theme);
                }
            }

            AppendLog("Theme set to light mode.");
        }

        private void StartMonitoring()
        {
            try
            {
                // Check if we have any enabled routers
                if (!_settings.MikrotikSettings.Routers.Any(r => r.IsEnabled))
                {
                    MessageBox.Show("No enabled routers found. Please configure at least one router in Settings.",
                        "No Routers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Reinitialize the schedule manager to ensure fresh schedule times
                _scheduleManager = new Services.ScheduleManager(_settings);

                _monitorTimer.Start();
                _isMonitoring = true;
                _statusLabel.Text = "Status: Monitoring";
                AppendLog("Monitoring started.");

                // Display router schedules
                DisplayRouterSchedules();

                // Trigger immediate check
                MonitorTimer_Tick(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting monitoring: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _logger.Error(ex, "Error starting monitoring");
            }
        }

        private void StopMonitoring()
        {
            try
            {
                _monitorTimer.Stop();
                _isMonitoring = false;
                _statusLabel.Text = "Status: Stopped";
                AppendLog("Monitoring stopped.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping monitoring: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _logger.Error(ex, "Error stopping monitoring");
            }
        }

        private async void MonitorTimer_Tick(object sender, EventArgs e)
        {
            _monitorTimer.Stop();

            try
            {
                AppendLog("Checking routers based on schedule...");

                // Log the current time for debugging
                AppendLog($"Current time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // Get routers to check based on schedule
                var routersToCheck = _scheduleManager.GetRoutersToCheck();

                if (routersToCheck.Count == 0)
                {
                    AppendLog("No routers scheduled for checking at this time.");

                    // Log next scheduled times for all routers for debugging
                    foreach (var router in _settings.MikrotikSettings.Routers.Where(r => r.IsEnabled && r.Schedule.Enabled))
                    {
                        AppendLog($"Router {router.Name} next scheduled check: {router.Schedule.NextScheduledRun:yyyy-MM-dd HH:mm:ss} (Interval: {router.Schedule.IntervalMinutes} min)");
                    }
                }
                else
                {
                    AppendLog($"Scheduled check for {routersToCheck.Count} routers.");
                }

                foreach (var router in routersToCheck)
                {
                    AppendLog($"Checking router: {router.Name} (Priority: {router.Schedule.Priority})");
                    bool success = false;
                    string message = "";
                    int logCount = 0;
                    bool hasAlerts = false;

                    try
                    {
                        // Skip if router IP is empty (test mode)
                        if (string.IsNullOrEmpty(router.RouterIP))
                        {
                            message = "Router IP is empty. Skipping (test mode).";
                            AppendLog(message);
                            _historyManager.AddHistoryEntry(router, false, message);
                            continue;
                        }

                        _lastConnectError = null;
                        using var connection = await ConnectToRouter(router);
                        if (connection == null)
                        {
                            message = $"Failed to connect to router: {router.Name}";
                            AppendLog(message);
                            _historyManager.AddHistoryEntry(router, false, message);

                            if (_routerWidgets.TryGetValue(router.Name, out var widget))
                                widget.SetChecked(false, "", _lastConnectError ?? "Connection failed");

                            var down = _analyzer.OnConnectFailed(router, _lastConnectError ?? "Connection failed");
                            if (down != null) await NotifyAsync(router.Name, new List<Finding> { down }, force: true);
                            continue;
                        }

                        var up = _analyzer.OnConnected(router);
                        if (up != null) await NotifyAsync(router.Name, new List<Finding> { up }, force: true);

                        await UpdateRouterWidget(router, connection);

                        var logs = await GetRouterLogs(connection, router);
                        AppendLog($"Retrieved {logs.Count} log lines from {router.Name}.");

                        var outcome = await _analyzer.AnalyzeAsync(router, logs);
                        logCount = outcome.NewLines;
                        AppendLog(Describe(router, outcome));
                        if (outcome.UsageNote != null) AppendLog($"AI usage: {outcome.UsageNote}");

                        if (outcome.EngineError != null)
                        {
                            AppendLog($"AI engine problem: {outcome.EngineError}");
                            await NotifyEngineProblemAsync(outcome.EngineError);
                        }

                        if (!string.IsNullOrWhiteSpace(outcome.EngineSummary))
                            AppendLog($"AI summary: {outcome.EngineSummary}", true);
                        foreach (var f in outcome.Findings.Where(f => f.Severity > Severity.Info))
                            AppendLog($"  [{f.Severity}] {f.Title}" + (string.IsNullOrWhiteSpace(f.SuggestedCheck) ? "" : $"  -> {f.SuggestedCheck}"), true);

                        // Turn the dashboard card amber for the most serious new High/Critical finding
                        var top = outcome.Findings.Where(f => f.Severity >= Severity.High && !f.IsRecovery)
                                                  .OrderByDescending(f => f.Severity).FirstOrDefault();
                        if (top != null && _routerWidgets.TryGetValue(router.Name, out var card))
                            card.SetAlert(top.Severity, top.Title);

                        hasAlerts = await NotifyAsync(router.Name, outcome.Findings);
                        message = outcome.NewLines == 0
                            ? "No new logs."
                            : $"{outcome.NewLines} new lines, {outcome.Findings.Count(f => f.Severity > Severity.Info)} findings.";
                        success = outcome.EngineError == null;
                    }
                    catch (Exception routerEx)
                    {
                        message = $"Error monitoring router {router.Name}: {routerEx.Message}";
                        AppendLog(message);
                        _logger.Error(routerEx, "Error monitoring router {Router}", router.Name);
                        success = false;
                    }

                    // Add history entry
                    _historyManager.AddHistoryEntry(router, success, message, logCount, hasAlerts);
                }

                // Save configuration to persist history
                SaveConfiguration();
            }
            catch (Exception ex)
            {
                AppendLog($"Monitoring error: {ex.Message}");
                _logger.Error(ex, "Error during monitoring");
            }
            finally
            {
                if (_isMonitoring)
                {
                    // Display updated router schedules after each check
                    DisplayRouterSchedules();

                    _monitorTimer.Start();
                }
            }
        }

        private async Task<ITikConnection> ConnectToRouter(RouterConfig router)
        {
            try
            {
                var connectionType = router.UseSSL ? TikConnectionType.ApiSsl : TikConnectionType.Api;
                var connection = ConnectionFactory.CreateConnection(connectionType);

                // Set connection timeout properties
                connection.SendTimeout = 5000; // 5 seconds
                connection.ReceiveTimeout = 5000; // 5 seconds

                bool connected = false;
                string errorMessage = "";

                // Debug info
                Console.WriteLine($"[MainForm] Attempting to connect to {router.Name} at {router.RouterIP}");

                // Attempt to connect with a timeout
                var connectTask = Task.Run(() => {
                    try {
                        connection.Open(
                            router.RouterIP,
                            router.Port,
                            router.Username,
                            router.Password
                        );
                        connected = connection.IsOpened;
                        Console.WriteLine($"[MainForm] Connection opened: {connected}");
                    }
                    catch (Exception ex) {
                        errorMessage = ex.Message;
                        Console.WriteLine($"[MainForm] Connection error: {errorMessage}");
                    }
                });

                // Wait for the connection with a timeout
                if (await Task.WhenAny(connectTask, Task.Delay(6000)) != connectTask)
                {
                    // Connection timed out
                    _lastConnectError = $"Connection to {router.RouterIP}:{router.Port} timed out.";
                    AppendLog($"Connection to {router.Name} at {router.RouterIP} timed out. Please check your network settings and ensure the router is reachable.");
                    return null;
                }

                // Check if connection was successful
                if (connected && connection.IsOpened)
                {
                    return connection;
                }
                else
                {
                    string message = string.IsNullOrEmpty(errorMessage) ?
                        $"Could not connect to {router.Name} at {router.RouterIP}. Please check your settings and try again." :
                        $"Connection error: {errorMessage}";

                    // Provide more helpful guidance for common errors in logs
                    if (errorMessage.Contains("sentence"))
                    {
                        message += "\nThis error often occurs when the router API service is not enabled, there is a firewall blocking the connection, or the router is running an incompatible RouterOS version.";
                    }
                    else if (errorMessage.Contains("timeout") || errorMessage.Contains("respond"))
                    {
                        message += "\nThis error often occurs when the router IP address is incorrect, the router is not reachable on the network, or the specified port is incorrect or blocked.";
                    }
                    else if (errorMessage.Contains("denied") || errorMessage.Contains("authentication"))
                    {
                        message += "\nThis error often occurs when the username or password is incorrect, or the user account doesn't have API access permissions.";
                    }

                    _lastConnectError = message;
                    AppendLog(message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _lastConnectError = ex.Message;
                AppendLog($"Connection error: {ex.Message}");
                return null;
            }
        }

        private async Task<List<LogEvent>> GetRouterLogs(ITikConnection connection, RouterConfig router = null)
        {
            var logs = new List<LogEvent>();

            try
            {
                await Task.Run(() =>
                {
                    var command = connection.CreateCommand("/log/print");
                    foreach (var entry in command.ExecuteList())
                    {
                        logs.Add(LogEvent.Create(
                            entry.GetResponseFieldOrDefault("time", ""),
                            entry.GetResponseFieldOrDefault("topics", ""),
                            entry.GetResponseFieldOrDefault("message", "")));
                    }
                });

                if (router != null && logs.Count > 0)
                    SaveRouterLogsBackup(router, logs.Select(l => l.Raw).ToList());
            }
            catch (Exception ex)
            {
                AppendLog($"Error getting logs: {ex.Message}");
            }

            return logs;
        }

        private void SaveRouterLogsBackup(RouterConfig router, List<string> logs)
        {
            try
            {
                // Create logs directory if it doesn't exist
                string logsDir = AppPaths.RouterLogsDir;
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                // Create a sanitized router name for the filename
                string safeRouterName = string.Join("_", router.Name.Split(Path.GetInvalidFileNameChars()));

                // Create the log file path
                string logFilePath = Path.Combine(logsDir, $"{safeRouterName}_last_logs.txt");

                // Write logs to file
                File.WriteAllLines(logFilePath, logs);

                AppendLog($"Saved backup of {logs.Count} logs for router {router.Name}");
            }
            catch (Exception ex)
            {
                AppendLog($"Error saving router logs backup: {ex.Message}");
                _logger.Error(ex, "Error saving router logs backup");
            }
        }

        private void AppendLog(string message, bool isAIResponse = false)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, bool>(AppendLog), message, isAIResponse);
                return;
            }

            // Save current selection and color
            var currentStart = _logTextBox.SelectionStart;
            var currentLength = _logTextBox.SelectionLength;
            var currentColor = _logTextBox.SelectionColor;

            // Set position to end of text
            _logTextBox.SelectionStart = _logTextBox.TextLength;
            _logTextBox.SelectionLength = 0;

            // Set color based on message type
            if (isAIResponse)
            {
                _logTextBox.SelectionColor = Color.Green; // Green for AI responses
            }
            else
            {
                _logTextBox.SelectionColor = _logTextBox.ForeColor; // Default color
            }

            // Append the text
            _logTextBox.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");

            // Restore original selection and color
            _logTextBox.SelectionStart = currentStart;
            _logTextBox.SelectionLength = currentLength;
            _logTextBox.SelectionColor = currentColor;

            // Scroll to end
            _logTextBox.SelectionStart = _logTextBox.Text.Length;
            _logTextBox.ScrollToCaret();

            // Also log to file
            _logger.Information(message);
        }

        private static string Describe(RouterConfig router, AnalysisOutcome o)
        {
            if (o.NewLines == 0) return $"{router.Name}: no new log lines.";
            var text = $"{router.Name}: {o.NewLines} new lines, {o.DroppedAsNoise} routine filtered out, {o.SkippedAsKnown} already known";
            return o.SentToAi == 0
                ? text + ". Nothing needed the AI."
                : text + $", {o.SentToAi} sent to AI -> {o.Findings.Count(f => f.Severity > Severity.Info)} findings.";
        }

        /// <summary>
        /// Sends findings that are at/above the notify level and not already reported (or got worse).
        /// Returns true when there was something to report.
        /// </summary>
        private async Task<bool> NotifyAsync(string source, List<Finding> findings, bool force = false)
        {
            var a = _settings.AnalysisSettings;
            var toSend = findings
                .Where(f => force || f.IsRecovery || (f.Severity >= a.NotifyMinSeverity && _memory.ShouldNotify(f, a.RepeatCooldownHours)))
                .ToList();
            if (toSend.Count == 0) return false;

            var telegram = new TelegramService(_settings.TelegramSettings);
            if (telegram.IsConfigured)
            {
                var (ok, error) = await telegram.SendFindingsAsync(source, toSend);
                if (!ok)
                {
                    AppendLog(error ?? "Telegram send failed.");
                    return true; // not marked as notified, so it is retried next check
                }
                AppendLog($"Telegram: sent {toSend.Count} alert(s) for {source}.");
            }
            else
            {
                AppendLog($"{toSend.Count} alert(s) for {source} (Telegram is off, shown here only).");
            }

            foreach (var f in toSend) _memory.MarkNotified(f);
            return true;
        }

        private Task<bool> NotifyEngineProblemAsync(string error) =>
            NotifyAsync("AIMonitor+", new List<Finding>
            {
                new Finding
                {
                    RouterName = "AIMonitor+",
                    Severity = Severity.High,
                    Title = "AI engine not working - only built-in rules are alerting",
                    Impact = error,
                    SuggestedCheck = "Settings > AI Engine > Test",
                    Fingerprints = { Analysis.Fingerprint.Hash("engine-error|" + _settings.LLMSettings.Provider) },
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now,
                    FromRules = true
                }
            });

        private void BackupRestoreMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using var backupRestoreForm = new BackupRestoreForm(_settings, _configPath);
                if (backupRestoreForm.ShowDialog() == DialogResult.OK)
                {
                    // Reload settings if restore was performed
                    _settings = LoadConfiguration();

                    // Reinitialize the schedule manager with the updated settings
                    _scheduleManager = new Services.ScheduleManager(_settings);

                    // If monitoring is active, restart it
                    if (_isMonitoring)
                    {
                        StopMonitoring();
                        StartMonitoring();
                    }

                    AppendLog("Configuration has been restored from backup.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in backup/restore: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendLog($"Error in backup/restore: {ex.Message}");
            }
        }

        private void DisplayRouterSchedules()
        {
            if (!_isMonitoring || _settings.MikrotikSettings.Routers.Count == 0)
                return;

            AppendLog("\nRouter Schedule Information:");
            AppendLog("=============================");

            foreach (var router in _settings.MikrotikSettings.Routers.Where(r => r.IsEnabled))
            {
                string scheduleInfo;

                if (!router.Schedule.Enabled)
                {
                    scheduleInfo = "Schedule disabled";
                }
                else
                {
                    var nextRun = router.Schedule.NextScheduledRun;
                    var timeUntilNextRun = nextRun - DateTime.Now;

                    if (timeUntilNextRun.TotalMinutes < 0)
                    {
                        scheduleInfo = "Due for check now";
                    }
                    else
                    {
                        scheduleInfo = $"Next check at {nextRun:HH:mm:ss} (in {(int)timeUntilNextRun.TotalMinutes} min)";
                    }

                    // Add schedule details
                    scheduleInfo += $" | Interval: {router.Schedule.IntervalMinutes} min | Priority: {router.Schedule.Priority}";
                }

                AppendLog($"Router: {router.Name} - {scheduleInfo}");
            }

            AppendLog("=============================");
        }

        private void AddRouterWidget(RouterConfig router)
        {
            if (!router.ShowWidget)
            {
                return;
            }

            var widget = new Controls.DashboardWidget(router)
            {
                AlertHold = TimeSpan.FromHours(Math.Max(1, _settings.AnalysisSettings.RepeatCooldownHours))
            };
            _routerWidgets[router.Name] = widget;
            _dashboardPanel.Controls.Add(widget);
        }

        private void RefreshDashboard()
        {
            // Clear existing widgets
            _dashboardPanel.Controls.Clear();
            _routerWidgets.Clear();

            // Add widgets for enabled routers
            foreach (var router in _settings.MikrotikSettings.Routers)
            {
                AddRouterWidget(router);
            }
            LayoutMain();
        }

        /// <summary>
        /// Stretches the card area and log to the window. The card area grows to show every row of cards,
        /// up to about half the window; beyond that it scrolls and the log keeps the rest.
        /// </summary>
        private void LayoutMain()
        {
            if (_dashboardPanel == null || _logTextBox == null || ClientSize.Height == 0) return;

            int margin = _dashboardPanel.Left;
            int width = Math.Max(200, ClientSize.Width - margin * 2);
            _dashboardPanel.Width = width;

            var card = _dashboardPanel.Controls.Count > 0 ? _dashboardPanel.Controls[0] : null;
            int cardW = card != null ? card.Width + card.Margin.Horizontal : 282;
            int cardH = card != null ? card.Height + card.Margin.Vertical : 182;
            int perRow = Math.Max(1, (width - _dashboardPanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth) / cardW);
            int rows = Math.Max(1, (int)Math.Ceiling(_dashboardPanel.Controls.Count / (double)perRow));

            int wanted = rows * cardH + _dashboardPanel.Padding.Vertical;
            int available = ClientSize.Height - _dashboardPanel.Top - 160;   // always leave room for the log
            int maxHeight = Math.Max(cardH + _dashboardPanel.Padding.Vertical, Math.Min(available, (ClientSize.Height - _dashboardPanel.Top) / 2));
            _dashboardPanel.Height = Math.Min(wanted, maxHeight);

            _logTextBox.Top = _dashboardPanel.Bottom + 8;
            _logTextBox.Left = margin;
            _logTextBox.Width = width;
            _logTextBox.Height = Math.Max(80, ClientSize.Height - _logTextBox.Top - margin);
        }

        private async Task UpdateRouterWidget(RouterConfig router, ITikConnection connection)
        {
            if (!_routerWidgets.TryGetValue(router.Name, out var widget))
            {
                return;
            }

            try
            {
                bool isOnline = connection != null && connection.IsOpened;
                string lastCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string uptime = "Unknown";

                if (isOnline)
                {
                    try
                    {
                        var command = connection.CreateCommand("/system/resource/print");
                        var systemResource = command.ExecuteList();
                        var resource = systemResource.FirstOrDefault();
                        if (resource != null)
                        {
                            uptime = resource.GetResponseField("uptime");
                        }
                    }
                    catch
                    {
                        uptime = "Error getting uptime";
                    }
                }

                widget.UpdateInfo(isOnline, lastCheck, uptime);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error updating router widget for {RouterName}", router.Name);
            }
        }
    }
}
