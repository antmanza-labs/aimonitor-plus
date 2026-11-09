using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SimpleMikrotikMonitor.Controls
{
    public class DashboardWidget : UserControl
    {
        private Label _titleLabel;
        private Label _statusLabel;
        private Label _lastCheckLabel;
        private Label _uptimeLabel;
        private PictureBox _statusIcon;
        private RouterConfig _router;
        private bool _isOnline;

        public DashboardWidget(RouterConfig router)
        {
            _router = router;
            InitializeComponent();
            UpdateInfo(false, "Not checked", "Unknown");
        }

        private void InitializeComponent()
        {
            // Set control properties
            Size = new Size(200, 150);
            BorderStyle = BorderStyle.None;
            Margin = new Padding(5);
            Padding = new Padding(1);

            // Create status icon
            _statusIcon = new PictureBox
            {
                Size = new Size(16, 16),
                Location = new Point(10, 10),
                SizeMode = PictureBoxSizeMode.StretchImage
            };

            // Create labels
            _titleLabel = new Label
            {
                Text = _router.Name,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                Location = new Point(35, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _statusLabel = new Label
            {
                Location = new Point(10, 40),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _lastCheckLabel = new Label
            {
                Location = new Point(10, 70),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _uptimeLabel = new Label
            {
                Location = new Point(10, 100),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            // Add controls
            Controls.AddRange(new Control[] { _statusIcon, _titleLabel, _statusLabel, _lastCheckLabel, _uptimeLabel });

            // Set initial status icon
            UpdateStatusIcon(false);
        }

        public void UpdateInfo(bool isOnline, string lastCheck, string uptime)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateInfo(isOnline, lastCheck, uptime)));
                return;
            }

            _isOnline = isOnline;
            UpdateStatusIcon(isOnline);
            _statusLabel.Text = $"Status: {(isOnline ? "Online" : "Offline")}";
            _lastCheckLabel.Text = $"Last Check: {lastCheck}";
            _uptimeLabel.Text = $"Uptime: {uptime}";
        }

        private void UpdateStatusIcon(bool isOnline)
        {
            _statusIcon.Image?.Dispose();
            _statusIcon.Image = isOnline ?
                SystemIcons.Information.ToBitmap() :
                SystemIcons.Error.ToBitmap();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Define colors based on online status (light theme only)
            Color startColor, endColor;

            // Light theme colors with transparency
            startColor = _isOnline ? Color.FromArgb(180, 240, 255, 240) : Color.FromArgb(180, 255, 240, 240);
            endColor = _isOnline ? Color.FromArgb(180, 220, 255, 220) : Color.FromArgb(180, 255, 220, 220);

            // Update text color for light mode
            _titleLabel.ForeColor = Color.Black;
            _statusLabel.ForeColor = Color.Black;
            _lastCheckLabel.ForeColor = Color.Black;
            _uptimeLabel.ForeColor = Color.Black;

            // Create a rectangle that's slightly smaller than the client rectangle to account for border
            Rectangle innerRect = new Rectangle(
                ClientRectangle.X + 1,
                ClientRectangle.Y + 1,
                ClientRectangle.Width - 2,
                ClientRectangle.Height - 2);

            // Add a subtle gradient background
            using (var brush = new LinearGradientBrush(
                innerRect,
                startColor,
                endColor,
                LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(brush, innerRect);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statusIcon.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}