using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public partial class UserMessagesWindow : Window, INotifyPropertyChanged
    {
        private readonly UserMessageLookupService _userMessageService;
        private readonly ILogger<UserMessagesWindow> _logger;
        private readonly string _username;        private readonly string _currentChannel;

        public ObservableCollection<UserChannelInfo> AvailableChannels { get; } = [];
        public ObservableCollection<MessageDisplayItem> DisplayItems { get; } = [];

        public event PropertyChangedEventHandler PropertyChanged;

        public UserMessagesWindow(
            UserMessageLookupService userMessageService, 
            ILogger<UserMessagesWindow> logger,
            string username, 
            string currentChannel = null)
        {
            InitializeComponent();
            
            _userMessageService = userMessageService ?? throw new ArgumentNullException(nameof(userMessageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _currentChannel = currentChannel;

            DataContext = this;

            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);

            // Set window title
            Title = $"Messages for {_username}";
            UserTitleTextBlock.Text = $"Messages for {_username}";

            // Bind collections
            ChannelComboBox.ItemsSource = AvailableChannels;
            MessagesListBox.ItemsSource = DisplayItems;

            // Initialize data
            _ = InitializeAsync();

            _logger.LogInformation("User Messages window opened for user: {Username}", _username);
        }

        private async Task InitializeAsync()
        {
            try
            {
                ShowLoading(true);
                
                // Load available channels for this user
                await LoadAvailableChannelsAsync();
                
                // Select current channel if provided, otherwise select first channel
                if (!string.IsNullOrEmpty(_currentChannel))
                {
                    var currentChannelInfo = AvailableChannels.FirstOrDefault(c => 
                        string.Equals(c.ChannelName, _currentChannel, StringComparison.OrdinalIgnoreCase));
                    if (currentChannelInfo != null)
                    {
                        ChannelComboBox.SelectedItem = currentChannelInfo;
                    }
                }
                else if (AvailableChannels.Count > 0)
                {
                    ChannelComboBox.SelectedItem = AvailableChannels[0];
                }

                // Load messages for selected channel
                if (ChannelComboBox.SelectedItem != null)
                {
                    await LoadMessagesForChannelAsync((UserChannelInfo)ChannelComboBox.SelectedItem);
                }
                
                ShowLoading(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing user messages window for user: {Username}", _username);
                ShowError("Error loading user messages. Please try again.");
            }
        }

        private async Task LoadAvailableChannelsAsync()
        {
            try
            {
                var channels = await _userMessageService.GetChannelsForUserAsync(_username);
                
                AvailableChannels.Clear();
                foreach (var channel in channels.OrderByDescending(c => c.MessageCount))
                {
                    AvailableChannels.Add(channel);
                }

                UpdateStats();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading available channels for user: {Username}", _username);
                throw;
            }
        }

        private async Task LoadMessagesForChannelAsync(UserChannelInfo channelInfo)
        {
            try
            {
                ShowLoading(true);
                var messages = await _userMessageService.GetUserMessagesFromChannelAsync(_username, channelInfo.ChannelName);
                
                DisplayItems.Clear();
                
                // Group messages by date and add dividers
                var sortedMessages = messages.OrderByDescending(m => m.Timestamp).ToList();
                DateTime? lastDate = null;
                var today = DateTime.Today;
                
                foreach (var message in sortedMessages)
                {
                    var messageDate = message.Timestamp.Date;
                    
                    // Add date divider if this is a new day (but skip today)
                    if (lastDate == null || lastDate.Value != messageDate)
                    {
                        // Only add divider if it's not today
                        if (messageDate != today)
                        {
                            DisplayItems.Add(new DateDividerItem(messageDate));
                        }
                        lastDate = messageDate;
                    }
                    
                    // Add the message
                    DisplayItems.Add(new ChatMessageDisplayItem(message));
                }

                // Show appropriate UI state
                if (DisplayItems.Count == 0)
                {
                    NoMessagesTextBlock.Visibility = Visibility.Visible;
                    MessagesListBox.Visibility = Visibility.Collapsed;
                }
                else
                {
                    NoMessagesTextBlock.Visibility = Visibility.Collapsed;
                    MessagesListBox.Visibility = Visibility.Visible;
                    
                    // Scroll to top to show most recent messages (newest first)
                    if (MessagesListBox.Items.Count > 0)
                    {
                        MessagesListBox.ScrollIntoView(MessagesListBox.Items[0]);
                    }
                }

                ShowLoading(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading messages for user {Username} in channel {Channel}", _username, channelInfo.ChannelName);
                ShowError($"Error loading messages from {channelInfo.ChannelName}. Please try again.");
            }
        }

        private void UpdateStats()
        {
            var totalMessages = AvailableChannels.Sum(c => c.MessageCount);
            var channelCount = AvailableChannels.Count;
            
            StatsTextBlock.Text = $"Found {totalMessages:N0} messages across {channelCount} channel{(channelCount != 1 ? "s" : "")}";
        }

        private void ShowLoading(bool isLoading)
        {
            LoadingTextBlock.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            MessagesListBox.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
            NoMessagesTextBlock.Visibility = Visibility.Collapsed;
            
            RefreshButton.IsEnabled = !isLoading;
            ChannelComboBox.IsEnabled = !isLoading;
        }

        private void ShowError(string message)
        {
            ShowLoading(false);
            StatsTextBlock.Text = message;
            StatsTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
        }

        private async void ChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChannelComboBox.SelectedItem is UserChannelInfo selectedChannel)
            {
                await LoadMessagesForChannelAsync(selectedChannel);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await InitializeAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Mention click event handler for opening user messages window for mentioned user
        private void HighlightedTextBlock_MentionClick(object sender, RoutedEventArgs e)
        {
            if (e is MentionClickEventArgs mentionArgs && !string.IsNullOrEmpty(mentionArgs.MentionedUsername))
            {
                try
                {                    _logger.LogInformation("Opening user messages window for mentioned user: {Username}", mentionArgs.MentionedUsername);
                    
                    // Create another UserMessagesWindow for the mentioned user
                    var userMessagesWindow = new UserMessagesWindow(
                        _userMessageService,
                        _logger,
                        mentionArgs.MentionedUsername,
                        null // No specific channel filter for mentioned user
                    )
                    {
                        Owner = this.Owner // Set the same owner as this window
                    };
                    
                    userMessagesWindow.Show();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening user messages window for mentioned user: {Username}", mentionArgs.MentionedUsername);
                    System.Windows.MessageBox.Show(
                        $"Error opening user messages window for {mentionArgs.MentionedUsername}.\n\nError: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }

        // Username click event handler for opening user messages window for message author
        private void HighlightedTextBlock_UsernameClick(object sender, RoutedEventArgs e)
        {
            if (e is UsernameClickEventArgs usernameArgs && !string.IsNullOrEmpty(usernameArgs.Username))
            {
                try
                {
                    _logger.LogInformation("Opening user messages window for message author: {Username}", usernameArgs.Username);
                    
                    // Create another UserMessagesWindow for the username that was clicked
                    var userMessagesWindow = new UserMessagesWindow(
                        _userMessageService,
                        _logger,
                        usernameArgs.Username,
                        null // No specific channel filter for clicked user
                    )
                    {
                        Owner = this.Owner // Set the same owner as this window
                    };
                    
                    userMessagesWindow.Show();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening user messages window for user: {Username}", usernameArgs.Username);
                    System.Windows.MessageBox.Show(
                        $"Error opening user messages window for {usernameArgs.Username}.\n\nError: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosed(EventArgs e)
        {
            _logger.LogInformation("User Messages window closed for user: {Username}", _username);
            base.OnClosed(e);
        }
    }

    // Data models for the user messages window
    public class UserChannelInfo
    {
        public string ChannelName { get; set; }
        public int MessageCount { get; set; }
        public DateTime LastMessageTime { get; set; }
    }

    public class ChatMessageWithChannel : ChatMessage
    {
        public string ChannelName { get; set; }
    }
}
