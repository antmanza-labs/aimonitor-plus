using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SimpleMikrotikMonitor
{
    // Custom color table for dark theme menu rendering
    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(65, 65, 68);
        public override Color MenuItemBorder => Color.FromArgb(60, 60, 60);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(65, 65, 68);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(65, 65, 68);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(65, 65, 68);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(65, 65, 68);
        public override Color MenuBorder => Color.FromArgb(60, 60, 60);
        public override Color MenuStripGradientBegin => Color.FromArgb(30, 30, 30);
        public override Color MenuStripGradientEnd => Color.FromArgb(30, 30, 30);
        public override Color ToolStripDropDownBackground => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientBegin => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientEnd => Color.FromArgb(30, 30, 30);
    }
    // Only Light theme is supported
    public enum ThemeMode
    {
        Light
    }

    public static class ThemeManager
    {
        // Theme colors - only using light theme
        public static readonly Color LightBackColor = SystemColors.Control;
        public static readonly Color LightForeColor = SystemColors.ControlText;
        public static readonly Color LightMenuBackColor = SystemColors.Menu;
        public static readonly Color LightMenuForeColor = SystemColors.MenuText;
        public static readonly Color LightTextBoxBackColor = SystemColors.Window;
        public static readonly Color LightTextBoxForeColor = SystemColors.WindowText;
        public static readonly Color LightBorderColor = SystemColors.ControlDark;

        public static ThemeMode CurrentTheme { get; private set; } = ThemeMode.Light;

        public static void ApplyTheme(Control control, ThemeMode theme)
        {
            // Only light theme is supported
            CurrentTheme = ThemeMode.Light;

            if (control is Form form)
            {
                // Apply theme to the form itself
                ApplyThemeToControl(form, ThemeMode.Light);

                // Apply theme to menus
                ApplyThemeToMenus(form, ThemeMode.Light);

                // Apply theme to all toolstrips
                foreach (var toolStrip in GetAllControls<ToolStrip>(form))
                {
                    ApplyThemeToToolStrip(toolStrip, ThemeMode.Light);
                }

                // Apply theme to status strip if present
                foreach (var statusStrip in GetAllControls<StatusStrip>(form))
                {
                    ApplyThemeToToolStrip(statusStrip, ThemeMode.Light);
                }
            }
            else
            {
                // For non-form controls
                ApplyThemeToControl(control, ThemeMode.Light);
            }
        }

        private static void ApplyThemeToControl(Control control, ThemeMode theme)
        {
            // Set colors for light theme only
            Color backColor = LightBackColor;
            Color foreColor = LightForeColor;
            Color textBoxBackColor = LightTextBoxBackColor;
            Color textBoxForeColor = LightTextBoxForeColor;
            Color borderColor = LightBorderColor;

            // Special handling for DashboardWidget
            if (control is Controls.DashboardWidget dashboardWidget)
            {
                // We'll handle the dashboard widget's children manually
                // Create a custom border by setting a light background color
                control.BackColor = Color.FromArgb(240, 240, 240);

                // Apply theme to all child controls
                foreach (Control child in control.Controls)
                {
                    if (child is Label label)
                    {
                        label.BackColor = Color.FromArgb(240, 240, 240);
                        label.ForeColor = foreColor;
                    }
                    else
                    {
                        ApplyThemeToControl(child, theme);
                    }
                }

                return;
            }

            // Apply to the control
            control.BackColor = backColor;
            control.ForeColor = foreColor;

            // Set border color for controls with borders
            // Check for specific control types that have BorderStyle property
            if (control is Panel panel)
            {
                // No special handling for panels in light theme
            }
            else if (control is GroupBox groupBox)
            {
                // No special handling for GroupBox in light theme
            }

            // Special handling for text boxes and rich text boxes
            if (control is TextBoxBase textBox)
            {
                textBox.BackColor = textBoxBackColor;
                textBox.ForeColor = textBoxForeColor;

                // No special handling for TextBoxBase in light theme
            }
            else if (control is DataGridView grid)
            {
                grid.BackgroundColor = backColor;
                grid.ForeColor = foreColor;
                grid.GridColor = borderColor;
                grid.DefaultCellStyle.BackColor = textBoxBackColor;
                grid.DefaultCellStyle.ForeColor = textBoxForeColor;
                grid.ColumnHeadersDefaultCellStyle.BackColor = backColor;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = foreColor;
                grid.EnableHeadersVisualStyles = false;

                // No special handling for DataGridView in light theme
            }
            else if (control is Button button)
            {
                button.FlatStyle = FlatStyle.Standard;
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.BackColor = textBoxBackColor;
                comboBox.ForeColor = textBoxForeColor;
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.FlatStyle = FlatStyle.Standard;
            }
            else if (control is RadioButton radioButton)
            {
                radioButton.FlatStyle = FlatStyle.Standard;
            }
            else if (control is GroupBox groupBox)
            {
                groupBox.FlatStyle = FlatStyle.Standard;
            }
            else if (control is TabControl tabControl)
            {
                // Light theme - use default drawing
                tabControl.DrawMode = TabDrawMode.Normal;
                tabControl.BackColor = LightBackColor;
                tabControl.ForeColor = LightForeColor;

                // Apply theme to all TabPages
                foreach (TabPage tabPage in tabControl.TabPages)
                {
                    tabPage.BackColor = LightBackColor;
                    tabPage.ForeColor = LightForeColor;
                    ApplyThemeToControl(tabPage, theme);
                }
            }
            else if (control is FlowLayoutPanel || control is TableLayoutPanel || control is Panel)
            {
                // Apply theme to all child controls
                foreach (Control child in control.Controls)
                {
                    ApplyThemeToControl(child, theme);
                }
            }
        }

        private static void ApplyThemeToMenus(Form form, ThemeMode theme)
        {
            // Find all MenuStrips in the form
            MenuStrip[] menuStrips = form.Controls.OfType<MenuStrip>().ToArray();

            // If no MenuStrips were found directly in the form's Controls collection,
            // try to find them using GetAllControls method
            if (menuStrips.Length == 0)
            {
                menuStrips = GetAllControls<MenuStrip>(form).ToArray();
            }

            foreach (MenuStrip menuStrip in menuStrips)
            {
                // Only light theme is supported
                Color menuBackColor = LightMenuBackColor;
                Color menuForeColor = LightMenuForeColor;

                menuStrip.BackColor = menuBackColor;
                menuStrip.ForeColor = menuForeColor;
                menuStrip.RenderMode = ToolStripRenderMode.System;

                foreach (ToolStripItem item in menuStrip.Items)
                {
                    ApplyThemeToMenuItem(item, theme);
                }
            }
        }

        // Helper method to get all controls of a specific type
        private static IEnumerable<T> GetAllControls<T>(Control control) where T : Control
        {
            var controls = control.Controls.OfType<T>();

            foreach (Control childControl in control.Controls)
            {
                controls = controls.Concat(GetAllControls<T>(childControl));
            }

            return controls;
        }

        private static void ApplyThemeToMenuItem(ToolStripItem item, ThemeMode theme)
        {
            // Only light theme is supported
            Color menuBackColor = LightMenuBackColor;
            Color menuForeColor = LightMenuForeColor;

            item.BackColor = menuBackColor;
            item.ForeColor = menuForeColor;

            if (item is ToolStripMenuItem menuItem)
            {
                foreach (ToolStripItem dropDownItem in menuItem.DropDownItems)
                {
                    ApplyThemeToMenuItem(dropDownItem, theme);
                }
            }
        }

        private static void ApplyThemeToToolStrip(ToolStrip toolStrip, ThemeMode theme)
        {
            // Only light theme is supported
            Color backColor = LightBackColor;
            Color foreColor = LightForeColor;

            toolStrip.BackColor = backColor;
            toolStrip.ForeColor = foreColor;
            toolStrip.RenderMode = ToolStripRenderMode.System;

            // Apply theme to all items
            foreach (ToolStripItem item in toolStrip.Items)
            {
                item.BackColor = backColor;
                item.ForeColor = foreColor;

                // Handle dropdown items
                if (item is ToolStripDropDownItem dropDownItem)
                {
                    foreach (ToolStripItem dropDownSubItem in dropDownItem.DropDownItems)
                    {
                        ApplyThemeToMenuItem(dropDownSubItem, theme);
                    }
                }
            }
        }
    }
}
