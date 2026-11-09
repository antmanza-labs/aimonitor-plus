using System.ComponentModel;

namespace SimpleMikrotikMonitor
{
    public enum PredefinedRuleType
    {
        InterfaceDown,
        MultipleDisconnects,
        WirelessDisconnects,
        DHCPLeaseIssues,
        InterfaceUpRecovery,
        PacketLossAlert,
        BandwidthSaturation,
        IgnoreLoginAttempts,
        FirewallBlockAlert,
        CPUOverload,
        MemoryWarning
    }
    public partial class RulesForm : Form
    {
        private readonly RulesSettings _settings;
        private readonly BindingList<MonitoringRule> _rulesList;
        private DataGridView _rulesGrid;

        public RulesForm(RulesSettings settings)
        {
            _settings = settings;

            // Create binding list from existing rules
            _rulesList = new BindingList<MonitoringRule>(_settings.Rules);

            InitializeUI();
        }

        private void InitializeUI()
        {
            // Form properties
            Text = "AIMonitor+ Rules";
            Size = new Size(700, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            // Create main layout panel
            var mainLayoutPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1,
                Padding = new Padding(8)
            };

            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Add header label
            var headerLabel = new Label
            {
                Text = "Configure Monitoring Rules",
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };

            mainLayoutPanel.Controls.Add(headerLabel, 0, 0);

            // Add enable rules checkbox
            var enableRulesCheckbox = new CheckBox
            {
                Text = "Enable Rules Filtering",
                Checked = _settings.EnableRules,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            enableRulesCheckbox.CheckedChanged += (s, e) => _settings.EnableRules = enableRulesCheckbox.Checked;

            mainLayoutPanel.Controls.Add(enableRulesCheckbox, 0, 1);

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
                Text = "Add Rule",
                Image = SystemIcons.Application.ToBitmap(),
                ImageTransparentColor = Color.Magenta,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
            };
            addButton.Click += (s, e) => AddRule();

            // Add dropdown for predefined rules
            var predefinedRulesButton = new ToolStripDropDownButton
            {
                Text = "Add Predefined Rule",
                Image = SystemIcons.Information.ToBitmap(),
                ImageTransparentColor = Color.Magenta,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
            };

            // Add menu items for different predefined rules
            // Interface rules
            var interfaceDownItem = new ToolStripMenuItem("Interface Down Detection");
            interfaceDownItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.InterfaceDown);

            var interfaceUpItem = new ToolStripMenuItem("Interface Up Recovery");
            interfaceUpItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.InterfaceUpRecovery);

            var multipleDisconnectsItem = new ToolStripMenuItem("Multiple Disconnects Detection");
            multipleDisconnectsItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.MultipleDisconnects);

            var wirelessDisconnectsItem = new ToolStripMenuItem("Wireless Client Disconnects");
            wirelessDisconnectsItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.WirelessDisconnects);

            // Network performance rules
            var packetLossItem = new ToolStripMenuItem("Packet Loss Alert");
            packetLossItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.PacketLossAlert);

            var bandwidthSaturationItem = new ToolStripMenuItem("Bandwidth Saturation");
            bandwidthSaturationItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.BandwidthSaturation);

            // Security rules
            var ignoreLoginItem = new ToolStripMenuItem("Ignore Login Attempts");
            ignoreLoginItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.IgnoreLoginAttempts);

            var firewallBlockItem = new ToolStripMenuItem("Firewall Block Alert");
            firewallBlockItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.FirewallBlockAlert);

            // System health rules
            var cpuOverloadItem = new ToolStripMenuItem("CPU Overload");
            cpuOverloadItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.CPUOverload);

            var memoryWarningItem = new ToolStripMenuItem("Memory Warning");
            memoryWarningItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.MemoryWarning);

            var dhcpLeaseItem = new ToolStripMenuItem("DHCP Lease Issues");
            dhcpLeaseItem.Click += (s, e) => AddPredefinedRule(PredefinedRuleType.DHCPLeaseIssues);

            // Create categories
            var interfaceCategory = new ToolStripMenuItem("Interface Rules");
            interfaceCategory.DropDownItems.AddRange(new ToolStripItem[] { interfaceDownItem, interfaceUpItem, multipleDisconnectsItem, wirelessDisconnectsItem });

            var networkCategory = new ToolStripMenuItem("Network Performance Rules");
            networkCategory.DropDownItems.AddRange(new ToolStripItem[] { packetLossItem, bandwidthSaturationItem });

            var securityCategory = new ToolStripMenuItem("Security Rules");
            securityCategory.DropDownItems.AddRange(new ToolStripItem[] { ignoreLoginItem, firewallBlockItem });

            var systemCategory = new ToolStripMenuItem("System Health Rules");
            systemCategory.DropDownItems.AddRange(new ToolStripItem[] { cpuOverloadItem, memoryWarningItem, dhcpLeaseItem });

            predefinedRulesButton.DropDownItems.AddRange(new ToolStripItem[]
            {
                interfaceCategory,
                networkCategory,
                securityCategory,
                systemCategory
            });

            var deleteButton = new ToolStripButton
            {
                Text = "Delete Rule",
                Image = SystemIcons.Error.ToBitmap(),
                ImageTransparentColor = Color.Magenta,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Enabled = false
            };
            deleteButton.Click += (s, e) => DeleteSelectedRule();

            toolStrip.Items.AddRange(new ToolStripItem[] { addButton, predefinedRulesButton, new ToolStripSeparator(), deleteButton });

            // Create rules grid
            _rulesGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };

            // Define columns
            var nameColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Name",
                DataPropertyName = "Name",
                Width = 120
            };

            var patternColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Pattern",
                DataPropertyName = "Pattern",
                Width = 200
            };

            var isRegexColumn = new DataGridViewCheckBoxColumn
            {
                HeaderText = "Is Regex",
                DataPropertyName = "IsRegex",
                Width = 60
            };

            var actionColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "Action",
                DataPropertyName = "Action",
                Width = 120
            };
            actionColumn.Items.Add(RuleAction.Monitor);
            actionColumn.Items.Add(RuleAction.Ignore);
            actionColumn.Items.Add(RuleAction.MultipleDisconnects);

            var enabledColumn = new DataGridViewCheckBoxColumn
            {
                HeaderText = "Enabled",
                DataPropertyName = "IsEnabled",
                Width = 60
            };

            var llmInstructionsColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "LLM Instructions",
                DataPropertyName = "LlmInstructions",
                Width = 200,
                ToolTipText = "Custom instructions for the LLM about how to analyze logs matching this rule"
            };

            // Columns for multiple disconnects settings
            var disconnectThresholdColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Disconnect Threshold",
                DataPropertyName = "DisconnectThreshold",
                Width = 80,
                ToolTipText = "Number of disconnects to trigger alert"
            };

            var timeWindowColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Time Window (min)",
                DataPropertyName = "TimeWindowMinutes",
                Width = 80,
                ToolTipText = "Time window in minutes to count disconnects"
            };

            var devicePatternColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Device Pattern",
                DataPropertyName = "DevicePattern",
                Width = 120,
                ToolTipText = "Optional pattern to match specific devices (leave empty for all devices)"
            };

            _rulesGrid.Columns.AddRange(new DataGridViewColumn[]
            {
                nameColumn, patternColumn, isRegexColumn, actionColumn, enabledColumn,
                disconnectThresholdColumn, timeWindowColumn, devicePatternColumn, llmInstructionsColumn
            });

            // Bind data
            var bindingSource = new BindingSource
            {
                DataSource = _rulesList
            };
            _rulesGrid.DataSource = bindingSource;

            // Enable/disable delete button based on selection
            _rulesGrid.SelectionChanged += (s, e) =>
            {
                deleteButton.Enabled = _rulesGrid.SelectedRows.Count > 0;
            };

            // Handle cell value changed to show/hide columns based on action
            _rulesGrid.CellValueChanged += (s, e) =>
            {
                // Check if the action column was changed
                if (e.ColumnIndex == actionColumn.Index && e.RowIndex >= 0)
                {
                    var row = _rulesGrid.Rows[e.RowIndex];
                    var action = (RuleAction)row.Cells[actionColumn.Index].Value;

                    // Update pattern column based on action
                    if (action == RuleAction.MultipleDisconnects)
                    {
                        // For multiple disconnects, pattern should be "disconnect" by default
                        if (string.IsNullOrEmpty(row.Cells[patternColumn.Index].Value?.ToString()))
                        {
                            row.Cells[patternColumn.Index].Value = "disconnect";
                        }
                    }
                }
            };

            // Handle DataBindingComplete to format rows based on action
            _rulesGrid.DataBindingComplete += (s, e) =>
            {
                foreach (DataGridViewRow row in _rulesGrid.Rows)
                {
                    if (row.DataBoundItem is MonitoringRule rule)
                    {
                        UpdateRowVisibility(row, rule.Action);
                    }
                }
            };

            // Add controls to grid panel
            gridPanel.Controls.Add(_rulesGrid);
            gridPanel.Controls.Add(toolStrip);
            _rulesGrid.BringToFront();

            toolStrip.Dock = DockStyle.Top;

            mainLayoutPanel.Controls.Add(gridPanel, 0, 2);

            // Create help text
            var helpText = new Label
            {
                Text = "Rules are applied in order. 'Monitor' rules include matching logs, 'Ignore' rules exclude them.\n" +
                       "If any 'Monitor' rules exist, only logs matching those rules will be analyzed.\n" +
                       "'MultipleDisconnects' rules track device disconnects and alert when threshold is exceeded within time window.",
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 10)
            };

            mainLayoutPanel.Controls.Add(helpText, 0, 3);

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
            Controls.Add(mainLayoutPanel);
            Controls.Add(buttonsPanel);

            // Set accept/cancel buttons
            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        private void AddRule()
        {
            var newRule = new MonitoringRule
            {
                Name = "New Rule",
                Pattern = "error",
                IsRegex = false,
                Action = RuleAction.Monitor,
                IsEnabled = true,
                LlmInstructions = "Pay special attention to these logs and explain their significance",
                // Default values for multiple disconnects settings
                DisconnectThreshold = 3,
                TimeWindowMinutes = 60,
                DevicePattern = ""
            };

            _rulesList.Add(newRule);

            // Select the new row
            int lastIndex = _rulesGrid.Rows.Count - 1;
            if (lastIndex >= 0)
            {
                _rulesGrid.CurrentCell = _rulesGrid.Rows[lastIndex].Cells[0];
                _rulesGrid.BeginEdit(true);
            }
        }

        private void DeleteSelectedRule()
        {
            if (_rulesGrid.CurrentRow != null)
            {
                var rule = _rulesGrid.CurrentRow.DataBoundItem as MonitoringRule;
                if (rule == null) return;

                var result = MessageBox.Show(
                    $"Are you sure you want to delete the rule '{rule.Name}'?",
                    "Confirm Deletion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    _rulesList.Remove(rule);
                }
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Update rules
                _settings.Rules = _rulesList.ToList();

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving rules: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddPredefinedRule(PredefinedRuleType ruleType)
        {
            MonitoringRule newRule;

            switch (ruleType)
            {
                case PredefinedRuleType.InterfaceDown:
                    newRule = new MonitoringRule
                    {
                        Name = "Interface Down Alert",
                        Pattern = @"(interface|ether\d+|wlan\d+|bridge).*(down|disconnected|link.*lost)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Alert about network interface that has gone down. This is a critical issue that may affect network connectivity."
                    };
                    break;

                case PredefinedRuleType.MultipleDisconnects:
                    newRule = new MonitoringRule
                    {
                        Name = "Multiple Disconnects Detection",
                        Pattern = "disconnect",
                        IsRegex = false,
                        Action = RuleAction.MultipleDisconnects,
                        IsEnabled = true,
                        DisconnectThreshold = 3,
                        TimeWindowMinutes = 30,
                        DevicePattern = "",
                        LlmInstructions = "This device is experiencing frequent disconnections which may indicate signal issues, hardware problems, or configuration issues."
                    };
                    break;

                case PredefinedRuleType.WirelessDisconnects:
                    newRule = new MonitoringRule
                    {
                        Name = "Wireless Client Disconnects",
                        Pattern = @"wireless.*(disconnect|lost)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor wireless client disconnections which may indicate signal issues or interference."
                    };
                    break;

                case PredefinedRuleType.DHCPLeaseIssues:
                    newRule = new MonitoringRule
                    {
                        Name = "DHCP Lease Issues",
                        Pattern = @"dhcp.*(lease|expired|conflict)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor DHCP lease issues which may indicate IP address conflicts or exhaustion of the DHCP pool."
                    };
                    break;

                case PredefinedRuleType.InterfaceUpRecovery:
                    newRule = new MonitoringRule
                    {
                        Name = "Interface Up Recovery",
                        Pattern = @"(interface|ether\d+|wlan\d+|bridge).*(up|connected|link.*established)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor interfaces that have recovered from a down state. This is useful for tracking intermittent connectivity issues."
                    };
                    break;

                case PredefinedRuleType.PacketLossAlert:
                    newRule = new MonitoringRule
                    {
                        Name = "Packet Loss Alert",
                        Pattern = @"(packet loss|drop|dropped packets|high latency)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor packet loss and high latency issues which may indicate network congestion or connectivity problems."
                    };
                    break;

                case PredefinedRuleType.BandwidthSaturation:
                    newRule = new MonitoringRule
                    {
                        Name = "Bandwidth Saturation",
                        Pattern = @"(bandwidth|traffic).*(limit reached|exceeded|saturation|congestion)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor bandwidth saturation events which may indicate heavy network usage or potential DoS attacks."
                    };
                    break;

                case PredefinedRuleType.IgnoreLoginAttempts:
                    newRule = new MonitoringRule
                    {
                        Name = "Ignore Login Attempts",
                        Pattern = @"(login|logged in|user authentication|session opened)",
                        IsRegex = true,
                        Action = RuleAction.Ignore,
                        IsEnabled = true,
                        LlmInstructions = "Ignore routine login events to reduce noise in the logs."
                    };
                    break;

                case PredefinedRuleType.FirewallBlockAlert:
                    newRule = new MonitoringRule
                    {
                        Name = "Firewall Block Alert",
                        Pattern = @"(firewall|filter).*(blocked|dropped|rejected)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor firewall block events which may indicate potential security threats or misconfigured services."
                    };
                    break;

                case PredefinedRuleType.CPUOverload:
                    newRule = new MonitoringRule
                    {
                        Name = "CPU Overload",
                        Pattern = @"(cpu|processor).*(high|overload|usage|load)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor CPU overload events which may indicate resource constraints or malicious activity."
                    };
                    break;

                case PredefinedRuleType.MemoryWarning:
                    newRule = new MonitoringRule
                    {
                        Name = "Memory Warning",
                        Pattern = @"(memory|ram).*(low|warning|critical)",
                        IsRegex = true,
                        Action = RuleAction.Monitor,
                        IsEnabled = true,
                        LlmInstructions = "Monitor memory warning events which may indicate resource constraints or memory leaks."
                    };
                    break;

                default:
                    return;
            }

            _rulesList.Add(newRule);

            // Select the new row
            int lastIndex = _rulesGrid.Rows.Count - 1;
            if (lastIndex >= 0)
            {
                _rulesGrid.CurrentCell = _rulesGrid.Rows[lastIndex].Cells[0];
            }
        }

        private void UpdateRowVisibility(DataGridViewRow row, RuleAction action)
        {
            // Get column indices for multiple disconnects settings
            int disconnectThresholdIndex = -1;
            int timeWindowIndex = -1;
            int devicePatternIndex = -1;

            // Find the column indices
            for (int i = 0; i < _rulesGrid.Columns.Count; i++)
            {
                var column = _rulesGrid.Columns[i];
                if (column.DataPropertyName == "DisconnectThreshold")
                    disconnectThresholdIndex = i;
                else if (column.DataPropertyName == "TimeWindowMinutes")
                    timeWindowIndex = i;
                else if (column.DataPropertyName == "DevicePattern")
                    devicePatternIndex = i;
            }

            // Set cell readOnly property based on action
            if (disconnectThresholdIndex >= 0 && timeWindowIndex >= 0 && devicePatternIndex >= 0)
            {
                bool isMultipleDisconnects = action == RuleAction.MultipleDisconnects;

                // Make cells read-only if not a multiple disconnects rule
                row.Cells[disconnectThresholdIndex].ReadOnly = !isMultipleDisconnects;
                row.Cells[timeWindowIndex].ReadOnly = !isMultipleDisconnects;
                row.Cells[devicePatternIndex].ReadOnly = !isMultipleDisconnects;

                // Set background color to indicate read-only status
                var backgroundColor = isMultipleDisconnects ? Color.White : SystemColors.Control;
                row.Cells[disconnectThresholdIndex].Style.BackColor = backgroundColor;
                row.Cells[timeWindowIndex].Style.BackColor = backgroundColor;
                row.Cells[devicePatternIndex].Style.BackColor = backgroundColor;

                // Only set default values if the cell value is null or empty
                if (!isMultipleDisconnects)
                {
                    // Check if the cell value is null before setting a default
                    if (row.Cells[disconnectThresholdIndex].Value == null || row.Cells[disconnectThresholdIndex].Value.ToString() == "")
                        row.Cells[disconnectThresholdIndex].Value = 3; // Default value

                    if (row.Cells[timeWindowIndex].Value == null || row.Cells[timeWindowIndex].Value.ToString() == "")
                        row.Cells[timeWindowIndex].Value = 60; // Default value

                    if (row.Cells[devicePatternIndex].Value == null)
                        row.Cells[devicePatternIndex].Value = string.Empty;
                }
            }
        }
    }
}
