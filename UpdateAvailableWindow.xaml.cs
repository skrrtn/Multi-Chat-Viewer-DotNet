using System.Diagnostics;
using System.Windows;

namespace TwitchChatViewer
{
    public partial class UpdateAvailableWindow : Window
    {
        private readonly string _releaseUrl;
        public UpdateAvailableWindow(string currentVersion, string latestVersion, string releaseUrl)
        {
            InitializeComponent();
            _releaseUrl = releaseUrl;
            CurrentVersionTextBlock.Text = $"Current version: {currentVersion}";
            LatestVersionTextBlock.Text = $"Latest version: {latestVersion}";
        }

        private void OpenReleasePage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _releaseUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show("Unable to open the release page.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
