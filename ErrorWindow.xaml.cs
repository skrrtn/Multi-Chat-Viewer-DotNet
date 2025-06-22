using System;
using System.Windows;
using System.Windows.Controls;

namespace TwitchChatViewer
{
    public partial class ErrorWindow : Window
    {
        public ErrorWindow(string errorDetails)
        {
            InitializeComponent();
            
            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);
            
            ErrorTextBox.Text = errorDetails;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ErrorTextBox.Text);
                // Temporarily change button text to show it worked
                var button = sender as Button;
                var originalText = button.Content.ToString();
                button.Content = "Copied!";
                
                // Reset button text after 1 second
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += (s, args) =>
                {
                    button.Content = originalText;
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
