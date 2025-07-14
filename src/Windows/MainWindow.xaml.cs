using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows.Forms;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using MultiChatViewer.Services;

namespace MultiChatViewer
{    public partial class MainWindow : Window, INotifyPropertyChanged
    {        private readonly TwitchIrcClient _twitchClient;
        private readonly ChatDatabaseService _databaseService;
        private readonly MultiChannelManager _multiChannelManager;
        private readonly UserFilterService _userFilterService;
        private readonly EmoteService _emoteService;
        private readonly ILogger<MainWindow> _logger;
        private readonly IServiceProvider _serviceProvider;
        private NotifyIcon _notifyIcon;
        private bool _isConnected;
        private string _statusMessage = "No followed channels";
        private string _currentChannel;
        private Platform _currentChannelPlatform = Platform.Twitch;
        private int _currentChannelMessageCount;        private string _currentChannelDatabaseSize = "0 B";
        private double _chatFontSize = 12.0; // Default font size matches BaseFontSize
        private bool _showTimestamps = true; // Default to showing timestamps
        private bool _showEmotes = true; // Default to showing emotes
        private string _scrollButtonText = "📄 Scroll to Top"; // Default scroll button text
        private readonly System.Windows.Threading.DispatcherTimer _statusUpdateTimer = new();
        
        // Streamer Mentions window singleton
        private StreamerMentionsWindow _streamerMentionsWindow;
        private bool _isStreamerMentionsWindowOpen = false;
          // Performance and scroll management
        private const int MAX_MESSAGES_IN_CHAT = 1200;
        private bool _isAutoScrollEnabled = true;
        private bool _scrollToTopButtonVisible = false;
        private readonly Queue<ChatMessage> _pendingMessages = new();
        private int _pendingMessageCount = 0;        // Window resize handling for performance
        private bool _isResizing = false;
        private readonly System.Windows.Threading.DispatcherTimer _resizeTimer = new();
        private readonly Queue<ChatMessage> _resizePausedMessages = new();

        // Window minimize handling for performance
        private bool _isMinimized = false;
        private readonly Queue<ChatMessage> _minimizedPausedMessages = [];

        public ObservableCollection<ChatMessage> ChatMessages { get; } = [];public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }        public string CurrentChannel
        {
            get => _currentChannel;
            set
            {
                _currentChannel = value;
                OnPropertyChanged(nameof(CurrentChannel));
                UpdateWindowTitle();
            }
        }

        public Platform CurrentChannelPlatform
        {
            get => _currentChannelPlatform;
            set
            {
                _currentChannelPlatform = value;
                OnPropertyChanged(nameof(CurrentChannelPlatform));
                UpdateWindowTitle();
            }
        }

        public int CurrentChannelMessageCount
        {
            get => _currentChannelMessageCount;
            set
            {
                _currentChannelMessageCount = value;
                OnPropertyChanged(nameof(CurrentChannelMessageCount));
            }
        }        public string CurrentChannelDatabaseSize
        {
            get => _currentChannelDatabaseSize;
            set
            {
                _currentChannelDatabaseSize = value;
                OnPropertyChanged(nameof(CurrentChannelDatabaseSize));
            }
        }        public double ChatFontSize
        {
            get => _chatFontSize;
            set
            {
                _chatFontSize = value;
                OnPropertyChanged(nameof(ChatFontSize));
            }
        }

        public bool ShowTimestamps
        {
            get => _showTimestamps;
            set
            {
                _showTimestamps = value;
                OnPropertyChanged(nameof(ShowTimestamps));
                
                // Save the setting to configuration
                var configService = _serviceProvider.GetService<UnifiedConfigurationService>();
                if (configService != null)
                {
                    _ = Task.Run(async () => await configService.SetShowTimestampsAsync(value));
                }
                
                // Update the StreamerMentionsWindow if it's open
                if (_streamerMentionsWindow != null && _isStreamerMentionsWindowOpen)
                {
                    _streamerMentionsWindow.UpdateTimestampSetting(value);
                }
            }
        }

        public bool ShowEmotes
        {
            get => _showEmotes;
            set
            {
                _showEmotes = value;
                OnPropertyChanged(nameof(ShowEmotes));
                
                // Save the setting to configuration
                var configService = _serviceProvider.GetService<UnifiedConfigurationService>();
                if (configService != null)
                {
                    _ = Task.Run(async () => await configService.SetShowEmotesAsync(value));
                }
                
                // Update the StreamerMentionsWindow if it's open
                if (_streamerMentionsWindow != null && _isStreamerMentionsWindowOpen)
                {
                    _streamerMentionsWindow.UpdateEmotesSetting(value);
                }
            }
        }        public bool ScrollToTopButtonVisible
        {
            get => _scrollToTopButtonVisible;
            set
            {
                _scrollToTopButtonVisible = value;
                OnPropertyChanged(nameof(ScrollToTopButtonVisible));
            }
        }

        public int PendingMessageCount
        {
            get => _pendingMessageCount;
            set
            {
                _pendingMessageCount = value;
                OnPropertyChanged(nameof(PendingMessageCount));
            }
        }

        public string ScrollButtonText
        {
            get => _scrollButtonText;
            set
            {
                _scrollButtonText = value;
                OnPropertyChanged(nameof(ScrollButtonText));
            }
        }

        // Loading state properties
        private bool _isLoading = true;
        private string _loadingMessage = "Loading databases...";

        public bool IsStreamerMentionsWindowOpen
        {
            get => _isStreamerMentionsWindowOpen;
            set
            {
                _isStreamerMentionsWindowOpen = value;
                OnPropertyChanged(nameof(IsStreamerMentionsWindowOpen));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                _loadingMessage = value;
                OnPropertyChanged(nameof(LoadingMessage));
            }
        }        private bool _reverseChatDirection = false; // Default to newest messages at top

        public bool ReverseChatDirection
        {
            get => _reverseChatDirection;
            set
            {
                _reverseChatDirection = value;
                OnPropertyChanged(nameof(ReverseChatDirection));
                
                // Save the setting to configuration
                var configService = _serviceProvider.GetService<UnifiedConfigurationService>();
                if (configService != null)
                {
                    _ = Task.Run(async () => await configService.SetReverseChatDirectionAsync(value));
                }
                
                // Update the StreamerMentionsWindow if it's open
                if (_streamerMentionsWindow != null && _isStreamerMentionsWindowOpen)
                {
                    _streamerMentionsWindow.UpdateReverseChatDirection(value);
                }
                
                // Refresh chat messages to apply new direction
                RefreshChatDirection();
                
                // Update scroll button text
                UpdateScrollButtonText();
            }
        }
        public MainWindow(TwitchIrcClient twitchClient, ChatDatabaseService databaseService, 
                          MultiChannelManager multiChannelManager, UserFilterService userFilterService,
                          EmoteService emoteService, IServiceProvider serviceProvider, ILogger<MainWindow> logger)
        {
            try
            {
                _logger = logger;
                _logger.LogInformation("MainWindow constructor started");
                
                InitializeComponent();
                DataContext = this;
                
                // Enable dark mode title bar
                DarkModeHelper.EnableDarkMode(this);
                
                _twitchClient = twitchClient;
                _databaseService = databaseService;
                _multiChannelManager = multiChannelManager;
                _userFilterService = userFilterService;
                _emoteService = emoteService;
                _serviceProvider = serviceProvider;
                
                // Set initial window title (after _multiChannelManager is assigned)
                UpdateWindowTitle();// Subscribe to IRC events
                _twitchClient.MessageReceived += OnMessageReceived;
                _twitchClient.Connected += OnConnected;
                _twitchClient.Disconnected += OnDisconnected;
                _twitchClient.Error += OnError;                // Subscribe to MultiChannelManager events for status updates                _multiChannelManager.ChannelConnected += OnChannelConnected;
                _multiChannelManager.ChannelDisconnected += OnChannelDisconnected;
                _multiChannelManager.ChannelRemoved += OnChannelRemoved;
                _multiChannelManager.MessageReceived += OnBackgroundMessageReceived;// Load followed channels when window is loaded
                this.Loaded += MainWindow_Loaded;                // Add scroll event handling for the chat ListBox
                this.Loaded += (s, e) => {
                    if (this.FindName("ChatListBox") is System.Windows.Controls.ListBox chatListBox)
                    {
                        // Get the ScrollViewer from the ListBox template
                        var scrollViewer = GetScrollViewer(chatListBox);
                        if (scrollViewer != null)
                        {
                            scrollViewer.ScrollChanged += ChatScrollViewer_ScrollChanged;
                            // Add mouse wheel event for immediate scroll detection
                            scrollViewer.PreviewMouseWheel += ChatScrollViewer_PreviewMouseWheel;
                            // Add keyboard event for immediate scroll detection
                            scrollViewer.PreviewKeyDown += ChatScrollViewer_PreviewKeyDown;
                            // Add mouse down event for immediate scroll detection when clicking on scroll bar
                            scrollViewer.PreviewMouseDown += ChatScrollViewer_PreviewMouseDown;
                        }
                    }
                };
                  // Initialize timer for updating current channel stats
                _statusUpdateTimer.Interval = TimeSpan.FromSeconds(5); // Update every 5 seconds
                _statusUpdateTimer.Tick += StatusUpdateTimer_Tick;
                _statusUpdateTimer.Start();                // Initialize resize timer for pausing messages during window resize
                _resizeTimer.Interval = TimeSpan.FromMilliseconds(200); // 200ms delay after resize stops - reduced for better responsiveness
                _resizeTimer.Tick += ResizeTimer_Tick;// Subscribe to window resize events
                this.SizeChanged += MainWindow_SizeChanged;
                
                // Subscribe to window state changes for minimize/restore handling
                this.StateChanged += MainWindow_StateChanged;
                
                // Initialize system tray
                if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
                {
                    InitializeSystemTray();
                }
                
                _logger.LogInformation("MainWindow constructor completed successfully");
            }            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in MainWindow constructor");
                
                var errorDetails = $"MainWindow Initialization Error:\n\n" +
                                  $"Type: {ex.GetType().Name}\n" +
                                  $"Message: {ex.Message}\n\n" +
                                  $"Stack Trace:\n{ex.StackTrace}";
                
                if (ex.InnerException != null)
                {
                    errorDetails += $"\n\nInner Exception:\n" +
                                   $"Type: {ex.InnerException.GetType().Name}\n" +
                                   $"Message: {ex.InnerException.Message}\n" +
                                   $"Stack Trace: {ex.InnerException.StackTrace}";
                }
                
                var errorWindow = new ErrorWindow(errorDetails);
                errorWindow.ShowDialog();
                throw;
            }        }        private void FollowedChannelsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFollowedChannelsWindow();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {            try
            {
                var aboutWindow = new AboutWindow
                {
                    Owner = this
                };
                aboutWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening About window");
                System.Windows.MessageBox.Show($"Error opening About window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private void LookupUsersMenuItem_Click(object sender, RoutedEventArgs e)        {
            try
            {
                if (_serviceProvider.GetService(typeof(UserLookupWindow)) is UserLookupWindow userLookupWindow)
                {
                    userLookupWindow.Owner = this;
                    userLookupWindow.Show();
                }
                else
                {
                    _logger.LogError("Failed to resolve UserLookupWindow from service provider");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening User Lookup window");                System.Windows.MessageBox.Show("Error opening User Lookup window. Please try again.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }private void UserFiltersMenuItem_Click(object sender, RoutedEventArgs e)        {
            if (_serviceProvider.GetService(typeof(UserFiltersWindow)) is UserFiltersWindow userFiltersWindow)
            {
                userFiltersWindow.Owner = this;
                userFiltersWindow.ShowDialog();
            }
            else
            {
                _logger.LogError("Failed to resolve UserFiltersWindow from service provider");
            }
        }

        // Username click event handler for opening user messages window
        private void HighlightedTextBlock_UsernameClick(object sender, RoutedEventArgs e)
        {
            if (e is UsernameClickEventArgs usernameArgs && !string.IsNullOrEmpty(usernameArgs.Username))
            {
                try
                {                    _logger.LogInformation("Opening user messages window for user: {Username} from channel: {SourceChannel}", usernameArgs.Username, usernameArgs.SourceChannel);
                    
                    // Get required dependencies from service provider
                    var userMessageService = _serviceProvider.GetRequiredService<UserMessageLookupService>();
                    var logger = _serviceProvider.GetRequiredService<ILogger<UserMessagesWindow>>();

                    // Use the source channel from the clicked message if available, otherwise fall back to current channel
                    string targetChannel = null;
                    if (!string.IsNullOrEmpty(usernameArgs.SourceChannel))
                    {
                        // Construct the complete channel identifier including platform from the message source
                        targetChannel = $"{usernameArgs.SourceChannel.ToLower()}_{usernameArgs.SourcePlatform.ToString().ToLower()}";
                    }
                    else if (!string.IsNullOrEmpty(CurrentChannel))
                    {
                        // Fall back to current channel with platform
                        targetChannel = $"{CurrentChannel.ToLower()}_{CurrentChannelPlatform.ToString().ToLower()}";
                    }

                    // Create UserMessagesWindow with required dependencies
                    var userMessagesWindow = new UserMessagesWindow(
                        userMessageService,
                        logger,
                        usernameArgs.Username,
                        targetChannel
                    )
                    {
                        Owner = this
                    };
                    userMessagesWindow.Show(); // Use Show() instead of ShowDialog() so user can continue interacting with main window
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

        // Mention click event handler for opening user messages window
        private void HighlightedTextBlock_MentionClick(object sender, RoutedEventArgs e)
        {
            if (e is MentionClickEventArgs mentionArgs && !string.IsNullOrEmpty(mentionArgs.MentionedUsername))
            {
                try
                {
                    _logger.LogInformation("Opening user messages window for mentioned user: {Username} from channel: {SourceChannel}", mentionArgs.MentionedUsername, mentionArgs.SourceChannel);
                    
                    // Get required dependencies from service provider
                    var userMessageService = _serviceProvider.GetRequiredService<UserMessageLookupService>();
                    var logger = _serviceProvider.GetRequiredService<ILogger<UserMessagesWindow>>();

                    // Use the source channel from the clicked message if available, otherwise fall back to current channel
                    string targetChannel = null;
                    if (!string.IsNullOrEmpty(mentionArgs.SourceChannel))
                    {
                        // Construct the complete channel identifier including platform from the message source
                        targetChannel = $"{mentionArgs.SourceChannel.ToLower()}_{mentionArgs.SourcePlatform.ToString().ToLower()}";
                    }
                    else if (!string.IsNullOrEmpty(CurrentChannel))
                    {
                        // Fall back to current channel
                        targetChannel = CurrentChannel;
                    }

                    // Create UserMessagesWindow with required dependencies
                    var userMessagesWindow = new UserMessagesWindow(
                        userMessageService,
                        logger,
                        mentionArgs.MentionedUsername,
                        targetChannel
                    )
                    {
                        Owner = this
                    };
                    userMessagesWindow.Show(); // Use Show() instead of ShowDialog() so user can continue interacting with main window
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

        private void OpenFollowedChannelsWindow()
        {
            try
            {
                var followedChannelsWindow = new FollowedChannelsWindow(_multiChannelManager, 
                    _serviceProvider.GetRequiredService<ILogger<FollowedChannelsWindow>>());
                
                followedChannelsWindow.ChannelViewingToggled += OnChannelViewingToggled;
                followedChannelsWindow.Owner = this;
                followedChannelsWindow.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening Followed Channels window");
                var errorDetails = $"Error Opening Followed Channels Window:\n\n" +
                                  $"Type: {ex.GetType().Name}\n" +
                                  $"Message: {ex.Message}\n\n" +
                                  $"Stack Trace:\n{ex.StackTrace}";
                
                var errorWindow = new ErrorWindow(errorDetails);
                errorWindow.ShowDialog();
            }
        }        private async void OnChannelViewingToggled(object sender, FollowedChannel followedChannel)
        {
            try
            {
                var channelName = followedChannel.Name;
                var platform = followedChannel.Platform;
                
                if (followedChannel.ViewingEnabled)
                {
                    _logger.LogInformation("Enabling viewing for channel: {Channel} ({Platform})", channelName, platform);
                    
                    // Ensure this channel is being followed for background logging
                    var followedChannels = _multiChannelManager.GetFollowedChannels();
                    var existingChannel = followedChannels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase) && c.Platform == platform);
                    
                    if (existingChannel == null)
                    {
                        _logger.LogInformation("Adding channel {Channel} to followed channels for background logging", channelName);
                        try
                        {
                            var success = await _multiChannelManager.AddChannelAsync(channelName, platform, true); // Enable logging
                            if (!success)
                            {
                                _logger.LogWarning("Failed to add channel {Channel} for background logging", channelName);
                                followedChannel.ViewingEnabled = false; // Revert the toggle
                                return;
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.Contains("Only one Kick channel"))
                        {
                            _logger.LogWarning("Cannot add Kick channel {Channel}: {Error}", channelName, ex.Message);
                            System.Windows.MessageBox.Show(ex.Message, "Kick Channel Limitation", MessageBoxButton.OK, MessageBoxImage.Warning);
                            followedChannel.ViewingEnabled = false; // Revert the toggle
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error adding channel {Channel} for background logging", channelName);
                            System.Windows.MessageBox.Show($"Error adding channel '{channelName}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            followedChannel.ViewingEnabled = false; // Revert the toggle
                            return;
                        }
                    }
                    else if (!existingChannel.IsConnected)
                    {
                        // Channel exists but is not connected - ensure it gets connected for live messages
                        _logger.LogInformation("Channel {Channel} exists but is not connected. Attempting to connect for live messages...", channelName);
                        try
                        {
                            await _multiChannelManager.RetryConnectionAsync(channelName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to connect channel {Channel} for live messages", channelName);
                            // Don't revert viewing - user can still see historical messages
                        }
                    }
                    
                    // Load recent messages for this channel
                    await LoadRecentMessagesForChannelAsync(channelName, platform);
                }
                else
                {
                    _logger.LogInformation("Disabling viewing for channel: {Channel} ({Platform})", channelName, platform);
                    
                    // Remove messages from this channel from the display
                    RemoveChannelMessagesFromDisplay(channelName, platform);
                }
                
                // Update status and stats
                UpdateMultiChannelStats();
                UpdateMultiChannelStatus();
                UpdateWindowTitle();
                
                // Fire the MultiChannelManager event so other windows can be notified
                if (_multiChannelManager != null)
                {
                    // Fire the event to notify other components (like StreamerMentionsWindow)
                    _multiChannelManager.OnChannelViewingToggled(channelName, followedChannel.ViewingEnabled);
                }
                
                // If this is the first channel enabled, ensure we're "connected"
                var anyChannelEnabled = _multiChannelManager.GetFollowedChannels().Any(c => c.ViewingEnabled);
                IsConnected = anyChannelEnabled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling viewing for channel: {Channel}", followedChannel?.Name ?? "unknown");
                followedChannel.ViewingEnabled = false; // Revert on error
            }
        }        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            // Only clear the visual chat messages, NOT the database count
            ChatMessages.Clear();
            
            // Clear pending messages and reset auto-scroll
            _pendingMessages.Clear();
            PendingMessageCount = 0;
            _isAutoScrollEnabled = true;
            ScrollToTopButtonVisible = false;
            
            // The counter should continue to show the database count
            // Do not reset CurrentChannelMessageCount here
        }private async void RefreshChatButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CurrentChannel))
            {
                _logger.LogWarning("No channel selected for refresh");
                return;
            }            try
            {
                // Clear current chat messages
                ChatMessages.Clear();

                // Get last 100 messages from database
                var recentMessages = await _databaseService.GetRecentMessagesAsync(100, _emoteService);
                  // Add messages to chat display in newest-first order
                // GetRecentMessagesAsync returns messages in DESC order (newest first), which is what we want
                foreach (var message in recentMessages)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ChatMessages.Add(message);
                    });
                }// Add system message at the top (newest position) to indicate chat was refreshed
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var systemMessage = new ChatMessage
                    {
                        Username = "System",
                        Message = $"Chat Refreshed - Loaded last {recentMessages.Count} messages from database.",
                        Timestamp = DateTime.Now,
                        IsSystemMessage = true
                    };
                    MessageParser.ParseChatMessage(systemMessage, _emoteService);
                    AddChatMessage(systemMessage);
                });

                _logger.LogInformation("Refreshed chat with {Count} messages from database for channel: {Channel}", 
                    recentMessages.Count, CurrentChannel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing chat for channel: {Channel}", CurrentChannel);
            }
        }

        // Font scaling event handlers
        private const double BaseFontSize = 12.0;
        
        private void FontScale50_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = BaseFontSize * 0.5; // 50% of base size
        }

        private void FontScale75_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = BaseFontSize * 0.75; // 75% of base size
        }

        private void FontScaleDefault_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = BaseFontSize; // 100% - Default size
        }

        private void FontScale125_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = BaseFontSize * 1.25; // 125% of base size
        }

        private void FontScale150_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = BaseFontSize * 1.5; // 150% of base size
        }

        private void FontScale200_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = BaseFontSize * 2.0; // 200% of base size
        }

        private void ChatScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Check if Ctrl key is held down for font scaling
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // Prevent the scroll viewer from scrolling
                e.Handled = true;
                
                // Calculate font size change (120 is the standard mouse wheel delta)
                double fontSizeChange = e.Delta > 0 ? 1.0 : -1.0; // Increase or decrease by 1pt
                double newFontSize = ChatFontSize + fontSizeChange;
                
                // Clamp font size to reasonable bounds (6pt to 36pt)
                const double MinFontSize = 6.0;
                const double MaxFontSize = 36.0;
                
                newFontSize = Math.Max(MinFontSize, Math.Min(MaxFontSize, newFontSize));
                
                // Only update if the size actually changed
                if (Math.Abs(newFontSize - ChatFontSize) > 0.01)
                {
                    ChatFontSize = newFontSize;
                    _logger.LogDebug("Font size changed via Ctrl+Mouse Wheel to: {FontSize}pt", ChatFontSize);
                }
                return;
            }

            // Check if user is scrolling down (negative delta) while auto-scroll is enabled
            if (e.Delta < 0 && _isAutoScrollEnabled && sender is ScrollViewer scrollViewer)
            {
                // User is scrolling down - immediately disable auto-scroll
                _isAutoScrollEnabled = false;
                ScrollToTopButtonVisible = true;
                _logger.LogDebug("Auto-scroll immediately disabled due to downward mouse wheel scroll");
            }
        }        private void OnMessageReceived(object sender, ChatMessage message)
        {
            // Check if user is blacklisted
            if (_userFilterService.IsUserBlacklisted(message.Username))
            {
                _logger.LogDebug("Filtered out message from blacklisted user: {Username}", message.Username);
                return;
            }            Dispatcher.Invoke(() =>
            {
                // Check if window is minimized - if so, pause message rendering
                if (_isMinimized)
                {
                    _minimizedPausedMessages.Enqueue(message);
                    _logger.LogDebug("Queued message while window minimized. Paused: {Count}", _minimizedPausedMessages.Count);
                }
                // Check if window is being resized - if so, pause message rendering
                else if (_isResizing)
                {
                    _resizePausedMessages.Enqueue(message);
                    _logger.LogDebug("Queued message while window resizing. Paused: {Count}", _resizePausedMessages.Count);
                }
                else if (_isAutoScrollEnabled)
                {
                    // Auto-scroll is enabled, add message using helper method
                    AddChatMessage(message);
                }
                else
                {
                    // Auto-scroll is disabled, queue the message instead of adding it immediately
                    _pendingMessages.Enqueue(message);
                    PendingMessageCount = _pendingMessages.Count;
                    
                    _logger.LogDebug("Queued message while auto-scroll disabled. Pending: {Count}", _pendingMessages.Count);
                }

                // Increment the counter immediately for responsiveness
                CurrentChannelMessageCount++;

                // DO NOT log to database here - the MultiChannelManager background client 
                // is already handling database logging for this channel.
                // This prevents duplicate database entries when actively viewing a channel.
                _logger.LogDebug("FALLBACK: Message displayed from main IRC client for channel {Channel}: {Username} - {Message}", 
                    CurrentChannel, message.Username, message.Message);
            });
        }private void OnConnected(object sender, string channel)
        {
            // This event is only for the main window's IRC client, which we no longer use
            // when actively viewing channels (we rely on background clients instead)
            Dispatcher.Invoke(async () =>
            {
                IsConnected = true;
                var messageCount = await _databaseService.GetMessageCountAsync();
                
                // Initialize the current channel message count
                CurrentChannelMessageCount = messageCount;
                
                _logger.LogInformation("Main window IRC client connected to #{Channel}", channel);
            });
        }        private void OnDisconnected(object sender, EventArgs e)
        {
            // This event is only for the main window's IRC client
            Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                _logger.LogInformation("Main window IRC client disconnected");
                // Note: We don't add system messages here since we're primarily using background clients
            });
        }private void OnError(object sender, string error)
        {
            Dispatcher.Invoke(() =>            {
                _logger.LogError("IRC Error: {Error}", error);
                // Insert system message at the top
                var errorMessage = new ChatMessage
                {
                    Username = "System",
                    Message = $"Error: {error}",
                    Timestamp = DateTime.Now,
                    IsSystemMessage = true
                };
                MessageParser.ParseChatMessage(errorMessage, _emoteService);
                ChatMessages.Insert(0, errorMessage);
            });
        }        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure we start in loading state (should already be true from initialization)
                IsLoading = true;
                LoadingMessage = "Loading followed channels...";
                _logger.LogInformation("Loading followed channels from storage...");
                await _multiChannelManager.LoadFollowedChannelsAsync();
                
                // Load UI settings
                var configService = _serviceProvider.GetService<UnifiedConfigurationService>();
                if (configService != null)
                {
                    _showTimestamps = configService.GetShowTimestamps();
                    OnPropertyChanged(nameof(ShowTimestamps));
                    _logger.LogDebug("Loaded ShowTimestamps setting: {ShowTimestamps}", _showTimestamps);
                    
                    _showEmotes = configService.GetShowEmotes();
                    OnPropertyChanged(nameof(ShowEmotes));
                    _logger.LogDebug("Loaded ShowEmotes setting: {ShowEmotes}", _showEmotes);
                    
                    _reverseChatDirection = configService.GetReverseChatDirection();
                    OnPropertyChanged(nameof(ReverseChatDirection));
                    _logger.LogDebug("Loaded ReverseChatDirection setting: {ReverseChatDirection}", _reverseChatDirection);
                    
                    // Update scroll button text based on loaded setting
                    UpdateScrollButtonText();
                }
                
                LoadingMessage = "Connecting channels...";
                
                // Update the status after loading channels
                UpdateFollowedChannelsStatus();
                
                // Ensure all channels with viewing enabled are connected for live messages
                var enabledChannels = _multiChannelManager.GetFollowedChannels().Where(c => c.ViewingEnabled).ToList();
                foreach (var channel in enabledChannels.Where(c => !c.IsConnected))
                {
                    LoadingMessage = $"Connecting to {channel.Name}...";
                    _logger.LogInformation("Reconnecting viewing-enabled channel on startup: {Channel} ({Platform})", channel.Name, channel.Platform);
                    try
                    {
                        await _multiChannelManager.RetryConnectionAsync(channel.Name);
                        // Load recent messages for this channel
                        await LoadRecentMessagesForChannelAsync(channel.Name, channel.Platform);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reconnect viewing-enabled channel on startup: {Channel}", channel.Name);
                    }
                }
                
                // Add initial welcome message if no channels are enabled for viewing
                if (enabledChannels.Count == 0)
                {
                    var welcomeMessage = new ChatMessage
                    {
                        Username = "System",
                        Message = "Welcome to Multi-Platform Chat Viewer! 📺\n\n" +
                                "To get started:\n" +
                                "1. Go to 'File' menu → 'Manage Channels'\n" +
                                "2. Add channels from Twitch or Kick\n" +
                                "3. Enable multiple channels to view all their chat messages here simultaneously\n\n" +
                                "You can enable/disable any combination of channels for multi-chat viewing!",
                        Timestamp = DateTime.Now,
                        IsSystemMessage = true
                    };
                    MessageParser.ParseChatMessage(welcomeMessage, _emoteService);
                    AddChatMessage(welcomeMessage);
                }
                
                // Loading complete - enable user interface
                IsLoading = false;
                _logger.LogInformation("Application startup completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading followed channels on startup");
                // Ensure we exit loading state even on error
                IsLoading = false;
                LoadingMessage = "Error occurred during startup";
            }
        }

        private void OnChannelConnected(object sender, string channelName)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateFollowedChannelsStatus();
            });
        }

        private void OnChannelDisconnected(object sender, string channelName)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateFollowedChannelsStatus();
            });
        }        private void OnChannelRemoved(object sender, string channelName)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateFollowedChannelsStatus();
            });
        }

        private void UpdateFollowedChannelsStatus()
        {
            try
            {
                // Use the same format as multi-channel status for consistency
                UpdateMultiChannelStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating followed channels status");
                StatusMessage = "Error loading channel status";
            }
        }

        private async void StatusUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(CurrentChannel))
            {
                await UpdateCurrentChannelStatsAsync();
            }
        }

        private async Task UpdateCurrentChannelStatsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentChannel))
                {
                    CurrentChannelMessageCount = 0;
                    CurrentChannelDatabaseSize = "0 B";
                    return;
                }

                // Get message count from database
                CurrentChannelMessageCount = await _databaseService.GetMessageCountAsync();

                // Get database size
                var dbSize = ChatDatabaseService.GetDatabaseSizeByPath(CurrentChannel, CurrentChannelPlatform);
                CurrentChannelDatabaseSize = FormatFileSize(dbSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating current channel stats for channel: {Channel}", CurrentChannel);
            }
        }

        private async Task LoadRecentMessagesForChannelAsync(string channelName, Platform platform)
        {
            try
            {
                // Create a temporary database service for this specific channel
                var databaseLogger = _serviceProvider.GetRequiredService<ILogger<ChatDatabaseService>>();
                var tempDatabaseService = new ChatDatabaseService(databaseLogger);
                await tempDatabaseService.InitializeDatabaseAsync(channelName, platform);
                
                // Load more messages initially, but respect our limit
                var messagesToLoad = Math.Min(50, 100); // Load fewer per channel to avoid overwhelming the UI
                var recentMessages = await tempDatabaseService.GetRecentMessagesAsync(messagesToLoad, _emoteService);
                
                // Mark messages with their source channel for display
                foreach (var message in recentMessages)
                {
                    message.SourceChannel = channelName;
                    message.SourcePlatform = platform;
                    
                    // Add to the chat messages collection, sorted by timestamp
                    InsertMessageInOrder(message);
                }
                
                await tempDatabaseService.CloseConnectionAsync();
                
                _logger.LogInformation("Loaded {Count} recent messages for channel: {Channel} ({Platform})", 
                    recentMessages.Count, channelName, platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent messages for channel: {Channel} ({Platform})", channelName, platform);
            }
        }

        private void InsertMessageInOrder(ChatMessage message)
        {
            if (_reverseChatDirection)
            {
                // Reverse direction: find position to maintain chronological order (oldest to newest)
                var insertIndex = ChatMessages.Count; // Default to end
                for (int i = 0; i < ChatMessages.Count; i++)
                {
                    if (ChatMessages[i].Timestamp > message.Timestamp)
                    {
                        insertIndex = i;
                        break;
                    }
                }
                ChatMessages.Insert(insertIndex, message);
            }
            else
            {
                // Normal direction: find position to maintain reverse chronological order (newest to oldest)
                var insertIndex = 0;
                for (int i = 0; i < ChatMessages.Count; i++)
                {
                    if (ChatMessages[i].Timestamp <= message.Timestamp)
                    {
                        insertIndex = i;
                        break;
                    }
                    insertIndex = i + 1;
                }
                ChatMessages.Insert(insertIndex, message);
            }
            
            // Maintain the message limit
            while (ChatMessages.Count > MAX_MESSAGES_IN_CHAT)
            {
                if (_reverseChatDirection)
                {
                    // Remove oldest (from beginning)
                    ChatMessages.RemoveAt(0);
                }
                else
                {
                    // Remove oldest (from end)
                    ChatMessages.RemoveAt(ChatMessages.Count - 1);
                }
            }
        }

        private void RemoveChannelMessagesFromDisplay(string channelName, Platform platform)
        {
            try
            {
                // Remove all messages from the specified channel
                for (int i = ChatMessages.Count - 1; i >= 0; i--)
                {
                    var message = ChatMessages[i];
                    if (string.Equals(message.SourceChannel, channelName, StringComparison.OrdinalIgnoreCase) 
                        && message.SourcePlatform == platform)
                    {
                        ChatMessages.RemoveAt(i);
                    }
                }
                
                _logger.LogInformation("Removed messages for channel: {Channel} ({Platform})", channelName, platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing messages for channel: {Channel} ({Platform})", channelName, platform);
            }
        }

        private void UpdateMultiChannelStats()
        {
            try
            {
                var enabledChannels = _multiChannelManager.GetFollowedChannels().Where(c => c.ViewingEnabled).ToList();
                
                if (enabledChannels.Count == 0)
                {
                    CurrentChannelMessageCount = 0;
                    CurrentChannelDatabaseSize = "0 B";
                }
                else if (enabledChannels.Count == 1)
                {
                    var channel = enabledChannels.First();
                    CurrentChannelMessageCount = channel.MessageCount;
                    CurrentChannelDatabaseSize = channel.DatabaseSizeFormatted;
                }
                else
                {
                    // Aggregate stats from multiple channels
                    var totalMessages = enabledChannels.Sum(c => c.MessageCount);
                    var totalSize = enabledChannels.Sum(c => c.DatabaseSize);
                    
                    CurrentChannelMessageCount = totalMessages;
                    CurrentChannelDatabaseSize = FormatFileSize(totalSize);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating multi-channel stats");
            }
        }

        private void UpdateMultiChannelStatus()
        {
            try
            {
                var enabledChannels = _multiChannelManager.GetFollowedChannels().Where(c => c.ViewingEnabled).ToList();
                
                if (enabledChannels.Count == 0)
                {
                    StatusMessage = "No Channels Enabled";
                }
                else if (enabledChannels.Count == 1)
                {
                    var channel = enabledChannels.First();
                    StatusMessage = $"{channel.Name}({channel.PlatformName.ToLower()})";
                }
                else
                {
                    var channelNames = string.Join(", ", enabledChannels.Select(c => $"{c.Name}({c.PlatformName.ToLower()})"));
                    StatusMessage = channelNames;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating multi-channel status");
                StatusMessage = "Error updating status";
            }
        }

        private async Task LoadRecentMessagesAsync()
        {
            try
            {
                // Load more messages initially, but respect our limit
                var messagesToLoad = Math.Min(MAX_MESSAGES_IN_CHAT - 50, 100); // Leave room for new messages
                var recentMessages = await _databaseService.GetRecentMessagesAsync(messagesToLoad, _emoteService);
                
                // Messages from database are already ordered DESC (newest first)
                // Insert each message at the end to maintain newest-first order in UI
                foreach (var message in recentMessages)
                {
                    // Add to the end so that the newest message (first in list) ends up at top
                    ChatMessages.Add(message);
                }
                
                _logger.LogInformation("Loaded {Count} recent messages for channel: {Channel}", 
                    recentMessages.Count, CurrentChannel);
                  // Add a system message at the top to indicate historical messages were loaded
                if (recentMessages.Count > 0)
                {
                    var systemMessage = new ChatMessage
                    {
                        Username = "System",
                        Message = $"Loaded {recentMessages.Count} recent messages from database (limit: {MAX_MESSAGES_IN_CHAT} total)",
                        Timestamp = DateTime.Now,
                        IsSystemMessage = true
                    };
                    MessageParser.ParseChatMessage(systemMessage, _emoteService);
                    ChatMessages.Insert(0, systemMessage);
                }
            }catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent messages for channel: {Channel}", CurrentChannel);
                
                // Add error message to chat at the top
                var errorMessage = new ChatMessage
                {
                    Username = "System",
                    Message = "Failed to load recent messages from database",
                    Timestamp = DateTime.Now,
                    IsSystemMessage = true
                };
                MessageParser.ParseChatMessage(errorMessage, _emoteService);
                ChatMessages.Insert(0, errorMessage);
            }
        }        private void OnBackgroundMessageReceived(object sender, (string Channel, ChatMessage Message) args)
        {
            // Check if user is blacklisted
            if (_userFilterService.IsUserBlacklisted(args.Message.Username))
            {
                _logger.LogDebug("Filtered out background message from blacklisted user: {Username}", args.Message.Username);
                return;
            }

            // Check if this message is from any channel that's enabled for viewing
            var enabledChannels = _multiChannelManager.GetFollowedChannels().Where(c => c.ViewingEnabled).ToList();
            var isFromEnabledChannel = enabledChannels.Any(c => 
                args.Channel.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
            
            if (isFromEnabledChannel)
            {                Dispatcher.Invoke(() =>
                {
                    // Check if window is minimized - if so, pause message rendering
                    if (_isMinimized)
                    {
                        _minimizedPausedMessages.Enqueue(args.Message);
                        _logger.LogDebug("Queued background message while window minimized. Paused: {Count}", _minimizedPausedMessages.Count);
                    }
                    // Check if window is being resized - if so, pause message rendering
                    else if (_isResizing)
                    {
                        _resizePausedMessages.Enqueue(args.Message);
                        _logger.LogDebug("Queued background message while window resizing. Paused: {Count}", _resizePausedMessages.Count);
                    }
                    else if (_isAutoScrollEnabled)
                    {
                        // Auto-scroll is enabled, add message using helper method
                        AddChatMessage(args.Message);
                    }
                    else
                    {
                        // Auto-scroll is disabled, queue the message instead of adding it immediately
                        _pendingMessages.Enqueue(args.Message);
                        PendingMessageCount = _pendingMessages.Count;
                        
                        _logger.LogDebug("Queued background message while auto-scroll disabled. Pending: {Count}", _pendingMessages.Count);
                    }

                    // Increment the counter immediately for responsiveness
                    CurrentChannelMessageCount++;                    
                    
                    _logger.LogDebug("Background message displayed in main window for channel {Channel}: {Username} - {Message}", 
                        args.Channel, args.Message.Username, args.Message.Message);
                });
            }
        }[SupportedOSPlatform("windows6.1")]        private void InitializeSystemTray()
        {            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Icon = IconHelper.GetApplicationIcon(),
                    Text = "Twitch Chat Viewer",
                    Visible = true // Always visible in notification area
                };

                // Create context menu for tray icon
                var contextMenu = new ContextMenuStrip();
                
                var showMenuItem = new ToolStripMenuItem("Show")
                {
                    Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold)
                };
                showMenuItem.Click += (s, e) => ShowFromTray();
                contextMenu.Items.Add(showMenuItem);
                
                contextMenu.Items.Add(new ToolStripSeparator());
                
                var exitMenuItem = new ToolStripMenuItem("Exit");
                exitMenuItem.Click += (s, e) => ExitApplication();
                contextMenu.Items.Add(exitMenuItem);
                
                _notifyIcon.ContextMenuStrip = contextMenu;
                
                // Double-click to show window
                _notifyIcon.DoubleClick += (s, e) => ShowFromTray();
                
                _logger.LogInformation("System tray initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize system tray");
            }
        }        [SupportedOSPlatform("windows6.1")]
        private void HideToTray()
        {
            try
            {
                this.Hide();
                this.WindowState = WindowState.Minimized;
                _isMinimized = true; // Treat hide to tray as minimized for message rendering
                
                // Show balloon tip to inform user
                _notifyIcon.ShowBalloonTip(2000, "Twitch Chat Viewer", 
                    "Application minimized to tray. Double-click the icon to restore.", 
                    ToolTipIcon.Info);
                
                _logger.LogInformation("Application minimized to system tray - pausing message rendering");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to minimize to system tray");
            }
        }        [SupportedOSPlatform("windows6.1")]
        private void ShowFromTray()
        {
            try
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
                this.Topmost = true;  // Bring to front
                this.Topmost = false; // Remove topmost flag
                
                // Resume message rendering if we were minimized/hidden
                if (_isMinimized)
                {
                    _isMinimized = false;
                    _logger.LogDebug("Window restored from tray - resuming message rendering. Processing {Count} paused messages", _minimizedPausedMessages.Count);
                    
                    // Process any messages that were paused while minimized/hidden
                    ProcessMinimizedPausedMessages();
                }
                
                _logger.LogInformation("Application restored from system tray");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore from system tray");
            }
        }

        private void ExitApplication()
        {
            try
            {
                _logger.LogInformation("Application exit requested from menu");
                this.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during application exit");
            }
        }        private void ChatScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {            
            if (sender is not ScrollViewer scrollViewer) return;

            // Check scroll position based on chat direction
            const double scrollThreshold = 1.0; // Very sensitive threshold
            bool isAtNewMessagePosition;
            
            if (_reverseChatDirection)
            {
                // Reverse direction: check if at bottom
                double maxScroll = scrollViewer.ScrollableHeight;
                isAtNewMessagePosition = (maxScroll - scrollViewer.VerticalOffset) <= scrollThreshold;
            }
            else
            {
                // Normal direction: check if at top
                isAtNewMessagePosition = scrollViewer.VerticalOffset <= scrollThreshold;
            }

            if (!isAtNewMessagePosition && _isAutoScrollEnabled)
            {
                // User scrolled away from new message position, disable auto-scroll and show button
                _isAutoScrollEnabled = false;
                ScrollToTopButtonVisible = true;
                _logger.LogDebug("Auto-scroll disabled, user scrolled to position: {Position}", scrollViewer.VerticalOffset);
            }
            else if (isAtNewMessagePosition && !_isAutoScrollEnabled)
            {
                // User scrolled back to new message position, re-enable auto-scroll and process pending messages
                ProcessPendingMessages();
                _isAutoScrollEnabled = true;
                ScrollToTopButtonVisible = false;
                _logger.LogDebug("Auto-scroll re-enabled, user scrolled back to new message position");
            }
        }        private void ScrollToTopButton_Click(object sender, RoutedEventArgs e)
        {
            var scrollViewer = ChatScrollViewer;
            if (scrollViewer != null)
            {
                // Process pending messages first, then scroll
                ProcessPendingMessages();
                ScrollToAppropriatePosition();
                _isAutoScrollEnabled = true;
                ScrollToTopButtonVisible = false;
                _logger.LogDebug("Scroll button clicked, auto-scroll re-enabled");
            }
        }

        private void ProcessPendingMessages()
        {
            if (_pendingMessages.Count == 0) return;

            _logger.LogInformation("Processing {Count} pending messages", _pendingMessages.Count);

            // Get all pending messages
            var messagesToAdd = new List<ChatMessage>();
            while (_pendingMessages.Count > 0)
            {
                messagesToAdd.Add(_pendingMessages.Dequeue());
            }

            // Add each message using the helper method that respects direction
            // Process in chronological order so they appear in correct sequence
            messagesToAdd.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            foreach (var message in messagesToAdd)
            {
                AddChatMessage(message);
            }

            // Reset pending count
            PendingMessageCount = 0;

            _logger.LogInformation("Processed {Count} pending messages", messagesToAdd.Count);
        }        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Start pausing message rendering when window starts resizing
            if (!_isResizing)
            {
                _isResizing = true;
                _logger.LogDebug("Window resize started - pausing message rendering");                
                // Temporarily suspend layout updates for better performance
                if (this.FindName("ChatListBox") is System.Windows.Controls.ListBox chatListBox)
                {
                    chatListBox.BeginInit();
                }
            }

            // Restart the resize timer on each size change event
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }        private void ResizeTimer_Tick(object sender, EventArgs e)
        {
            // Timer fired, which means resize has stopped
            _resizeTimer.Stop();
            _isResizing = false;
            
            _logger.LogDebug("Window resize ended - resuming message rendering. Processing {Count} paused messages", _resizePausedMessages.Count);            
            // Resume layout updates
            if (this.FindName("ChatListBox") is System.Windows.Controls.ListBox chatListBox)
            {
                chatListBox.EndInit();
            }
            
            // Process any messages that were paused during resize
            ProcessResizePausedMessages();
        }

        private void ProcessResizePausedMessages()
        {
            if (_resizePausedMessages.Count == 0) return;

            _logger.LogInformation("Processing {Count} resize-paused messages", _resizePausedMessages.Count);

            // Move paused messages to regular pending queue or add directly to chat
            while (_resizePausedMessages.Count > 0)
            {
                var message = _resizePausedMessages.Dequeue();
                
                if (_isAutoScrollEnabled)
                {
                    // Auto-scroll is enabled, add message directly to chat
                    ChatMessages.Insert(0, message);
                }
                else
                {
                    // Auto-scroll is disabled, add to pending messages
                    _pendingMessages.Enqueue(message);
                }
            }

            // Update pending count if auto-scroll is disabled
            if (!_isAutoScrollEnabled)
            {
                PendingMessageCount = _pendingMessages.Count;
            }

            // Maintain message limit for performance
            if (ChatMessages.Count > MAX_MESSAGES_IN_CHAT)
            {
                var messagesToRemove = ChatMessages.Count - MAX_MESSAGES_IN_CHAT;
                for (int i = 0; i < messagesToRemove; i++)
                {
                    ChatMessages.RemoveAt(ChatMessages.Count - 1);
                }
                _logger.LogDebug("Trimmed {Count} old messages to maintain {MaxMessages} message limit after processing resize-paused messages", 
                    messagesToRemove, MAX_MESSAGES_IN_CHAT);
            }            // Auto-scroll to top if enabled            if (_isAutoScrollEnabled)
            {
                var scrollViewer = ChatScrollViewer;
                scrollViewer?.ScrollToTop();
            }

            _logger.LogInformation("Processed {Count} resize-paused messages", _resizePausedMessages.Count);
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                _isMinimized = true;
                _logger.LogDebug("Window minimized - pausing message rendering");
            }
            else if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
            {
                if (_isMinimized)
                {
                    _isMinimized = false;
                    _logger.LogDebug("Window restored - resuming message rendering. Processing {Count} paused messages", _minimizedPausedMessages.Count);
                    
                    // Process any messages that were paused while minimized
                    ProcessMinimizedPausedMessages();
                }
            }
        }        private async void ProcessMinimizedPausedMessages()
        {
            // Clear any queued messages since we'll reload from database instead
            var queuedCount = _minimizedPausedMessages.Count;
            _minimizedPausedMessages.Clear();

            if (string.IsNullOrEmpty(CurrentChannel))
            {
                _logger.LogWarning("No current channel set when restoring from minimize");
                return;
            }

            _logger.LogInformation("Window restored from minimize - clearing chat and loading last 100 messages from database (discarded {QueuedCount} queued messages)", queuedCount);

            try
            {
                // Clear current chat messages
                ChatMessages.Clear();

                // Clear pending messages and reset auto-scroll
                _pendingMessages.Clear();
                PendingMessageCount = 0;
                _isAutoScrollEnabled = true;
                ScrollToTopButtonVisible = false;

                // Load last 100 messages from database
                var recentMessages = await _databaseService.GetRecentMessagesAsync(100);
                
                // Add messages to chat display in newest-first order
                // GetRecentMessagesAsync returns messages in DESC order (newest first), which is what we want
                foreach (var message in recentMessages)
                {
                    ChatMessages.Add(message);
                }

                // Add system message at the top (newest position) to indicate chat was refreshed
                var systemMessage = new ChatMessage
                {
                    Username = "System",
                    Message = $"Window restored - Loaded last {recentMessages.Count} messages from database.",
                    Timestamp = DateTime.Now,
                    IsSystemMessage = true
                };
                MessageParser.ParseChatMessage(systemMessage, _emoteService);
                ChatMessages.Insert(0, systemMessage);                // Auto-scroll to top since we enabled auto-scroll
                var scrollViewer = ChatScrollViewer;
                scrollViewer?.ScrollToTop();

                _logger.LogInformation("Successfully reloaded {Count} messages from database for channel: {Channel}", 
                    recentMessages.Count, CurrentChannel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reloading messages from database when restoring from minimize for channel: {Channel}", CurrentChannel);
                
                // Add error message to chat
                var errorMessage = new ChatMessage
                {
                    Username = "System",
                    Message = "Failed to load recent messages from database after window restore",
                    Timestamp = DateTime.Now,
                    IsSystemMessage = true
                };
                MessageParser.ParseChatMessage(errorMessage, _emoteService);
                ChatMessages.Insert(0, errorMessage);
            }
        }

        // Helper method to get the ScrollViewer from a ListBox
        private static ScrollViewer GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer scrollViewer)
                return scrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }        // Cache the scroll viewer for performance
        private ScrollViewer _chatScrollViewer;
        private ScrollViewer ChatScrollViewer 
        { 
            get 
            {                if (_chatScrollViewer == null)
                {
                    if (this.FindName("ChatListBox") is System.Windows.Controls.ListBox chatListBox)
                    {
                        _chatScrollViewer = GetScrollViewer(chatListBox);
                    }
                }
                return _chatScrollViewer;
            } 
        }

        private void ChatScrollViewer_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Check if user is pressing keys that would scroll down while auto-scroll is enabled
            if (_isAutoScrollEnabled)
            {
                bool isScrollDownKey = e.Key == System.Windows.Input.Key.Down ||
                                     e.Key == System.Windows.Input.Key.PageDown ||
                                     e.Key == System.Windows.Input.Key.End;

                if (isScrollDownKey)
                {
                    // User is trying to scroll down - immediately disable auto-scroll
                    _isAutoScrollEnabled = false;
                    ScrollToTopButtonVisible = true;
                    _logger.LogDebug("Auto-scroll immediately disabled due to navigation key: {Key}", e.Key);
                }
            }
        }

        private void ChatScrollViewer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Check if user clicked on scroll bar area while auto-scroll is enabled
            if (_isAutoScrollEnabled && sender is ScrollViewer scrollViewer)
            {
                var mousePos = e.GetPosition(scrollViewer);
                var scrollBarWidth = SystemParameters.VerticalScrollBarWidth;
                
                // Check if click is in the scroll bar area (right side of the scroll viewer)
                if (mousePos.X > scrollViewer.ActualWidth - scrollBarWidth)
                {
                    // User clicked on scroll bar - disable auto-scroll immediately
                    _isAutoScrollEnabled = false;
                    ScrollToTopButtonVisible = true;
                    _logger.LogDebug("Auto-scroll immediately disabled due to scroll bar interaction");
                }
            }
        }

        private void ShowTimestampsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // The ShowTimestamps property setter will handle saving to configuration
            // This event is mainly for logging purposes
            _logger.LogDebug("Show timestamps toggled via menu: {ShowTimestamps}", ShowTimestamps);
        }

        private void ShowEmotesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // The ShowEmotes property setter will handle saving to configuration
            // This event is mainly for logging purposes
            _logger.LogDebug("Show emotes toggled via menu: {ShowEmotes}", ShowEmotes);
        }

        private void StreamerMentionsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem)
                {
                    // Get the desired state from the menu item's IsChecked property
                    bool shouldBeOpen = menuItem.IsChecked;
                    
                    if (shouldBeOpen && (_streamerMentionsWindow == null || !_isStreamerMentionsWindowOpen))
                    {
                        // Create and show the Streamer Mentions window with current settings
                        _streamerMentionsWindow = new StreamerMentionsWindow(_serviceProvider, null, ShowTimestamps, ReverseChatDirection);
                        
                        // Subscribe to the Closed event to update our state
                        _streamerMentionsWindow.Closed += StreamerMentionsWindow_Closed;
                        
                        _streamerMentionsWindow.Show();
                        IsStreamerMentionsWindowOpen = true;
                        
                        _logger.LogInformation("Opened Streamer Mentions window - monitoring all active channels");
                    }
                    else if (!shouldBeOpen && _streamerMentionsWindow != null && _isStreamerMentionsWindowOpen)
                    {
                        // Close the window
                        _streamerMentionsWindow.Close();
                        // The Closed event handler will update our state
                    }
                    else if (shouldBeOpen && _streamerMentionsWindow != null && _isStreamerMentionsWindowOpen)
                    {
                        // Window exists and is open, bring it to front
                        _streamerMentionsWindow.Activate();
                        _streamerMentionsWindow.Focus();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing Streamer Mentions window");
                System.Windows.MessageBox.Show("Error managing Streamer Mentions window. Please try again.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Reset the checkbox state on error
                IsStreamerMentionsWindowOpen = false;
            }
        }

        private void StreamerMentionsWindow_Closed(object sender, EventArgs e)
        {
            // Update our state when the window is closed
            IsStreamerMentionsWindowOpen = false;
            
            // Unsubscribe from the event and clear the reference
            if (_streamerMentionsWindow != null)
            {
                _streamerMentionsWindow.Closed -= StreamerMentionsWindow_Closed;
                _streamerMentionsWindow = null;
            }
            
            _logger.LogInformation("Streamer Mentions window closed");
        }

        private void RefreshChatDirection()
        {
            try
            {
                // Store current messages
                var currentMessages = ChatMessages.ToList();
                
                // Clear and re-add messages in the correct order
                ChatMessages.Clear();
                
                if (_reverseChatDirection)
                {
                    // Reverse direction: oldest first, newest at bottom
                    var sortedMessages = currentMessages.OrderBy(m => m.Timestamp).ToList();
                    foreach (var message in sortedMessages)
                    {
                        ChatMessages.Add(message);
                    }
                }
                else
                {
                    // Normal direction: newest first, newest at top
                    var sortedMessages = currentMessages.OrderByDescending(m => m.Timestamp).ToList();
                    foreach (var message in sortedMessages)
                    {
                        ChatMessages.Add(message);
                    }
                }
                
                // Scroll to the appropriate position
                ScrollToAppropriatePosition();
                
                _logger.LogInformation("Refreshed chat direction. Reverse: {IsReverse}, Messages: {Count}", 
                    _reverseChatDirection, ChatMessages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing chat direction");
            }
        }

        private void AddChatMessage(ChatMessage message)
        {
            try
            {
                if (_reverseChatDirection)
                {
                    // Reverse direction: add at bottom (newest messages at bottom)
                    ChatMessages.Add(message);
                }
                else
                {
                    // Normal direction: add at top (newest messages at top)
                    ChatMessages.Insert(0, message);
                }
                
                // Maintain message limit
                while (ChatMessages.Count > MAX_MESSAGES_IN_CHAT)
                {
                    if (_reverseChatDirection)
                    {
                        // Remove oldest (from beginning)
                        ChatMessages.RemoveAt(0);
                    }
                    else
                    {
                        // Remove oldest (from end)
                        ChatMessages.RemoveAt(ChatMessages.Count - 1);
                    }
                }
                
                // Auto-scroll if enabled
                if (_isAutoScrollEnabled)
                {
                    ScrollToAppropriatePosition();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding chat message");
            }
        }

        private void ScrollToAppropriatePosition()
        {
            try
            {
                var scrollViewer = ChatScrollViewer;
                if (scrollViewer == null) return;
                
                if (_reverseChatDirection)
                {
                    // Reverse direction: scroll to bottom for newest messages
                    scrollViewer.ScrollToEnd();
                }
                else
                {
                    // Normal direction: scroll to top for newest messages
                    scrollViewer.ScrollToTop();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scrolling to appropriate position");
            }
        }

        private void ReverseChatDirectionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // The ReverseChatDirection property setter will handle saving to configuration and refreshing
            _logger.LogDebug("Reverse chat direction toggled via menu: {ReverseChatDirection}", ReverseChatDirection);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateScrollButtonText()
        {
            ScrollButtonText = _reverseChatDirection ? "📄 Scroll to Bottom" : "📄 Scroll to Top";
        }

        private void UpdateWindowTitle()
        {
            // Simple implementation - can be enhanced later if needed
            Title = "Multi-Platform Chat Viewer";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
    }
}
