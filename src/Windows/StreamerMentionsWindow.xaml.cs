using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;

namespace MultiChatViewer
{
    public partial class StreamerMentionsWindow : Window, INotifyPropertyChanged
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly MultiChannelManager _multiChannelManager;
        private string _currentChannelName;
        private double _chatFontSize = 12.0; // Default font size
        private bool _showTimestamps = true; // Default to showing timestamps (matches main window)
        private bool _timestampExplicitlySet = false; // Track if timestamp was explicitly set in constructor
        private bool _reverseChatDirectionExplicitlySet = false; // Track if reverse chat direction was explicitly set in constructor
        private bool _scrollToTopButtonVisible = false;
        private bool _isAutoScrollEnabled = true;
        private ScrollViewer _mentionsScrollViewer;
        private readonly Queue<ChatMessage> _pendingMentions = new();
        private int _pendingMentionCount = 0;
        
        // Performance management
        private const int MAX_MENTIONS_IN_CHAT = 500;

        private bool _reverseChatDirection = false; // Default to newest messages at top

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
                
                // Do NOT save to configuration - the main window is the source of truth
                // The StreamerMentionsWindow should only read from configuration, not write to it
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

        public int PendingMentionCount
        {
            get => _pendingMentionCount;
            set
            {
                _pendingMentionCount = value;
                OnPropertyChanged(nameof(PendingMentionCount));
            }
        }

        public bool ReverseChatDirection
        {
            get => _reverseChatDirection;
            set
            {
                _reverseChatDirection = value;
                OnPropertyChanged(nameof(ReverseChatDirection));
                
                // Do NOT save to configuration - the main window is the source of truth
                // The StreamerMentionsWindow should only read from configuration, not write to it
                
                // Refresh mentions to apply new direction
                RefreshMentionsDirection();
                
                // Update scroll button text
                UpdateScrollButtonText();
            }
        }

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

        private string _scrollButtonText = "📄 Scroll to Top"; // Default scroll button text

        public StreamerMentionsWindow(IServiceProvider serviceProvider, string channelName = null, bool? showTimestamps = null, bool? reverseChatDirection = null)
        {
            InitializeComponent();
            
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _multiChannelManager = _serviceProvider.GetRequiredService<MultiChannelManager>();
            
            DataContext = this;

            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);

            // If showTimestamps is provided, use it immediately before loading other settings
            if (showTimestamps.HasValue)
            {
                _showTimestamps = showTimestamps.Value;
                _timestampExplicitlySet = true;
            }
            
            // If reverseChatDirection is provided, use it immediately before loading other settings
            if (reverseChatDirection.HasValue)
            {
                _reverseChatDirection = reverseChatDirection.Value;
                _reverseChatDirectionExplicitlySet = true;
            }

            // Load settings from configuration (this will NOT override explicitly set settings)
            LoadSettings();

            // Subscribe to message events
            _multiChannelManager.MessageReceived += OnMessageReceived;
            
            // Subscribe to channel events
            _multiChannelManager.ChannelConnected += OnChannelConnected;
            _multiChannelManager.ChannelDisconnected += OnChannelDisconnected;
            _multiChannelManager.ChannelAdded += OnChannelAdded;
            _multiChannelManager.ChannelRemoved += OnChannelRemoved;
            _multiChannelManager.ChannelViewingToggled += OnChannelViewingToggled;

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
            this.PreviewKeyUp += StreamerMentionsWindow_PreviewKeyUp;
            this.PreviewMouseWheel += StreamerMentionsWindow_PreviewMouseWheel;
        }

        private async void LoadSettings()
        {
            try
            {
                var configService = _serviceProvider.GetService<UnifiedConfigurationService>();
                if (configService != null)
                {
                    await configService.LoadConfigurationAsync();
                    
                    // Only load the timestamp setting from config if it wasn't explicitly set in constructor
                    if (!_timestampExplicitlySet)
                    {
                        ShowTimestamps = configService.GetShowTimestamps();
                    }
                    
                    // Only load reverse chat direction setting from config if it wasn't explicitly set in constructor
                    if (!_reverseChatDirectionExplicitlySet)
                    {
                        ReverseChatDirection = configService.GetReverseChatDirection();
                    }
                    
                    // Always update scroll button text based on current setting
                    UpdateScrollButtonText();
                }
            }
            catch (Exception)
            {
                // Settings loading failed, continue with defaults
            }
        }

        private void OnMessageReceived(object sender, (string Channel, ChatMessage Message) args)
        {
            // Get only channels that are enabled for multi-view (ViewingEnabled = true)
            var activeChannels = _multiChannelManager.GetFollowedChannels()
                .Where(c => c.ViewingEnabled)
                .ToList();
            
            if (!activeChannels.Any())
            {
                return;
            }

            // Check if this message mentions any of the viewing-enabled channel names
            var mentionedChannel = GetMentionedChannel(args.Message, activeChannels);
            if (!string.IsNullOrEmpty(mentionedChannel))
            {
                Dispatcher.Invoke(() =>
                {
                    // Set the source channel for display
                    args.Message.SourceChannel = args.Channel;
                    
                    if (_isAutoScrollEnabled)
                    {
                        // Add using helper method that respects direction
                        AddMentionMessage(args.Message);
                    }
                    else
                    {
                        // Auto-scroll is disabled, queue the mention instead
                        _pendingMentions.Enqueue(args.Message);
                        PendingMentionCount = _pendingMentions.Count;
                    }

                    // Update mention count
                    OnPropertyChanged(nameof(MentionCount));
                    
                    // Update no mentions visibility
                    UpdateNoMentionsVisibility();
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
            // Clear existing mentions when channel context changes
            MentionMessages.Clear();
            OnPropertyChanged(nameof(MentionCount));
            
            // Show no mentions message initially
            UpdateNoMentionsVisibility();
        }

        private void UpdateNoMentionsVisibility()
        {
            var noMentionsTextBlock = this.FindName("NoMentionsTextBlock") as TextBlock;
            if (noMentionsTextBlock != null)
            {
                noMentionsTextBlock.Visibility = MentionMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                // Check scroll position based on chat direction
                const double scrollThreshold = 100.0;
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

                // Show scroll button when not at new message position
                ScrollToTopButtonVisible = !isAtNewMessagePosition;
                
                // Enable auto-scroll when user scrolls to new message position
                if (isAtNewMessagePosition)
                {
                    // Process pending mentions and re-enable auto-scroll
                    ProcessPendingMentions();
                    _isAutoScrollEnabled = true;
                    ScrollToTopButtonVisible = false;
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
            _pendingMentions.Clear();
            PendingMentionCount = 0;
            _isAutoScrollEnabled = true;
            ScrollToTopButtonVisible = false;
            OnPropertyChanged(nameof(MentionCount));
            UpdateNoMentionsVisibility();
        }

        private void ScrollToTopButton_Click(object sender, RoutedEventArgs e)
        {
            var scrollViewer = MentionsScrollViewer;
            if (scrollViewer != null)
            {
                // Process pending mentions first, then scroll
                ProcessPendingMentions();
                scrollViewer.ScrollToTop();
                _isAutoScrollEnabled = true;
                ScrollToTopButtonVisible = false;
            }
        }

        // Ctrl+Scroll zoom functionality
        private void StreamerMentionsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // This event handler is kept for potential future use
            // Currently, we rely on Keyboard.Modifiers for real-time state checking
        }

        private void StreamerMentionsWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            // This event handler is kept for potential future use
            // Currently, we rely on Keyboard.Modifiers for real-time state checking
        }

        private void StreamerMentionsWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Only adjust font size when Ctrl key is currently pressed
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
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
                    var userMessageService = _serviceProvider.GetService<UserMessageLookupService>();
                    if (userMessageService != null)
                    {
                        // Get logger for UserMessagesWindow - it still uses logging
                        var logger = _serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<UserMessagesWindow>>();
                        
                        // Use the source channel from the clicked message if available, otherwise use current channel context
                        string targetChannel = null;
                        if (!string.IsNullOrEmpty(mentionArgs.SourceChannel))
                        {
                            // Construct the complete channel identifier including platform from the message source
                            targetChannel = $"{mentionArgs.SourceChannel.ToLower()}_{mentionArgs.SourcePlatform.ToString().ToLower()}";
                        }
                        else if (!string.IsNullOrEmpty(CurrentChannelName))
                        {
                            // Fall back to current channel context
                            targetChannel = CurrentChannelName;
                        }
                        
                        var userMessagesWindow = new UserMessagesWindow(
                            userMessageService,
                            logger,
                            mentionArgs.MentionedUsername,
                            targetChannel
                        )
                        {
                            Owner = this
                        };
                        
                        userMessagesWindow.Show();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error opening user messages window for {mentionArgs.MentionedUsername}.\n\nError: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }

        // Username click event handler for opening user messages window
        private void HighlightedTextBlock_UsernameClick(object sender, RoutedEventArgs e)
        {
            if (e is UsernameClickEventArgs usernameArgs && !string.IsNullOrEmpty(usernameArgs.Username))
            {
                try
                {
                    var userMessageService = _serviceProvider.GetService<UserMessageLookupService>();
                    if (userMessageService != null)
                    {
                        // Get logger for UserMessagesWindow - it still uses logging
                        var logger = _serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<UserMessagesWindow>>();
                        
                        // Use the source channel from the clicked message if available, otherwise use current channel context
                        string targetChannel = null;
                        if (!string.IsNullOrEmpty(usernameArgs.SourceChannel))
                        {
                            // Construct the complete channel identifier including platform from the message source
                            targetChannel = $"{usernameArgs.SourceChannel.ToLower()}_{usernameArgs.SourcePlatform.ToString().ToLower()}";
                        }
                        else if (!string.IsNullOrEmpty(CurrentChannelName))
                        {
                            // Fall back to current channel context
                            targetChannel = CurrentChannelName;
                        }
                        
                        var userMessagesWindow = new UserMessagesWindow(
                            userMessageService,
                            logger,
                            usernameArgs.Username,
                            targetChannel
                        )
                        {
                            Owner = this
                        };
                        
                        userMessagesWindow.Show();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
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
            // Unsubscribe from events
            if (_multiChannelManager != null)
            {
                _multiChannelManager.MessageReceived -= OnMessageReceived;
                _multiChannelManager.ChannelConnected -= OnChannelConnected;
                _multiChannelManager.ChannelDisconnected -= OnChannelDisconnected;
                _multiChannelManager.ChannelAdded -= OnChannelAdded;
                _multiChannelManager.ChannelRemoved -= OnChannelRemoved;
                _multiChannelManager.ChannelViewingToggled -= OnChannelViewingToggled;
            }
            
            base.OnClosed(e);
        }

        private void OnChannelConnected(object sender, string channelName)
        {
            // No action needed - status updates removed
        }

        private void OnChannelDisconnected(object sender, string channelName)
        {
            // No action needed - status updates removed
        }

        private void OnChannelRemoved(object sender, string channelName)
        {
            // No action needed - status updates removed
        }

        private void OnChannelAdded(object sender, string channelName)
        {
            // No action needed - status updates removed
        }

        private void OnChannelViewingToggled(object sender, (string Channel, bool ViewingEnabled) args)
        {
            // No action needed - status updates removed
        }

        /// <summary>
        /// Updates the timestamp setting to match the main window's setting
        /// This should be called by the MainWindow when the timestamp setting changes
        /// </summary>
        public void UpdateTimestampSetting(bool showTimestamps)
        {
            _showTimestamps = showTimestamps;
            OnPropertyChanged(nameof(ShowTimestamps));
        }

        private void ProcessPendingMentions()
        {
            if (_pendingMentions.Count == 0) return;

            // Get all pending mentions
            var mentionsToAdd = new List<ChatMessage>();
            while (_pendingMentions.Count > 0)
            {
                mentionsToAdd.Add(_pendingMentions.Dequeue());
            }

            // Add each mention using the helper method that respects direction
            foreach (var mention in mentionsToAdd)
            {
                AddMentionMessage(mention);
            }

            // Reset pending count
            PendingMentionCount = 0;

            // Update mention count and visibility
            OnPropertyChanged(nameof(MentionCount));
            UpdateNoMentionsVisibility();
        }

        private void RefreshMentionsDirection()
        {
            try
            {
                // Store current mentions
                var currentMentions = MentionMessages.ToList();
                
                // Clear and re-add mentions in the correct order
                MentionMessages.Clear();
                
                if (_reverseChatDirection)
                {
                    // Reverse direction: oldest first, newest at bottom
                    var sortedMentions = currentMentions.OrderBy(m => m.Timestamp).ToList();
                    foreach (var mention in sortedMentions)
                    {
                        MentionMessages.Add(mention);
                    }
                }
                else
                {
                    // Normal direction: newest first, newest at top
                    var sortedMentions = currentMentions.OrderByDescending(m => m.Timestamp).ToList();
                    foreach (var mention in sortedMentions)
                    {
                        MentionMessages.Add(mention);
                    }
                }
                
                // Scroll to the appropriate position
                ScrollToAppropriateMentionPosition();
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                System.Diagnostics.Debug.WriteLine($"Error refreshing mentions direction: {ex.Message}");
            }
        }

        private void AddMentionMessage(ChatMessage message)
        {
            try
            {
                if (_reverseChatDirection)
                {
                    // Reverse direction: add at bottom (newest messages at bottom)
                    MentionMessages.Add(message);
                }
                else
                {
                    // Normal direction: add at top (newest messages at top)
                    MentionMessages.Insert(0, message);
                }
                
                // Maintain message limit
                while (MentionMessages.Count > MAX_MENTIONS_IN_CHAT)
                {
                    if (_reverseChatDirection)
                    {
                        // Remove oldest (from beginning)
                        MentionMessages.RemoveAt(0);
                    }
                    else
                    {
                        // Remove oldest (from end)
                        MentionMessages.RemoveAt(MentionMessages.Count - 1);
                    }
                }
                
                // Auto-scroll if enabled
                if (_isAutoScrollEnabled)
                {
                    ScrollToAppropriateMentionPosition();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding mention message: {ex.Message}");
            }
        }

        private void ScrollToAppropriateMentionPosition()
        {
            try
            {
                var scrollViewer = MentionsScrollViewer;
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
                System.Diagnostics.Debug.WriteLine($"Error scrolling to appropriate mention position: {ex.Message}");
            }
        }

        public void UpdateReverseChatDirection(bool reverseChatDirection)
        {
            ReverseChatDirection = reverseChatDirection;
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

        private void UpdateScrollButtonText()
        {
            ScrollButtonText = _reverseChatDirection ? "📄 Scroll to Bottom" : "📄 Scroll to Top";
        }
    }
}
