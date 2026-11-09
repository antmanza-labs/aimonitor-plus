using System.ComponentModel;
using tik4net;
using tik4net.Objects;

namespace SimpleMikrotikMonitor
{
    public partial class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly BindingList<RouterConfig> _routersList;
        private DataGridView _routersGrid;
        private Controls.AiEngineSettingsPanel _aiPanel;

        public AppSettings Settings => _settings;

        public SettingsForm(AppSettings settings)
        {
            _settings = settings;

            // Create binding list from existing routers
            _routersList = new BindingList<RouterConfig>(_settings.MikrotikSettings.Routers);

            InitializeUI();

            // Apply current theme
            ThemeManager.ApplyTheme(this, _settings.UISettings.Theme);

            // Make sure the theme is applied to all child controls
            foreach (Control control in Controls)
            {
                ThemeManager.ApplyTheme(control, _settings.UISettings.Theme);

                // Apply to TabControl pages
                if (control is TabControl tabControl)
                {
                    foreach (TabPage tabPage in tabControl.TabPages)
                    {
                        ThemeManager.ApplyTheme(tabPage, _settings.UISettings.Theme);
                    }
                }
            }
        }

        private void InitializeUI()
        {
            // Form properties
            Text = "AIMonitor+ Settings";
            Size = new Size(800, 600);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            // Create tab control
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(10, 3),
                // Set appearance properties
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(100, 30)
            };

            // Set custom drawing for TabControl
            tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl.DrawItem += (sender, e) =>
            {
                // Get the tab control
                var tc = sender as TabControl;

                // Set colors for light theme
                var backColor = ThemeManager.LightBackColor;
                var foreColor = ThemeManager.LightForeColor;
                var unselectedTextColor = Color.DarkGray;

                // Draw the tab background
                using (var brush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }

                // Get the tab page
                var tabPage = tc.TabPages[e.Index];

                // Set text color based on selection state
                var textColor = e.State == DrawItemState.Selected ? foreColor : unselectedTextColor;

                // Draw the text
                var textRect = e.Bounds;
                textRect.Inflate(-2, -2);
                using (var textBrush = new SolidBrush(textColor))
                {
                    var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    e.Graphics.DrawString(tabPage.Text, tc.Font, textBrush, textRect, format);
                }

                // Draw a highlight for the selected tab
                if (e.State == DrawItemState.Selected)
                {
                    using (var pen = new Pen(Color.FromArgb(0, 122, 204), 2))
                    {
                        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom, e.Bounds.Right, e.Bounds.Bottom);
                    }
                }

                // Draw a border around the tab
                using (var pen = new Pen(Color.FromArgb(200, 200, 200)))
                {
                    e.Graphics.DrawRectangle(pen, new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1));
                }

                e.DrawFocusRectangle();
            };

            // Create Routers tab
            var routersTab = new TabPage("Routers");

            // Create Rules tab
            var rulesTab = new TabPage("Rules");

            // Create LLM Settings tab
            var llmTab = new TabPage("AI Engine");

            // Create LLM Instructions tab
            var llmInstructionsTab = new TabPage("AI Notes");

            // Create Notifications tab
            var notificationsTab = new TabPage("Notifications");

            // Create main layout panel
            var mainLayoutPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(8)
            };

            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Add header label
            var headerLabel = new Label
            {
                Text = "Configure Mikrotik Routers",
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };

            mainLayoutPanel.Controls.Add(headerLabel, 0, 0);

            // Create grid panel
            var gridPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0)
            };

            // Create toolbar
            var toolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new ToolStripProfessionalRenderer()
            };

            var addButton = new ToolStripButton
            {
                Text = "Add Router",
                Image = SystemIcons.Application.ToBitmap(),
                ImageTransparentColor = Color.Magenta,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
            };
            addButton.Click += (s, e) => AddRouter();

            var scheduleButton = new ToolStripButton
            {
                Text = "Schedule",
                ToolTipText = "Configure router schedule",
                Image = SystemIcons.Information.ToBitmap(),
                ImageTransparentColor = Color.Magenta,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Enabled = false
            };
            scheduleButton.Click += (s, e) => ConfigureSchedule();

            var deleteButton = new ToolStripButton
            {
                Text = "Delete Router",
                Image = SystemIcons.Error.ToBitmap(),
                ImageTransparentColor = Color.Magenta,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Enabled = false
            };
            deleteButton.Click += (s, e) => DeleteSelectedRouter();

            toolStrip.Items.AddRange(new ToolStripItem[] { addButton, new ToolStripSeparator(), scheduleButton, new ToolStripSeparator(), deleteButton });

            // Create routers grid
            _routersGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EnableHeadersVisualStyles = false
            };

            // Define columns
            var enabledColumn = new DataGridViewCheckBoxColumn
            {
                Name = "Enabled",
                HeaderText = "Enabled",
                DataPropertyName = "IsEnabled",
                Width = 70
            };

            var showWidgetColumn = new DataGridViewCheckBoxColumn
            {
                Name = "ShowWidget",
                HeaderText = "Show Widget",
                DataPropertyName = "ShowWidget",
                Width = 90,
                ToolTipText = "Show this router in the dashboard"
            };

            var nameColumn = new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "Name",
                DataPropertyName = "Name",
                Width = 120
            };

            var ipColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Router IP",
                DataPropertyName = "RouterIP",
                Width = 120
            };

            var usernameColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Username",
                DataPropertyName = "Username",
                Width = 100
            };

            var passwordColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Password",
                DataPropertyName = "Password",
                Width = 100
            };

            // Set up password masking for the password column
            _routersGrid.CellFormatting += (s, e) => {
                if (e.ColumnIndex == passwordColumn.Index && e.Value != null)
                {
                    e.Value = new string('*', e.Value.ToString().Length);
                    e.FormattingApplied = true;
                }
            };

            // Create a custom editing control for the password column
            _routersGrid.EditingControlShowing += (s, e) => {
                if (_routersGrid.CurrentCell.ColumnIndex == passwordColumn.Index)
                {
                    if (e.Control is TextBox textBox)
                    {
                        // Make password visible during editing
                        textBox.UseSystemPasswordChar = false;

                        // Get the actual password value from the data source
                        int rowIndex = _routersGrid.CurrentCell.RowIndex;
                        if (rowIndex >= 0 && rowIndex < _routersList.Count)
                        {
                            textBox.Text = _routersList[rowIndex].Password;
                        }
                    }
                }
                else if (e.Control is TextBox tb)
                {
                    tb.UseSystemPasswordChar = false;
                }
            };

            // Handle cell end edit to ensure password is saved correctly
            _routersGrid.CellEndEdit += (s, e) => {
                if (e.ColumnIndex == passwordColumn.Index)
                {
                    // Get the edited value
                    var editedValue = _routersGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";

                    // Update the router password directly
                    if (e.RowIndex >= 0 && e.RowIndex < _routersList.Count)
                    {
                        _routersList[e.RowIndex].Password = editedValue;

                        // Debug info
                        Console.WriteLine($"Password updated for router {_routersList[e.RowIndex].Name}: {editedValue}");
                    }
                }
            };

            var portColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Port",
                DataPropertyName = "Port",
                Width = 60
            };

            var sslColumn = new DataGridViewCheckBoxColumn
            {
                HeaderText = "Use SSL",
                DataPropertyName = "UseSSL",
                Width = 60
            };

            // Add a schedule button column
            var scheduleColumn = new DataGridViewButtonColumn
            {
                HeaderText = "Schedule",
                Text = "Configure",
                UseColumnTextForButtonValue = true,
                Width = 80
            };

            // Add a test connection button column
            var testConnectionColumn = new DataGridViewButtonColumn
            {
                HeaderText = "Test",
                Text = "Test",
                UseColumnTextForButtonValue = true,
                Width = 60
            };

            _routersGrid.Columns.AddRange(new DataGridViewColumn[]
            {
                enabledColumn,
                showWidgetColumn,
                nameColumn,
                ipColumn,
                usernameColumn,
                passwordColumn,
                portColumn,
                sslColumn,
                scheduleColumn,
                testConnectionColumn
            });

            // Bind data
            var bindingSource = new BindingSource
            {
                DataSource = _routersList
            };
            _routersGrid.DataSource = bindingSource;

            // Enable/disable buttons based on selection
            _routersGrid.SelectionChanged += (s, e) =>
            {
                bool hasSelection = _routersGrid.SelectedRows.Count > 0;
                deleteButton.Enabled = hasSelection;
                scheduleButton.Enabled = hasSelection;
            };

            // Handle cell clicks for the schedule and test buttons
            _routersGrid.CellClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    var router = _routersList[e.RowIndex];

                    // Schedule button (second to last column)
                    if (e.ColumnIndex == scheduleColumn.Index)
                    {
                        ConfigureRouterSchedule(router);
                    }
                    // Test connection button (last column)
                    else if (e.ColumnIndex == testConnectionColumn.Index)
                    {
                        TestRouterConnection(router);
                    }
                }
            };

            // Add controls to grid panel
            gridPanel.Controls.Add(_routersGrid);
            gridPanel.Controls.Add(toolStrip);
            _routersGrid.BringToFront();

            toolStrip.Dock = DockStyle.Top;

            mainLayoutPanel.Controls.Add(gridPanel, 0, 1);

            // No API settings panel on the Routers page anymore

            // Create buttons panel
            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
                Padding = new Padding(0, 10, 0, 0)
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 80
            };

            var saveButton = new Button
            {
                Text = "Save",
                DialogResult = DialogResult.OK,
                Width = 80
            };
            saveButton.Click += SaveButton_Click;

            buttonsPanel.Controls.Add(cancelButton);
            buttonsPanel.Controls.Add(saveButton);

            // Add to form
            routersTab.Controls.Add(mainLayoutPanel);

            // Create Rules tab content
            var rulesButton = new Button
            {
                Text = "Manage Rules",
                Size = new Size(120, 30),
                Location = new Point(20, 20)
            };
            rulesButton.Click += (s, e) => ManageRules();

            var rulesDescription = new Label
            {
                Text = "Configure rules to filter which logs are analyzed by the AI.\n" +
                      "Rules can be used to focus on specific patterns or ignore irrelevant logs.",
                AutoSize = true,
                Location = new Point(20, 60)
            };

            rulesTab.Controls.Add(rulesButton);
            rulesTab.Controls.Add(rulesDescription);

            // AI engine + alerting settings
            _aiPanel = new Controls.AiEngineSettingsPanel(_settings);
            llmTab.Controls.Add(_aiPanel);

            // Create Notifications tab content
            var notificationsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 3,
                Padding = new Padding(10)
            };

            notificationsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            notificationsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            notificationsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

            for (int i = 0; i < notificationsPanel.RowCount; i++)
            {
                notificationsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            // Telegram settings
            var enableTelegramCheckbox = new CheckBox
            {
                Text = "Enable Telegram Notifications",
                Checked = _settings.TelegramSettings.EnableNotifications,
                AutoSize = true,
                Margin = new Padding(0, 5, 0, 10)
            };

            var botTokenLabel = new Label
            {
                Text = "Bot Token:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 10, 5),
                Width = 80
            };

            var botTokenTextBox = new TextBox
            {
                Text = _settings.TelegramSettings.BotToken,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };

            var chatIdLabel = new Label
            {
                Text = "Chat ID:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 10, 5),
                Width = 80
            };

            var chatIdTextBox = new TextBox
            {
                Text = _settings.TelegramSettings.ChatId,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };

            var testTelegramButton = new Button
            {
                Text = "Test",
                Dock = DockStyle.Fill,
                Margin = new Padding(5, 5, 0, 5)
            };
            testTelegramButton.Click += async (s, e) => await TestTelegramConnection();

            var telegramHelpLabel = new Label
            {
                Text = "To set up Telegram notifications:\n" +
                      "1. Create a bot using BotFather in Telegram and get the token\n" +
                      "2. Start a chat with your bot and send a message\n" +
                      "3. Visit https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getUpdates to get your chat ID",
                AutoSize = true,
                Margin = new Padding(0, 15, 0, 0)
            };

            notificationsPanel.Controls.Add(enableTelegramCheckbox, 0, 0);
            notificationsPanel.SetColumnSpan(enableTelegramCheckbox, 3);

            notificationsPanel.Controls.Add(botTokenLabel, 0, 1);
            notificationsPanel.Controls.Add(botTokenTextBox, 1, 1);
            notificationsPanel.Controls.Add(new Panel(), 2, 1);

            notificationsPanel.Controls.Add(chatIdLabel, 0, 2);
            notificationsPanel.Controls.Add(chatIdTextBox, 1, 2);
            notificationsPanel.Controls.Add(testTelegramButton, 2, 2);

            notificationsPanel.Controls.Add(telegramHelpLabel, 0, 3);
            notificationsPanel.SetColumnSpan(telegramHelpLabel, 3);

            notificationsTab.Controls.Add(notificationsPanel);

            // Create LLM Instructions tab content
            var llmInstructionsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(10)
            };
            llmInstructionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            llmInstructionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            llmInstructionsPanel.Controls.Add(new Label
            {
                Text = "Notes sent to the AI with every analysis, on top of the built-in MikroTik triage rules. " +
                       "Describe your network: which routers and links are core, upstream providers, maintenance windows, " +
                       "and what you do or don't want to be alerted about.",
                AutoSize = true,
                MaximumSize = new Size(740, 0),
                Margin = new Padding(0, 0, 0, 8)
            }, 0, 0);

            var instructionsTextBox = new TextBox
            {
                Text = _settings.LLMSettings.Instructions ?? "",
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Tag = "LLMInstructions",
                Font = new Font(Font.FontFamily, 10),
                AcceptsReturn = true,
                AcceptsTab = true
            };

            llmInstructionsPanel.Controls.Add(instructionsTextBox, 0, 1);
            llmInstructionsTab.Controls.Add(llmInstructionsPanel);

            // Add tabs to tab control
            tabControl.TabPages.AddRange(new TabPage[] { routersTab, rulesTab, llmTab, llmInstructionsTab, notificationsTab });

            Controls.Add(tabControl);
            Controls.Add(buttonsPanel);


            // Set accept/cancel buttons
            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        private void AddRouter()
        {
            var newRouter = new RouterConfig
            {
                Name = "New Router",
                RouterIP = "192.168.1.1",
                Username = "admin",
                Password = "",
                Port = 8728,
                IsEnabled = true
            };

            _routersList.Add(newRouter);

            // Select the new row
            int lastIndex = _routersGrid.Rows.Count - 1;
            if (lastIndex >= 0)
            {
                _routersGrid.CurrentCell = _routersGrid.Rows[lastIndex].Cells[0];
                _routersGrid.BeginEdit(true);
            }
        }

        private void DeleteSelectedRouter()
        {
            if (_routersGrid.CurrentRow != null)
            {
                var router = _routersGrid.CurrentRow.DataBoundItem as RouterConfig;
                if (router == null) return;

                var result = MessageBox.Show(
                    $"Are you sure you want to delete the router '{router.Name}'?",
                    "Confirm Deletion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    _routersList.Remove(router);
                }
            }
        }

        private void ConfigureSchedule()
        {
            if (_routersGrid.CurrentRow != null)
            {
                var router = _routersGrid.CurrentRow.DataBoundItem as RouterConfig;
                if (router == null) return;

                ConfigureRouterSchedule(router);
            }
        }

        private void ConfigureRouterSchedule(RouterConfig router)
        {
            using var scheduleForm = new ScheduleForm(router);
            if (scheduleForm.ShowDialog() == DialogResult.OK)
            {
                // Refresh the grid to show any changes
                _routersGrid.Refresh();
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Update routers
                _settings.MikrotikSettings.Routers = _routersList.ToList();

                // Update LLM settings
                var tabControl = Controls.OfType<TabControl>().FirstOrDefault();
                if (tabControl != null)
                {
                    _aiPanel.ApplyTo(_settings);

                    // Get LLM Instructions tab
                    var llmInstructionsTab = tabControl.TabPages[3];
                    var llmInstructionsPanel = llmInstructionsTab.Controls.OfType<TableLayoutPanel>().FirstOrDefault();

                    if (llmInstructionsPanel != null)
                    {
                        var instructionsTextBox = llmInstructionsPanel.Controls.OfType<TextBox>().FirstOrDefault();
                        if (instructionsTextBox != null)
                        {
                            _settings.LLMSettings.Instructions = instructionsTextBox.Text;
                        }
                    }

                    ReadTelegramFields(_settings.TelegramSettings);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ManageRules()
        {
            using var rulesForm = new RulesForm(_settings.RulesSettings);
            rulesForm.ShowDialog();
        }

        private void ReadTelegramFields(TelegramSettings target)
        {
            var tabControl = Controls.OfType<TabControl>().FirstOrDefault();
            var notificationsPanel = tabControl?.TabPages[4].Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (notificationsPanel == null) return;

            var enableTelegramCheckbox = notificationsPanel.Controls.OfType<CheckBox>().FirstOrDefault(c => c.Text.Contains("Telegram"));
            var botTokenTextBox = notificationsPanel.Controls.OfType<TextBox>().FirstOrDefault(t => notificationsPanel.GetRow(t) == 1);
            var chatIdTextBox = notificationsPanel.Controls.OfType<TextBox>().FirstOrDefault(t => notificationsPanel.GetRow(t) == 2);

            if (enableTelegramCheckbox != null) target.EnableNotifications = enableTelegramCheckbox.Checked;
            if (botTokenTextBox != null) target.BotToken = botTokenTextBox.Text.Trim();
            if (chatIdTextBox != null) target.ChatId = chatIdTextBox.Text.Trim();
        }

        private async Task TestTelegramConnection()
        {
            try
            {
                Cursor = Cursors.WaitCursor;

                // Test with the values on screen without saving or closing the dialog
                var current = new TelegramSettings();
                ReadTelegramFields(current);
                var telegramService = new Services.TelegramService(current);

                // Test connection
                var success = await telegramService.TestConnection();

                if (success)
                {
                    MessageBox.Show("Telegram connection successful! A test message has been sent.", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Could not connect to Telegram. Please check your Bot Token and Chat ID and try again.",
                        "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Telegram test failed: {ex.Message}", "Connection Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private async void TestRouterConnection(RouterConfig router)
        {
            if (string.IsNullOrWhiteSpace(router.RouterIP))
            {
                MessageBox.Show("Please enter a router IP address first", "Missing IP Address",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;

                // Create a connection to the router with timeout settings
                var connectionType = router.UseSSL ? TikConnectionType.ApiSsl : TikConnectionType.Api;
                var connection = ConnectionFactory.CreateConnection(connectionType);

                // Set connection timeout properties
                connection.SendTimeout = 5000; // 5 seconds
                connection.ReceiveTimeout = 5000; // 5 seconds

                bool connected = false;
                string errorMessage = "";

                // Debug info
                Console.WriteLine($"Attempting to connect to {router.Name} at {router.RouterIP}");
                Console.WriteLine($"Username: {router.Username}, Password length: {router.Password?.Length ?? 0}");

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
                        Console.WriteLine($"Connection opened: {connected}");
                    }
                    catch (Exception ex) {
                        errorMessage = ex.Message;
                        Console.WriteLine($"Connection error: {errorMessage}");
                    }
                });

                // Wait for the connection with a timeout
                if (await Task.WhenAny(connectTask, Task.Delay(6000)) != connectTask)
                {
                    // Connection timed out
                    MessageBox.Show($"Connection to {router.Name} at {router.RouterIP} timed out. Please check your network settings and ensure the router is reachable.",
                        "Connection Timeout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Check if connection was successful
                if (connected && connection.IsOpened)
                {
                    // Test a simple command to verify API communication
                    try
                    {
                        var command = connection.CreateCommand("/system/identity/print");
                        var identity = command.ExecuteScalar();

                        MessageBox.Show($"Successfully connected to {router.Name} at {router.RouterIP}!\nRouter Identity: {identity}",
                            "Connection Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception cmdEx)
                    {
                        MessageBox.Show($"Connected to {router.Name} but failed to execute test command: {cmdEx.Message}",
                            "Partial Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        // Close the connection
                        connection.Close();
                    }
                }
                else
                {
                    string message = string.IsNullOrEmpty(errorMessage) ?
                        $"Could not connect to {router.Name} at {router.RouterIP}. Please check your settings and try again." :
                        $"Connection error: {errorMessage}";

                    // Provide more helpful guidance for common errors
                    if (errorMessage.Contains("sentence"))
                    {
                        message += "\n\nThis error often occurs when:\n" +
                                  "- The router API service is not enabled\n" +
                                  "- There is a firewall blocking the connection\n" +
                                  "- The router is running an incompatible RouterOS version";
                    }
                    else if (errorMessage.Contains("timeout") || errorMessage.Contains("respond"))
                    {
                        message += "\n\nThis error often occurs when:\n" +
                                  "- The router IP address is incorrect\n" +
                                  "- The router is not reachable on the network\n" +
                                  "- The specified port is incorrect or blocked";
                    }
                    else if (errorMessage.Contains("denied") || errorMessage.Contains("authentication"))
                    {
                        message += "\n\nThis error often occurs when:\n" +
                                  "- The username or password is incorrect\n" +
                                  "- The user account doesn't have API access permissions";
                    }

                    MessageBox.Show(message, "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection test failed: {ex.Message}", "Connection Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}


