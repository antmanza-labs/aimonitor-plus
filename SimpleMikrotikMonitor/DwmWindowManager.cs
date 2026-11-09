using System;
using System.Runtime.InteropServices;

namespace SimpleMikrotikMonitor
{
    /// <summary>
    /// Provides functionality to interact with the Desktop Window Manager (DWM) API
    /// for modern Windows styling, particularly for dark mode title bars.
    /// </summary>
    public class DwmWindowManager
    {
        // DWM Window Attribute enumeration
        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20, // Windows 10 1809 or later
        }

        // Import DwmSetWindowAttribute from dwmapi.dll
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(
            IntPtr hwnd,
            DWMWINDOWATTRIBUTE attribute,
            ref int pvAttribute,
            uint cbAttribute);

        /// <summary>
        /// Sets a Desktop Window Manager (DWM) attribute for the specified window.
        /// </summary>
        /// <param name="hwnd">The window handle.</param>
        /// <param name="attribute">The attribute to set.</param>
        /// <param name="attributeValue">The value to set (1 for enabled, 0 for disabled).</param>
        public void SetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, int attributeValue)
        {
            try
            {
                int value = attributeValue;
                DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            }
            catch (Exception)
            {
                // Silently fail if the API is not available (older Windows versions)
            }
        }
    }
}
