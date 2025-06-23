using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace TwitchChatViewer
{
    public static class IconHelper
    {        [SupportedOSPlatform("windows6.1")]
        public static Icon GetApplicationIcon()
        {
            try
            {
                // Try to load from WPF Resource first
                var uri = new Uri("pack://application:,,,/logo.ico");
                var resource = System.Windows.Application.GetResourceStream(uri);
                
                if (resource != null)
                {
                    System.Diagnostics.Debug.WriteLine("IconHelper: Successfully loaded logo.ico from WPF resources");
                    return new Icon(resource.Stream);
                }
                
                // Try to get icon from executing assembly
                var assembly = Assembly.GetExecutingAssembly();
                var iconStream = assembly.GetManifestResourceStream("TwitchChatViewer.logo.ico");
                
                if (iconStream != null)
                {
                    System.Diagnostics.Debug.WriteLine("IconHelper: Successfully loaded logo.ico from embedded resources");
                    return new Icon(iconStream);                }
                
                // Try to load from file system as fallback
                var exeDirectory = AppContext.BaseDirectory;
                var iconPath = Path.Combine(exeDirectory, "logo.ico");
                
                if (File.Exists(iconPath))
                {
                    System.Diagnostics.Debug.WriteLine($"IconHelper: Successfully loaded logo.ico from file system at {iconPath}");
                    return new Icon(iconPath);
                }
                
                System.Diagnostics.Debug.WriteLine("IconHelper: Could not find logo.ico, falling back to programmatically created icon");
                // Fallback to creating a simple icon programmatically
                return CreateSimpleIcon();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IconHelper: Exception loading icon: {ex.Message}");
                // Ultimate fallback
                return SystemIcons.Application;
            }
        }
        
        [SupportedOSPlatform("windows6.1")]
        private static Icon CreateSimpleIcon()
        {
            // Create a 16x16 bitmap with a simple design
            using (var bitmap = new Bitmap(16, 16))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // Purple background (Twitch color)
                    graphics.Clear(Color.FromArgb(145, 70, 255));
                    
                    // White "T" for Twitch
                    using (var brush = new SolidBrush(Color.White))
                    using (var font = new Font("Arial", 10, FontStyle.Bold))
                    {
                        var stringFormat = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        graphics.DrawString("T", font, brush, new RectangleF(0, 0, 16, 16), stringFormat);
                    }
                }
                
                // Convert to icon
                IntPtr hIcon = bitmap.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }
    }
}
