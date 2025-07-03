using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{    public partial class FollowedChannelsWindow : Window, INotifyPropertyChanged
    {
        private readonly MultiChannelManager _channelManager;
        private readonly ILogger<FollowedChannelsWindow> _logger;
        private readonly DispatcherTimer _updateTimer;
        
        public ObservableCollection<FollowedChannel> FollowedChannels { get; } = [];
        public event EventHandler<FollowedChannel> SwitchToChannelRequested;        public FollowedChannelsWindow(MultiChannelManager channelManager, ILogger<FollowedChannelsWindow> logger)
        {
            InitializeComponent();
            DataContext = this;
            
            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);
            
            _channelManager = channelManager;
            _logger = logger;

            ChannelsDataGrid.ItemsSource = FollowedChannels;

            // Initialize timer for updating relative times
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10) // Update every 10 seconds
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // Subscribe to channel manager events
            _channelManager.ChannelConnected += OnChannelConnected;
            _channelManager.ChannelDisconnected += OnChannelDisconnected;
            _channelManager.ChannelError += OnChannelError;
            _channelManager.MessageReceived += OnMessageReceived;

            // Subscribe to window events to refresh channels when window is shown
            this.Activated += FollowedChannelsWindow_Activated;
            
            _logger.LogInformation("Followed Channels window initialized");
        }

        private void FollowedChannelsWindow_Activated(object sender, EventArgs e)
        {
            RefreshChannelsList();
        }        private async void RefreshChannelsList()
        {
            FollowedChannels.Clear();
              // Update database stats first
            await _channelManager.UpdateDatabaseStatsAsync();
            
            var existingChannels = _channelManager.GetFollowedChannels()
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var channel in existingChannels)
            {
                FollowedChannels.Add(channel);
            }            UpdateStatus($"Loaded {existingChannels.Count} followed channels");
        }

        private async void AddChannelButton_Click(object sender, RoutedEventArgs e)
        {
            await AddChannelAsync();
        }

        private async void ChannelNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AddChannelAsync();
            }
        }        private async Task AddChannelAsync()
        {
            var channelName = ChannelNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(channelName))
            {
                MessageBox.Show("Please enter a channel name.", "Invalid Input", 
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get selected platform
            var selectedPlatform = Platform.Twitch; // Default
            if (PlatformComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (Enum.TryParse<Platform>(selectedItem.Tag?.ToString(), out var platform))
                {
                    selectedPlatform = platform;
                }
            }

            try
            {
                UpdateStatus($"Adding {selectedPlatform} channel: {channelName}...");
                  // First check if channel already exists on this platform
                var normalizedChannel = channelName.ToLower().Replace("#", "");
                var existingChannels = _channelManager.GetFollowedChannels();
                var existingChannel = existingChannels.FirstOrDefault(c => 
                    c.Name.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase) && 
                    c.Platform == selectedPlatform);
                
                if (existingChannel != null)
                {
                    UpdateStatus($"{selectedPlatform} channel {channelName} is already being followed");
                    MessageBox.Show($"{selectedPlatform} channel '{channelName}' is already being followed.", 
                                   "Channel Already Added", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Refresh the UI to make sure it shows up
                    if (!FollowedChannels.Contains(existingChannel))
                    {
                        FollowedChannels.Add(existingChannel);
                    }
                    
                    ChannelNameTextBox.Clear();
                    return;
                }                _logger.LogInformation("Attempting to add new {Platform} channel: {Channel}", selectedPlatform, normalizedChannel);                try
                {
                    var result = await _channelManager.AddChannelAsync(channelName, selectedPlatform);
                      if (result)
                    {
                        var followedChannel = _channelManager.GetFollowedChannels()
                            .FirstOrDefault(c => c.Name.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase) && 
                                                c.Platform == selectedPlatform);
                        
                        if (followedChannel != null && !FollowedChannels.Contains(followedChannel))
                        {
                            FollowedChannels.Add(followedChannel);
                        }
                        
                        ChannelNameTextBox.Clear();
                        
                        // Check if the channel connected successfully or is in retry mode
                        if (followedChannel?.IsConnected == true)
                        {
                            UpdateStatus($"Successfully added and connected to {selectedPlatform} channel: {channelName}");
                        }
                        else
                        {
                            UpdateStatus($"Added {selectedPlatform} channel: {channelName} (connecting in background)");
                            
                            // Try to reconnect after a short delay
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(2000); // Wait 2 seconds
                                await _channelManager.RetryConnectionAsync(normalizedChannel);
                            });
                        }
                        
                        _logger.LogInformation("Successfully added {Platform} channel: {Channel}", selectedPlatform, normalizedChannel);
                    }                    else
                    {
                        UpdateStatus($"Failed to add channel: {channelName}");
                        _logger.LogWarning("Failed to add channel: {Channel} (unknown error)", normalizedChannel);
                        MessageBox.Show($"Failed to add channel '{channelName}'. Please check the channel name and your connection, then try again.",
                            "Add Channel Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception addEx)
                {
                    _logger.LogError(addEx, "Exception occurred while adding channel: {Channel}", normalizedChannel);
                    UpdateStatus($"Error adding channel: {channelName}");
                    
                    // Provide more specific error messages for Kick channels
                    string errorMessage;
                    if (selectedPlatform == Platform.Kick)
                    {
                        if (addEx.Message.Contains("not found"))
                        {
                            errorMessage = $"Failed to add Kick channel '{channelName}':\n\n" +
                                         $"The channel '{channelName}' was not found on Kick.com.\n" +
                                         $"Please check that:\n" +
                                         $"• The channel name is spelled correctly\n" +
                                         $"• The channel exists on Kick.com\n" +
                                         $"• The channel is not private or restricted\n\n" +
                                         $"Error details: {addEx.Message}";
                        }
                        else
                        {
                            errorMessage = $"Failed to add Kick channel '{channelName}':\n\n" +
                                         $"{addEx.Message}\n\n" +
                                         $"Please check your internet connection and try again.\n\n" +
                                         $"Note: Kick OAuth credentials are only needed for sending messages, not for reading chat.";
                        }
                    }
                    else
                    {
                        errorMessage = $"An error occurred while adding channel '{channelName}':\n\n{addEx.Message}\n\n" +
                                     $"Please check your connection and try again.";
                    }
                    
                    MessageBox.Show(errorMessage, "Add Channel Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding channel: {Channel}", channelName);
                UpdateStatus($"Error adding channel: {channelName}");
                MessageBox.Show($"Error adding channel '{channelName}':\n\n{ex.Message}\n\n" +
                               $"Please check your internet connection and try again.", 
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }private async void RemoveChannelButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedChannels = ChannelsDataGrid.SelectedItems.Cast<FollowedChannel>().ToList();
            if (selectedChannels.Count == 0)
            {
                MessageBox.Show("Please select one or more channels to remove.", "No Selection", 
                               MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }            var channelNames = string.Join(", ", selectedChannels.Select(c => $"'{c.Name}'"));
            var channelWord = selectedChannels.Count == 1 ? "channel" : "channels";            var result = MessageBox.Show($"⚠️ WARNING: This will permanently delete all data! ⚠️\n\n" +
                                        $"Are you sure you want to remove {selectedChannels.Count} {channelWord}?\n\n" +
                                        $"Channels: {channelNames}\n\n" +
                                        $"This action will:\n" +
                                        $"• Stop monitoring these channels\n" +
                                        $"• DELETE the entire database file for each channel\n" +
                                        $"• Remove all stored chat messages permanently\n" +
                                        $"• Remove all associated data permanently\n\n" +
                                        $"This operation CANNOT be undone!", 
                                        "⚠️ Confirm Permanent Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                int successCount = 0;
                int failureCount = 0;
                var errors = new List<string>();                foreach (var selectedChannel in selectedChannels)
                {                    try
                    {
                        UpdateStatus($"Removing channel: {selectedChannel.Name}... ({successCount + failureCount + 1}/{selectedChannels.Count})");
                        var success = await _channelManager.RemoveChannelAsync(selectedChannel.Name);
                        
                        if (success)
                        {
                            FollowedChannels.Remove(selectedChannel);
                            successCount++;
                        }
                        else
                        {
                            failureCount++;
                            errors.Add($"{selectedChannel.Name}: Remove operation failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        errors.Add($"{selectedChannel.Name}: {ex.Message}");
                        _logger.LogError(ex, "Error removing channel: {Channel}", selectedChannel.Name);
                    }
                }

                // Update status and show results
                UpdateStatus($"Remove operation completed: {successCount} successful, {failureCount} failed");
                
                if (failureCount > 0)
                {
                    var resultMessage = $"Remove operation completed:\n\n" +
                                       $"✓ Successfully removed: {successCount} {(successCount == 1 ? "channel" : "channels")}\n" +
                                       $"✗ Failed to remove: {failureCount} {(failureCount == 1 ? "channel" : "channels")}\n\n";
                    
                    if (errors.Count > 0)
                    {
                        resultMessage += "Errors:\n" + string.Join("\n", errors);
                    }

                    MessageBox.Show(resultMessage, "Remove Results", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    UpdateStatus($"Successfully removed {successCount} {channelWord}");
                }
            }
        }        private void ViewInMainButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is FollowedChannel selectedChannel)
            {
                SwitchToChannelRequested?.Invoke(this, selectedChannel);
                UpdateStatus($"Switching main window to channel: {selectedChannel.Name}");
            }
        }

        private void OnChannelConnected(object sender, string channel)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus($"Channel connected: {channel}");
            });
        }        private void OnChannelDisconnected(object sender, string channel)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus($"Channel disconnected: {channel}");
                
                // Find the channel in our collection and force a refresh
                var followedChannel = FollowedChannels.FirstOrDefault(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
                if (followedChannel != null)
                {
                    // Trigger property change notifications to update the UI
                    followedChannel.OnPropertyChanged(nameof(followedChannel.IsConnected));
                    followedChannel.OnPropertyChanged(nameof(followedChannel.ConnectionStatus));
                    followedChannel.OnPropertyChanged(nameof(followedChannel.StatusColor));
                    followedChannel.OnPropertyChanged(nameof(followedChannel.Status));
                }
                
                // Refresh the entire DataGrid to ensure changes are visible
                ChannelsDataGrid.Items.Refresh();
            });
        }

        private void OnChannelError(object sender, (string Channel, string Error) args)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus($"Channel error ({args.Channel}): {args.Error}");
            });
        }

        private void OnMessageReceived(object sender, (string Channel, ChatMessage Message) args)
        {
            // Messages are automatically handled by the MultiChannelManager
            // This event can be used for additional processing if needed
        }        private void UpdateStatus(string message)
        {
            // Status bar has been removed - optionally log status messages
            _logger.LogInformation("Status: {Message}", message);
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Refresh the display to update relative time formatting
            foreach (var channel in FollowedChannels)
            {
                channel.OnPropertyChanged(nameof(channel.LastMessageTimeFormatted));
            }
        }        protected override void OnClosing(CancelEventArgs e)
        {
            // Stop the timer
            _updateTimer?.Stop();
            
            // Unsubscribe from events
            _channelManager.ChannelConnected -= OnChannelConnected;
            _channelManager.ChannelDisconnected -= OnChannelDisconnected;
            _channelManager.ChannelError -= OnChannelError;
            _channelManager.MessageReceived -= OnMessageReceived;
            
            base.OnClosing(e);
        }        private async void EraseSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedChannels = ChannelsDataGrid.SelectedItems.Cast<FollowedChannel>().ToList();
            if (selectedChannels.Count == 0)
            {
                MessageBox.Show("Please select one or more channels to erase messages from.", "No Selection", 
                               MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var channelNames = string.Join(", ", selectedChannels.Select(c => $"'{c.Name}'"));
            var channelWord = selectedChannels.Count == 1 ? "channel" : "channels";
            
            var result = MessageBox.Show($"Are you sure you want to erase ALL messages from {selectedChannels.Count} {channelWord}?\n\n" +
                                        $"Channels: {channelNames}\n\n" +
                                        "This will permanently delete all chat history for these channels but keep the database files.\n" +
                                        "This action cannot be undone!", 
                                        "Confirm Erase All Messages", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                int successCount = 0;
                int failureCount = 0;
                var errors = new List<string>();

                foreach (var selectedChannel in selectedChannels)
                {
                    try
                    {
                        UpdateStatus($"Erasing all messages from channel: {selectedChannel.Name}... ({successCount + failureCount + 1}/{selectedChannels.Count})");
                        
                        // Use the static method to clear messages for any channel
                        await ChatDatabaseService.ClearAllMessagesForChannelAsync(selectedChannel.Name, selectedChannel.Platform, _logger);
                          // Update the channel's properties
                        selectedChannel.MessageCount = 0;
                        selectedChannel.DatabaseSize = 0;
                        selectedChannel.LastMessageTime = default;
                        
                        successCount++;
                        _logger.LogInformation("Successfully erased messages from channel: {Channel}", selectedChannel.Name);
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        var errorMsg = $"{selectedChannel.Name}: {ex.Message}";
                        errors.Add(errorMsg);
                        _logger.LogError(ex, "Error erasing messages from channel: {Channel}", selectedChannel.Name);
                    }
                }

                // Update status and show results
                UpdateStatus($"Erase operation completed: {successCount} successful, {failureCount} failed");
                
                var resultMessage = $"Erase operation completed:\n\n" +
                                   $"✓ Successfully erased: {successCount} {(successCount == 1 ? "channel" : "channels")}\n";
                
                if (failureCount > 0)
                {
                    resultMessage += $"✗ Failed to erase: {failureCount} {(failureCount == 1 ? "channel" : "channels")}\n\n";
                    if (errors.Count > 0)
                    {
                        resultMessage += "Errors:\n" + string.Join("\n", errors);
                    }
                }

                if (successCount > 0)
                {
                    resultMessage += "\nThe database files have been kept and new messages will be logged normally.";
                }

                var messageBoxIcon = failureCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning;
                MessageBox.Show(resultMessage, "Erase Results", MessageBoxButton.OK, messageBoxIcon);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
