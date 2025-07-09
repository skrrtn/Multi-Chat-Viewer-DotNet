using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace MultiChatViewer
{
    public partial class AboutWindow : Window
    {        public AboutWindow()
        {
            InitializeComponent();
            
            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);
            
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                VersionTextBlock.Text = $"Version {version}";
            }            catch (Exception)
            {
                // Fallback to default version if unable to read assembly version
                VersionTextBlock.Text = "Version 1.0.0.0";
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Use the default system browser to open the URL
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.ToString(),
                    UseShellExecute = true
                });
                e.Handled = true;
            }            catch (Exception ex)
            {
                // If opening the URL fails, show an error message
                System.Windows.MessageBox.Show($"Unable to open link: {e.Uri}\n\nError: {ex.Message}", 
                              "Error",                              MessageBoxButton.OK, 
                              MessageBoxImage.Warning);
            }
        }
    }
}
