using System;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;

namespace SimpleMikrotikMonitor
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeUI();

            // Apply current theme if settings are available
            try
            {
                // Try to get the current theme from the main form
                var mainForm = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
                if (mainForm != null)
                {
                    var settings = mainForm.GetType().GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mainForm) as AppSettings;
                    if (settings != null && settings.UISettings != null)
                    {
                        ThemeManager.ApplyTheme(this, settings.UISettings.Theme);
                    }
                }
            }
            catch
            {
                // If we can't get the theme, just use the default
            }
        }

        private void InitializeUI()
        {
            // Form properties
            Text = "About AIMonitor+";
            Size = new Size(500, 430);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = true;
            ShowInTaskbar = false;

            // Try to load custom logo if it exists
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
                    Icon = SystemIcons.Information;
                }
            }
            else
            {
                Icon = SystemIcons.Information;
            }

            // Create a TableLayoutPanel for organized layout
            var tableLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                RowCount = 7,
                ColumnCount = 1
            };

            // Add rows with specific heights
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104)); // Logo
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // App name
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); // Version
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); // Developer
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); // Website
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); // Copyright
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Buttons

            // Create logo PictureBox
            var logoPictureBox = new PictureBox
            {
                Size = new Size(96, 96),
                SizeMode = PictureBoxSizeMode.Zoom,
                Anchor = AnchorStyles.None,
                Dock = DockStyle.None
            };

            // Try to load the logo
            try
            {
                if (File.Exists(logoPath))
                {
                    // Ask for the large frame; the default 32px frame looks blurry when enlarged.
                    using (var icon = new Icon(logoPath, 128, 128))
                    {
                        logoPictureBox.Image = icon.ToBitmap();
                    }
                }
                else
                {
                    logoPictureBox.Image = SystemIcons.Information.ToBitmap();
                }
            }
            catch
            {
                logoPictureBox.Image = SystemIcons.Information.ToBitmap();
            }

            // Create labels
            var appNameLabel = new Label
            {
                Text = "AIMonitor+",
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            // Get the actual version from the assembly
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionText = version != null ? $"Version {version.Major}.{version.Minor}.{version.Build}" : "Version 2.0.1";

            var versionLabel = new Label
            {
                Text = versionText,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            var developerLabel = new Label
            {
                Text = "Developed by Antmanza Labs",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            var websiteLabel = new Label
            {
                Text = "www.antmanza.co.za",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };

            // Make the website clickable
            websiteLabel.Click += (sender, e) => {
                try {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://www.antmanza.co.za",
                        UseShellExecute = true
                    });
                } catch (Exception ex) {
                    MessageBox.Show($"Could not open website: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            var copyrightLabel = new Label
            {
                Text = "© 2025-" + DateTime.Now.Year.ToString() + " Antmanza Labs. Free to use under the MIT License.\n" +
                      "Provided as-is, without warranty. AI findings can be wrong: check them before acting.\n" +
                      "Not affiliated with MikroTik or Anthropic.",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            // Create OK button
            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.None,
                Width = 80
            };
            okButton.Click += (sender, e) => Close();

            var termsButton = new Button { Text = "Terms of use", AutoSize = true };
            termsButton.Click += (sender, e) => ShowTerms();

            var buttonRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, WrapContents = false };
            buttonRow.Controls.Add(termsButton);
            buttonRow.Controls.Add(okButton);

            // Add controls to the table layout
            tableLayout.Controls.Add(logoPictureBox, 0, 0);
            tableLayout.Controls.Add(appNameLabel, 0, 1);
            tableLayout.Controls.Add(versionLabel, 0, 2);
            tableLayout.Controls.Add(developerLabel, 0, 3);
            tableLayout.Controls.Add(websiteLabel, 0, 4);
            tableLayout.Controls.Add(copyrightLabel, 0, 5);
            tableLayout.Controls.Add(buttonRow, 0, 6);

            // Set control alignment
            tableLayout.SetCellPosition(logoPictureBox, new TableLayoutPanelCellPosition(0, 0));
            tableLayout.SetCellPosition(appNameLabel, new TableLayoutPanelCellPosition(0, 1));
            tableLayout.SetCellPosition(versionLabel, new TableLayoutPanelCellPosition(0, 2));
            tableLayout.SetCellPosition(developerLabel, new TableLayoutPanelCellPosition(0, 3));
            tableLayout.SetCellPosition(websiteLabel, new TableLayoutPanelCellPosition(0, 4));
            tableLayout.SetCellPosition(copyrightLabel, new TableLayoutPanelCellPosition(0, 5));
            tableLayout.SetCellPosition(buttonRow, new TableLayoutPanelCellPosition(0, 6));

            // Center the logo in its cell
            tableLayout.SetColumnSpan(logoPictureBox, 1);
            tableLayout.SetRowSpan(logoPictureBox, 1);
            logoPictureBox.Anchor = AnchorStyles.None;
            tableLayout.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;

            // Add the table layout to the form
            Controls.Add(tableLayout);

            // Set the accept button
            AcceptButton = okButton;
        }

        /// <summary>Shows TERMS.md (embedded at build time from the repo root).</summary>
        private void ShowTerms()
        {
            string text;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SimpleMikrotikMonitor.TERMS.md"))
            {
                text = stream == null ? "Terms file missing from this build." : new StreamReader(stream).ReadToEnd();
            }

            using var dlg = new Form
            {
                Text = "AIMonitor+ - Terms of Use",
                Size = new Size(640, 560),
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Icon = Icon
            };
            var box = new TextBox
            {
                Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10f),
                BackColor = SystemColors.Window
            };
            var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
            dlg.Controls.Add(box);
            dlg.Controls.Add(close);
            dlg.AcceptButton = close;
            dlg.Shown += (_, _) => box.Select(0, 0);
            dlg.ShowDialog(this);
        }
    }
}
