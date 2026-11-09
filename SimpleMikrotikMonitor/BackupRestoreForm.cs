using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace SimpleMikrotikMonitor
{
    public partial class BackupRestoreForm : Form
    {
        private readonly AppSettings _settings;
        private readonly string _configPath;

        public BackupRestoreForm(AppSettings settings, string configPath)
        {
            _settings = settings;
            _configPath = configPath;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // Form properties
            this.Text = "Backup and Restore Configuration";
            this.Size = new System.Drawing.Size(550, 400); // Increased size for better visibility
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Font = new System.Drawing.Font("Segoe UI", 9F);

            // Create main layout
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(10),
                RowStyles = {
                    new RowStyle(SizeType.Percent, 45),  // Increased space for backup section
                    new RowStyle(SizeType.Percent, 45),  // Increased space for restore section
                    new RowStyle(SizeType.Percent, 10)   // Reduced space for close button
                }
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Backup section
            GroupBox backupGroup = new GroupBox
            {
                Text = "Backup Configuration",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            TableLayoutPanel backupLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 3
            };

            // Configure column styles for centering
            backupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            backupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            backupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            // Configure row styles
            backupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // Label row
            backupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); // Button row

            Label backupLabel = new Label
            {
                Text = "Save your current configuration to a file. This includes all router settings, schedules, and monitoring rules.",
                Dock = DockStyle.Fill,
                AutoSize = true
            };

            Button backupButton = new Button
            {
                Text = "Create Backup",
                Dock = DockStyle.None,
                AutoSize = false,
                Padding = new Padding(10),
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 50,
                Width = 250,
                Anchor = AnchorStyles.None,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular)
            };
            backupButton.Click += BackupButton_Click;

            backupLayout.Controls.Add(backupLabel, 0, 0);
            backupLayout.SetColumnSpan(backupLabel, 3);
            backupLayout.Controls.Add(backupButton, 1, 1);
            backupLayout.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
            backupButton.Anchor = AnchorStyles.None;
            backupGroup.Controls.Add(backupLayout);

            // Restore section
            GroupBox restoreGroup = new GroupBox
            {
                Text = "Restore Configuration",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            TableLayoutPanel restoreLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 3
            };

            // Configure column styles for centering
            restoreLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            restoreLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            restoreLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            // Configure row styles
            restoreLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // Label row
            restoreLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); // Button row

            Label restoreLabel = new Label
            {
                Text = "Load a previously saved configuration file. This will replace your current settings.",
                Dock = DockStyle.Fill,
                AutoSize = true
            };

            Button restoreButton = new Button
            {
                Text = "Restore from Backup",
                Dock = DockStyle.None,
                AutoSize = false,
                Padding = new Padding(10),
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 50,
                Width = 250,
                Anchor = AnchorStyles.None,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular)
            };
            restoreButton.Click += RestoreButton_Click;

            restoreLayout.Controls.Add(restoreLabel, 0, 0);
            restoreLayout.SetColumnSpan(restoreLabel, 3);
            restoreLayout.Controls.Add(restoreButton, 1, 1);
            restoreLayout.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
            restoreButton.Anchor = AnchorStyles.None;
            restoreGroup.Controls.Add(restoreLayout);

            // Create a simple panel for the close button
            Panel buttonPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 60
            };

            // Close button - make it much larger and simpler
            Button closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Width = 150,
                Height = 40,
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                FlatStyle = FlatStyle.System,
                UseVisualStyleBackColor = true
            };

            // Position the button in the center of the panel
            closeButton.Location = new Point(
                (buttonPanel.Width - closeButton.Width) / 2,
                (buttonPanel.Height - closeButton.Height) / 2);

            // Make sure the button stays centered when the panel resizes
            buttonPanel.Resize += (s, e) => {
                closeButton.Location = new Point(
                    (buttonPanel.Width - closeButton.Width) / 2,
                    (buttonPanel.Height - closeButton.Height) / 2);
            };

            buttonPanel.Controls.Add(closeButton);

            // Add controls to main layout
            mainLayout.Controls.Add(backupGroup, 0, 0);
            mainLayout.Controls.Add(restoreGroup, 0, 1);
            mainLayout.Controls.Add(buttonPanel, 0, 2);

            // Add main layout to form
            this.Controls.Add(mainLayout);
            this.CancelButton = closeButton;
        }

        private void BackupButton_Click(object sender, EventArgs e)
        {
            try
            {
                using (SaveFileDialog saveDialog = new SaveFileDialog())
                {
                    saveDialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                    saveDialog.Title = "Save Configuration Backup";
                    saveDialog.FileName = $"AIMonitorPlus_Backup_{DateTime.Now:yyyy-MM-dd}.json";

                    if (saveDialog.ShowDialog() == DialogResult.OK)
                    {
                        // Serialize settings to JSON
                        var jsonOptions = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                        };
                        string jsonString = JsonSerializer.Serialize(_settings, jsonOptions);

                        // Write to file
                        File.WriteAllText(saveDialog.FileName, jsonString);

                        MessageBox.Show(
                            $"Configuration successfully backed up to:\n{saveDialog.FileName}",
                            "Backup Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error creating backup: {ex.Message}",
                    "Backup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RestoreButton_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog openDialog = new OpenFileDialog())
                {
                    openDialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                    openDialog.Title = "Open Configuration Backup";

                    if (openDialog.ShowDialog() == DialogResult.OK)
                    {
                        // Read JSON from file
                        string jsonString = File.ReadAllText(openDialog.FileName);

                        // Validate JSON structure before restoring
                        try
                        {
                            var jsonOptions = new JsonSerializerOptions
                            {
                                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                            };

                            // Try to deserialize to verify it's a valid configuration
                            var tempSettings = JsonSerializer.Deserialize<AppSettings>(jsonString, jsonOptions);

                            // Basic validation
                            if (tempSettings == null ||
                                tempSettings.MikrotikSettings == null ||
                                tempSettings.LogSettings == null)
                            {
                                throw new Exception("The backup file does not contain valid configuration data.");
                            }

                            // Confirm restore
                            var result = MessageBox.Show(
                                "This will replace your current configuration with the backup. Continue?",
                                "Confirm Restore",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);

                            if (result == DialogResult.Yes)
                            {
                                // Write to config file
                                File.WriteAllText(_configPath, jsonString);

                                MessageBox.Show(
                                    "Configuration restored successfully. Please restart the application for changes to take effect.",
                                    "Restore Complete",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);

                                this.DialogResult = DialogResult.OK;
                            }
                        }
                        catch (JsonException jex)
                        {
                            throw new Exception($"Invalid backup file format: {jex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error restoring backup: {ex.Message}",
                    "Restore Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
