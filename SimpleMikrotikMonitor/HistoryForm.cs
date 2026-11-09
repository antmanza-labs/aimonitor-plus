using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SimpleMikrotikMonitor.Services;

namespace SimpleMikrotikMonitor
{
    public partial class HistoryForm : Form
    {
        private readonly AppSettings _settings;
        private readonly HistoryManager _historyManager;
        private DataGridView _historyGrid;
        private ComboBox _routerFilterComboBox;

        public HistoryForm(AppSettings settings)
        {
            _settings = settings;
            _historyManager = new HistoryManager(settings);
            InitializeComponent();

            // Apply current theme
            ThemeManager.ApplyTheme(this, _settings.UISettings.Theme);

            LoadHistory();
        }

        private void InitializeComponent()
        {
            this.Text = "AIMonitor+ History";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = true;
            this.MaximizeBox = true;

            // Main layout panel
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(10)
            };

            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            // Filter panel
            var filterPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 10)
            };

            var filterLabel = new Label
            {
                Text = "Filter by Router:",
                AutoSize = true,
                Margin = new Padding(0, 5, 10, 0)
            };

            _routerFilterComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 200,
                Margin = new Padding(0, 0, 10, 0)
            };

            // Add "All Routers" option
            _routerFilterComboBox.Items.Add("All Routers");

            // Add each router
            foreach (var router in _settings.MikrotikSettings.Routers)
            {
                _routerFilterComboBox.Items.Add(router.Name);
            }

            _routerFilterComboBox.SelectedIndex = 0;
            _routerFilterComboBox.SelectedIndexChanged += FilterComboBox_SelectedIndexChanged;

            filterPanel.Controls.Add(filterLabel);
            filterPanel.Controls.Add(_routerFilterComboBox);

            // History grid
            _historyGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = true,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                MultiSelect = false,
                BackgroundColor = SystemColors.Window
            };

            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Timestamp",
                HeaderText = "Timestamp",
                DataPropertyName = "Timestamp"
            });

            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Status",
                HeaderText = "Status",
                DataPropertyName = "Status"
            });

            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Message",
                HeaderText = "Message",
                DataPropertyName = "Message"
            });

            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "LogCount",
                HeaderText = "Logs",
                DataPropertyName = "LogCount"
            });

            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Alerts",
                HeaderText = "Alerts",
                DataPropertyName = "Alerts"
            });

            // Buttons panel
            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 10, 0, 0)
            };

            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(5)
            };

            var clearButton = new Button
            {
                Text = "Clear History",
                Margin = new Padding(5)
            };
            clearButton.Click += ClearButton_Click;

            var refreshButton = new Button
            {
                Text = "Refresh",
                Margin = new Padding(5)
            };
            refreshButton.Click += RefreshButton_Click;

            buttonsPanel.Controls.Add(closeButton);
            buttonsPanel.Controls.Add(clearButton);
            buttonsPanel.Controls.Add(refreshButton);

            // Add controls to main panel
            mainPanel.Controls.Add(filterPanel, 0, 0);
            mainPanel.Controls.Add(_historyGrid, 0, 1);
            mainPanel.Controls.Add(buttonsPanel, 0, 2);

            this.Controls.Add(mainPanel);
            this.CancelButton = closeButton;
        }

        private void LoadHistory()
        {
            _historyGrid.Rows.Clear();

            // Add a debug entry to ensure the grid is working
            _historyGrid.Rows.Add(
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "Debug",
                "History view initialized",
                "0",
                "No"
            );

            List<RouterHistoryEntry> historyEntries = new List<RouterHistoryEntry>();

            try
            {
                if (_routerFilterComboBox.SelectedIndex == 0)
                {
                    // "All Routers" selected
                    historyEntries = _historyManager.GetAllHistory();
                }
                else
                {
                    // Specific router selected
                    var selectedRouterName = _routerFilterComboBox.SelectedItem?.ToString();
                    if (selectedRouterName != null)
                    {
                        var selectedRouter = _settings.MikrotikSettings.Routers.FirstOrDefault(r => r.Name == selectedRouterName);

                        if (selectedRouter != null)
                        {
                            historyEntries = _historyManager.GetHistoryForRouter(selectedRouter);
                        }
                    }
                }

                // If no history entries, add a message
                if (historyEntries.Count == 0)
                {
                    _historyGrid.Rows.Add(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        "Info",
                        "No history entries found",
                        "0",
                        "No"
                    );
                }
            }
            catch (Exception ex)
            {
                _historyGrid.Rows.Add(
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    "Error",
                    $"Error loading history: {ex.Message}",
                    "0",
                    "No"
                );
            }

            foreach (var entry in historyEntries)
            {
                var status = entry.Success ? "Success" : "Failed";
                var alerts = entry.HasAlerts ? "Yes" : "No";

                _historyGrid.Rows.Add(
                    entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    status,
                    entry.Message,
                    entry.LogCount,
                    alerts
                );

                // Color row based on status
                var row = _historyGrid.Rows[_historyGrid.Rows.Count - 1];
                if (!entry.Success)
                {
                    row.DefaultCellStyle.BackColor = Color.LightPink;
                }
                else if (entry.HasAlerts)
                {
                    row.DefaultCellStyle.BackColor = Color.LightYellow;
                }
            }
        }

        private void FilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadHistory();
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            LoadHistory();
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear the history?",
                "Confirm Clear History",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                if (_routerFilterComboBox.SelectedIndex == 0)
                {
                    // Clear all history
                    _historyManager.ClearAllHistory();
                }
                else
                {
                    // Clear history for selected router
                    var selectedRouterName = _routerFilterComboBox.SelectedItem.ToString();
                    var selectedRouter = _settings.MikrotikSettings.Routers.FirstOrDefault(r => r.Name == selectedRouterName);

                    if (selectedRouter != null)
                        _historyManager.ClearHistoryForRouter(selectedRouter);
                }

                LoadHistory();
            }
        }
    }
}
