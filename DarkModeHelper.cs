using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MultiChatViewer
{
    /// <summary>
    /// Utility class to enable dark mode title bar on Windows 10/11
    /// </summary>
    public static class DarkModeHelper
    {        [DllImport("dwmapi.dll", PreserveSig = true)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "LibraryImport not suitable for this Windows-specific P/Invoke")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054", Justification = "LibraryImport not suitable for this Windows-specific P/Invoke")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int leftWidth;
            public int rightWidth;
            public int topHeight;
            public int bottomHeight;
        }

        private const uint DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const uint DWMWCP_ROUND = 2;

        /// <summary>
        /// Enables dark mode title bar for the specified window
        /// </summary>
        /// <param name="window">The WPF window to apply dark mode to</param>
        public static void EnableDarkMode(Window window)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var hwnd = windowHelper.Handle;

                if (hwnd == IntPtr.Zero)
                {
                    // Window handle not available yet, hook into SourceInitialized event
                    window.SourceInitialized += (sender, args) =>
                    {
                        var handle = new WindowInteropHelper(window).Handle;
                        ApplyDarkMode(handle);
                    };
                }
                else
                {
                    ApplyDarkMode(hwnd);
                }
            }
            catch (Exception)
            {
                // Ignore errors - dark mode is not critical functionality
            }
        }

        private static void ApplyDarkMode(IntPtr hwnd)
        {
            try
            {
                // Enable dark mode title bar
                int darkMode = 1;
                
                // Try the newer attribute first (Windows 11)
                int result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                
                // If that fails, try the older attribute (Windows 10)
                if (result != 0)                {
                    _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                }

                // Enable rounded corners if supported (Windows 11)
                int cornerPreference = (int)DWMWCP_ROUND;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch (Exception)
            {
                // Ignore errors - dark mode is not critical functionality
            }
        }
    }
}
