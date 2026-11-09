using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimpleMikrotikMonitor
{
    public partial class ScheduleForm : Form
    {
        private readonly RouterConfig _router;

        public ScheduleForm(RouterConfig router)
        {
            _router = router;
            InitializeComponent();
            LoadScheduleSettings();
        }

        private void InitializeComponent()
        {
            this.Text = $"AIMonitor+ Schedule - {_router.Name}";
            this.Size = new Size(550, 550);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Main layout panel
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 10,
                ColumnCount = 2,
                Padding = new Padding(10)
            };

            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Add a header with router name
            var headerLabel = new Label
            {
                Text = $"Schedule Settings for {_router.Name}",
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(5),
                Dock = DockStyle.Fill
            };

            for (int i = 0; i < mainPanel.RowCount; i++)
            {
                mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            // Enable schedule checkbox
            var enabledCheckbox = new CheckBox
            {
                Text = "Enable Schedule",
                Checked = _router.Schedule.Enabled,
                AutoSize = true,
                Margin = new Padding(5)
            };
            enabledCheckbox.CheckedChanged += (s, e) => _router.Schedule.Enabled = enabledCheckbox.Checked;

            // Interval setting
            var intervalLabel = new Label
            {
                Text = "Check Interval (minutes):",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var intervalNumeric = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1440, // 24 hours
                Value = _router.Schedule.IntervalMinutes,
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };
            intervalNumeric.ValueChanged += (s, e) => _router.Schedule.IntervalMinutes = (int)intervalNumeric.Value;

            // Priority setting
            var priorityLabel = new Label
            {
                Text = "Priority:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var priorityComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(5)
            };

            foreach (RouterPriority priority in Enum.GetValues(typeof(RouterPriority)))
            {
                priorityComboBox.Items.Add(priority);
            }
            priorityComboBox.SelectedItem = _router.Schedule.Priority;
            priorityComboBox.SelectedIndexChanged += (s, e) =>
            {
                if (priorityComboBox.SelectedItem != null)
                    _router.Schedule.Priority = (RouterPriority)priorityComboBox.SelectedItem;
            };

            // Active days
            var daysLabel = new Label
            {
                Text = "Active Days:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var daysPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(5)
            };

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                var dayCheckbox = new CheckBox
                {
                    Text = day.ToString().Substring(0, 3),
                    Checked = _router.Schedule.ActiveDays.Contains(day),
                    AutoSize = true,
                    Margin = new Padding(3)
                };

                dayCheckbox.Tag = day;
                dayCheckbox.CheckedChanged += (s, e) =>
                {
                    var checkbox = (CheckBox)s;
                    var dayValue = (DayOfWeek)checkbox.Tag;

                    if (checkbox.Checked && !_router.Schedule.ActiveDays.Contains(dayValue))
                        _router.Schedule.ActiveDays.Add(dayValue);
                    else if (!checkbox.Checked && _router.Schedule.ActiveDays.Contains(dayValue))
                        _router.Schedule.ActiveDays.Remove(dayValue);
                };

                daysPanel.Controls.Add(dayCheckbox);
            }

            // Start time
            var startTimeLabel = new Label
            {
                Text = "Start Time:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var startTimePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Value = DateTime.Today.Add(_router.Schedule.StartTime),
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };
            startTimePicker.ValueChanged += (s, e) => _router.Schedule.StartTime = startTimePicker.Value.TimeOfDay;

            // End time
            var endTimeLabel = new Label
            {
                Text = "End Time:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var endTimePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Value = DateTime.Today.Add(_router.Schedule.EndTime),
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };
            endTimePicker.ValueChanged += (s, e) => _router.Schedule.EndTime = endTimePicker.Value.TimeOfDay;

            // Buttons
            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(5)
            };

            var saveButton = new Button
            {
                Text = "Save",
                DialogResult = DialogResult.OK,
                Margin = new Padding(5)
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(5)
            };

            buttonsPanel.Controls.Add(cancelButton);
            buttonsPanel.Controls.Add(saveButton);

            // Add controls to main panel
            mainPanel.Controls.Add(enabledCheckbox, 0, 0);
            mainPanel.SetColumnSpan(enabledCheckbox, 2);

            mainPanel.Controls.Add(intervalLabel, 0, 1);
            mainPanel.Controls.Add(intervalNumeric, 1, 1);

            mainPanel.Controls.Add(priorityLabel, 0, 2);
            mainPanel.Controls.Add(priorityComboBox, 1, 2);

            mainPanel.Controls.Add(daysLabel, 0, 3);
            mainPanel.Controls.Add(daysPanel, 1, 3);

            mainPanel.Controls.Add(startTimeLabel, 0, 4);
            mainPanel.Controls.Add(startTimePicker, 1, 4);

            mainPanel.Controls.Add(endTimeLabel, 0, 5);
            mainPanel.Controls.Add(endTimePicker, 1, 5);

            // Add a summary section
            var summaryLabel = new Label
            {
                Text = "Schedule Summary:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(5)
            };

            var summaryTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Height = 80,
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                BackColor = SystemColors.Info,
                ForeColor = SystemColors.InfoText,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Update summary when any control changes
            EventHandler updateSummary = (s, e) => UpdateSummary(summaryTextBox);
            enabledCheckbox.CheckedChanged += updateSummary;
            intervalNumeric.ValueChanged += updateSummary;
            priorityComboBox.SelectedIndexChanged += updateSummary;
            startTimePicker.ValueChanged += updateSummary;
            endTimePicker.ValueChanged += updateSummary;

            // Initial summary update
            UpdateSummary(summaryTextBox);

            mainPanel.Controls.Add(summaryLabel, 0, 6);
            mainPanel.Controls.Add(summaryTextBox, 1, 6);

            mainPanel.Controls.Add(buttonsPanel, 0, 8);
            mainPanel.SetColumnSpan(buttonsPanel, 2);

            // Add header at the top
            mainPanel.Controls.Add(headerLabel, 0, 0);
            mainPanel.SetColumnSpan(headerLabel, 2);

            this.Controls.Add(mainPanel);
            this.AcceptButton = saveButton;
            this.CancelButton = cancelButton;
        }

        private void LoadScheduleSettings()
        {
            // Settings are loaded in InitializeComponent
        }

        private void UpdateSummary(TextBox summaryTextBox)
        {
            var schedule = _router.Schedule;
            var summary = new StringBuilder();

            if (!schedule.Enabled)
            {
                summary.AppendLine("Schedule is disabled. This router will not be checked automatically.");
            }
            else
            {
                summary.AppendLine($"Router will be checked every {schedule.IntervalMinutes} minutes");

                // Format days
                var days = schedule.ActiveDays.Count == 7
                    ? "every day"
                    : string.Join(", ", schedule.ActiveDays.Select(d => d.ToString().Substring(0, 3)));

                summary.AppendLine($"Active on: {days}");
                summary.AppendLine($"Between: {schedule.StartTime.ToString(@"hh\:mm")} and {schedule.EndTime.ToString(@"hh\:mm")}");
                summary.AppendLine($"Priority: {schedule.Priority}");

                // Calculate next run time
                var nextRun = schedule.NextScheduledRun;
                var timeUntilNextRun = nextRun - DateTime.Now;

                if (timeUntilNextRun.TotalMinutes < 0)
                {
                    summary.AppendLine("Next check: Due now");
                }
                else
                {
                    summary.AppendLine($"Next check: {nextRun:yyyy-MM-dd HH:mm:ss} (in {(int)timeUntilNextRun.TotalMinutes} min)");
                }
            }

            summaryTextBox.Text = summary.ToString();
        }
    }
}
