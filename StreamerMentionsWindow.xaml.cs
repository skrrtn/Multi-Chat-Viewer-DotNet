using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public partial class StreamerMentionsWindow : Window, INotifyPropertyChanged
    {
        private readonly ILogger<StreamerMentionsWindow> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly MultiChannelManager _multiChannelManager;
        private string _currentChannelName;
        private string _statusMessage = "No channel selected";
        private double _chatFontSize = 12.0; // Default font size
        private bool _showTimestamps = true; // Default to showing timestamps
        private bool _scrollToTopButtonVisible = false;
        private bool _isAutoScrollEnabled = true;
        private ScrollViewer _mentionsScrollViewer;
        
        // Performance management
        private const int MAX_MENTIONS_IN_CHAT = 500;

        public ObservableCollection<ChatMessage> MentionMessages { get; } = [];

        public event PropertyChangedEventHandler PropertyChanged;

        // Properties for data binding
        public string CurrentChannelName
        {
            get => _currentChannelName;
            set
            {
                _currentChannelName = value;
                OnPropertyChanged(nameof(CurrentChannelName));
                UpdateWindowTitle();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public double ChatFontSize
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
            }
        }

        public bool ScrollToTopButtonVisible
        {
            get => _scrollToTopButtonVisible;
            set
            {
                _scrollToTopButtonVisible = value;
                OnPropertyChanged(nameof(ScrollToTopButtonVisible));
            }
        }

        public int MentionCount => MentionMessages.Count;

        private ScrollViewer MentionsScrollViewer 
        { 
            get 
            {
                if (_mentionsScrollViewer == null)
                {
                    if (this.FindName("MentionsListBox") is ListBox mentionsListBox)
                    {
                        _mentionsScrollViewer = GetScrollViewer(mentionsListBox);
                    }
                }
                return _mentionsScrollViewer;
            } 
        }

        public StreamerMentionsWindow(IServiceProvider serviceProvider, string channelName = null)
        {
            InitializeComponent();
            
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = _serviceProvider.GetRequiredService<ILogger<StreamerMentionsWindow>>();
            _multiChannelManager = _serviceProvider.GetRequiredService<MultiChannelManager>();
            
            DataContext = this;

            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);

            // Set initial status based on active channels
            UpdateStatusForActiveChannels();
            UpdateWindowTitle();

            // Load settings from configuration
            LoadSettings();

            // Subscribe to message events
            _multiChannelManager.MessageReceived += OnMessageReceived;
            
            // Subscribe to channel events to update status when channels are added/removed
            _multiChannelManager.ChannelConnected += OnChannelConnected;
            _multiChannelManager.ChannelDisconnected += OnChannelDisconnected;
            _multiChannelManager.ChannelRemoved += OnChannelRemoved;

            // Set up scroll event handling
            this.Loaded += (s, e) => {
                if (this.FindName("MentionsListBox") is ListBox mentionsListBox)
                {
                    var scrollViewer = GetScrollViewer(mentionsListBox);
                    if (scrollViewer != null)
                    {
                        scrollViewer.ScrollChanged += MentionsScrollViewer_ScrollChanged;
                        scrollViewer.PreviewMouseWheel += MentionsScrollViewer_PreviewMouseWheel;
                    }
                }
            };

            // Enable Ctrl+Scroll zoom functionality
            this.PreviewKeyDown += StreamerMentionsWindow_PreviewKeyDown;
            this.PreviewMouseWheel += StreamerMentionsWindow_PreviewMouseWheel;

            _logger.LogInformation("StreamerMentionsWindow initialized for channel: {Channel}", channelName);
        }

        private async void LoadSettings()
        {
            try
            {
                var configService = _serviceProvider.GetService<UnifiedConfigurationService>();
                if (configService != null)
                {
                    await configService.LoadConfigurationAsync();
                    ShowTimestamps = configService.GetShowTimestamps();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load settings from configuration");
            }
        }

        private void OnMessageReceived(object sender, (string Channel, ChatMessage Message) args)
        {
            // Get all active channels from the MultiChannelManager
            var activeChannels = _multiChannelManager.GetFollowedChannels();
            
            if (!activeChannels.Any())
            {
                // Update status if no channels are active
                if (StatusMessage == "No channel selected")
                {
                    // Already showing correct message
                    return;
                }
                Dispatcher.Invoke(() => StatusMessage = "No channels active");
                return;
            }

            // Check if this message mentions any of the active channel names
            var mentionedChannel = GetMentionedChannel(args.Message, activeChannels);
            if (!string.IsNullOrEmpty(mentionedChannel))
            {
                Dispatcher.Invoke(() =>
                {
                    // Set the source channel for display
                    args.Message.SourceChannel = args.Channel;
                    
                    if (_isAutoScrollEnabled)
                    {
                        // Add to the top of the list (most recent first)
                        MentionMessages.Insert(0, args.Message);
                        
                        // Maintain message limit for performance
                        if (MentionMessages.Count > MAX_MENTIONS_IN_CHAT)
                        {
                            var messagesToRemove = MentionMessages.Count - MAX_MENTIONS_IN_CHAT;
                            for (int i = 0; i < messagesToRemove; i++)
                            {
                                MentionMessages.RemoveAt(MentionMessages.Count - 1);
                            }
                        }

                        // Auto-scroll to top to show latest mention
                        var scrollViewer = MentionsScrollViewer;
                        scrollViewer?.ScrollToTop();
                    }

                    // Update mention count
                    OnPropertyChanged(nameof(MentionCount));
                    
                    // Update status message to show which channel was mentioned
                    var channelCount = activeChannels.Count;
                    StatusMessage = $"Latest mention (@{mentionedChannel}): {args.Message.Timestamp:HH:mm:ss} | Monitoring {channelCount} channel{(channelCount != 1 ? "s" : "")}";
                    
                    // Update no mentions visibility
                    UpdateNoMentionsVisibility();
                });
            }
            else if (activeChannels.Any())
            {
                // Update status to show we're monitoring channels but no recent mentions
                Dispatcher.Invoke(() =>
                {
                    var channelCount = activeChannels.Count;
                    var channelNames = string.Join(", ", activeChannels.Select(c => c.Name));
                    StatusMessage = $"Monitoring @mentions for {channelCount} channel{(channelCount != 1 ? "s" : "")}: {channelNames}";
                });
            }
        }

        private static string GetMentionedChannel(ChatMessage message, System.Collections.Generic.List<FollowedChannel> activeChannels)
        {
            if (message?.ParsedMessage == null || !activeChannels.Any())
                return null;

            // Check each active channel to see if it's explicitly mentioned with @
            foreach (var channel in activeChannels)
            {
                var channelName = channel.Name;
                
                // Only check for explicit @mentions in the parsed message
                bool hasExplicitMention = message.ParsedMessage.Any(part => 
                    part.IsMention && 
                    string.Equals(part.MentionedUsername, channelName, StringComparison.OrdinalIgnoreCase));

                if (hasExplicitMention)
                    return channelName;
            }

            return null;
        }

        private static bool IsMentionForChannel(ChatMessage message, string channelName)
        {
            if (message?.ParsedMessage == null || string.IsNullOrEmpty(channelName))
                return false;

            // Only check for explicit @mentions in the parsed message
            bool hasExplicitMention = message.ParsedMessage.Any(part => 
                part.IsMention && 
                string.Equals(part.MentionedUsername, channelName, StringComparison.OrdinalIgnoreCase));

            return hasExplicitMention;
        }

        public void SetChannel(string channelName)
        {
            // This method is maintained for backward compatibility but now updates status for all channels
            UpdateStatusForActiveChannels();
            
            // Clear existing mentions when channel context changes
            MentionMessages.Clear();
            OnPropertyChanged(nameof(MentionCount));
            
            // Show no mentions message initially
            UpdateNoMentionsVisibility();
            
            _logger.LogInformation("Updated StreamerMentionsWindow to monitor all active channels (SetChannel called with {Channel})", channelName);
        }

        private void UpdateStatusForActiveChannels()
        {
            var activeChannels = _multiChannelManager.GetFollowedChannels();
            
            if (!activeChannels.Any())
            {
                StatusMessage = "No channels active";
            }
            else
            {
                var channelCount = activeChannels.Count;
                var channelNames = string.Join(", ", activeChannels.Select(c => c.Name));
                StatusMessage = $"Monitoring @mentions for {channelCount} channel{(channelCount != 1 ? "s" : "")}: {channelNames}";
            }
        }

        private void UpdateNoMentionsVisibility()
        {
            var noMentionsTextBlock = this.FindName("NoMentionsTextBlock") as TextBlock;
            if (noMentionsTextBlock != null)
            {
                noMentionsTextBlock.Visibility = MentionMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateWindowTitle()
        {
            var activeChannels = _multiChannelManager.GetFollowedChannels();
            
            if (!activeChannels.Any())
            {
                Title = "Streamer Mentions - No Active Channels";
            }
            else if (activeChannels.Count == 1)
            {
                Title = $"Streamer Mentions - {activeChannels.First().Name}";
            }
            else
            {
                Title = $"Streamer Mentions - {activeChannels.Count} Channels";
            }
        }

        private static ScrollViewer GetScrollViewer(DependencyObject o)
        {
            if (o is ScrollViewer scrollViewer)
                return scrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);
                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        // Scroll event handlers
        private void MentionsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // Show scroll to top button when not at the top
                ScrollToTopButtonVisible = scrollViewer.VerticalOffset > 100;
                
                // Enable auto-scroll when user scrolls to top
                if (scrollViewer.VerticalOffset == 0)
                {
                    _isAutoScrollEnabled = true;
                }
            }
        }

        private void MentionsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && e.Delta < 0) // Scrolling down
            {
                _isAutoScrollEnabled = false;
                ScrollToTopButtonVisible = true;
            }
        }

        // Font scaling menu handlers
        private void FontScale50_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = 6.0; // 50% of 12
        }

        private void FontScale75_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = 9.0; // 75% of 12
        }

        private void FontScaleDefault_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = 12.0; // 100%
        }

        private void FontScale125_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = 15.0; // 125% of 12
        }

        private void FontScale150_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = 18.0; // 150% of 12
        }

        private void FontScale200_Click(object sender, RoutedEventArgs e)
        {
            ChatFontSize = 24.0; // 200% of 12
        }

        private void ShowTimestampsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowTimestamps = !ShowTimestamps;
        }

        private void ClearMentions_Click(object sender, RoutedEventArgs e)
        {
            MentionMessages.Clear();
            OnPropertyChanged(nameof(MentionCount));
            UpdateNoMentionsVisibility();
            StatusMessage = $"Mentions cleared for @{CurrentChannelName}";
        }

        private void ScrollToTopButton_Click(object sender, RoutedEventArgs e)
        {
            var scrollViewer = MentionsScrollViewer;
            scrollViewer?.ScrollToTop();
            _isAutoScrollEnabled = true;
            ScrollToTopButtonVisible = false;
        }

        // Ctrl+Scroll zoom functionality
        private void StreamerMentionsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Store Ctrl key state for mouse wheel events
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                _isCtrlPressed = true;
            }
        }

        private bool _isCtrlPressed = false;

        private void StreamerMentionsWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_isCtrlPressed || (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Adjust font size with Ctrl+Mouse Wheel
                double newSize = ChatFontSize + (e.Delta > 0 ? 1 : -1);
                
                // Clamp font size between 6 and 36
                newSize = Math.Max(6, Math.Min(36, newSize));
                
                ChatFontSize = newSize;
                e.Handled = true;
            }
        }

        // Mention click event handler for opening user messages window
        private void HighlightedTextBlock_MentionClick(object sender, RoutedEventArgs e)
        {
            if (e is MentionClickEventArgs mentionArgs && !string.IsNullOrEmpty(mentionArgs.MentionedUsername))
            {
                try
                {
                    _logger.LogInformation("Opening user messages window for mentioned user: {Username}", mentionArgs.MentionedUsername);
                    
                    var userMessageService = _serviceProvider.GetService<UserMessageLookupService>();
                    if (userMessageService != null)
                    {
                        var userMessagesWindow = new UserMessagesWindow(
                            userMessageService,
                            _serviceProvider.GetRequiredService<ILogger<UserMessagesWindow>>(),
                            mentionArgs.MentionedUsername,
                            CurrentChannelName
                        )
                        {
                            Owner = this
                        };
                        
                        userMessagesWindow.Show();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening user messages window for mentioned user: {Username}", mentionArgs.MentionedUsername);
                    MessageBox.Show(
                        $"Error opening user messages window for {mentionArgs.MentionedUsername}.\n\nError: {ex.Message}",
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
            // Unsubscribe from events
            if (_multiChannelManager != null)
            {
                _multiChannelManager.MessageReceived -= OnMessageReceived;
                _multiChannelManager.ChannelConnected -= OnChannelConnected;
                _multiChannelManager.ChannelDisconnected -= OnChannelDisconnected;
                _multiChannelManager.ChannelRemoved -= OnChannelRemoved;
            }
            
            _logger.LogInformation("StreamerMentionsWindow closed - was monitoring multiple channels");
            base.OnClosed(e);
        }

        private void OnChannelConnected(object sender, string channelName)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatusForActiveChannels();
                UpdateWindowTitle();
                _logger.LogDebug("StreamerMentionsWindow updated for connected channel: {Channel}", channelName);
            });
        }

        private void OnChannelDisconnected(object sender, string channelName)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatusForActiveChannels();
                UpdateWindowTitle();
                _logger.LogDebug("StreamerMentionsWindow updated for disconnected channel: {Channel}", channelName);
            });
        }

        private void OnChannelRemoved(object sender, string channelName)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatusForActiveChannels();
                UpdateWindowTitle();
                _logger.LogDebug("StreamerMentionsWindow updated for removed channel: {Channel}", channelName);
            });
        }
    }
}
