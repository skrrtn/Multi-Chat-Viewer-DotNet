using System;
using System.Windows;

namespace TwitchChatViewer
{
    public partial class KickCredentialsWindow : Window
    {
        public string ClientId { get; private set; }
        public string ClientSecret { get; private set; }
        public bool CredentialsProvided { get; private set; }

        public KickCredentialsWindow()
        {
            InitializeComponent();
            
            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);
            
            // Set focus to client ID textbox
            ClientIdTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var clientId = ClientIdTextBox.Text?.Trim();
            var clientSecret = ClientSecretPasswordBox.Password?.Trim();

            if (string.IsNullOrEmpty(clientId))
            {
                MessageBox.Show("Please enter a Client ID.", "Invalid Input", 
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                ClientIdTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(clientSecret))
            {
                MessageBox.Show("Please enter a Client Secret.", "Invalid Input", 
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                ClientSecretPasswordBox.Focus();
                return;
            }

            ClientId = clientId;
            ClientSecret = clientSecret;
            CredentialsProvided = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CredentialsProvided = false;
            Close();
        }
    }
}
