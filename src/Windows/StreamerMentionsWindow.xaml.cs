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
        private bool _scrollToTopButtonVisible = false;
        private bool _isAutoScrollEnabled = true;
        private ScrollViewer _mentionsScrollViewer;
        private readonly Queue<ChatMessage> _pendingMentions = new();
        private int _pendingMentionCount = 0;
        
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

        public StreamerMentionsWindow(IServiceProvider serviceProvider, string channelName = null, bool? showTimestamps = null)
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

            // Load settings from configuration (this will NOT override the timestamp setting if already set)
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
                // Show scroll to top button when not at the top
                ScrollToTopButtonVisible = scrollViewer.VerticalOffset > 100;
                
                // Enable auto-scroll when user scrolls to top
                if (scrollViewer.VerticalOffset == 0)
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
                        
                        var userMessagesWindow = new UserMessagesWindow(
                            userMessageService,
                            logger,
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
                        
                        var userMessagesWindow = new UserMessagesWindow(
                            userMessageService,
                            logger,
                            usernameArgs.Username,
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

            // Insert all pending mentions at the top in the correct order
            var mentionsToAdd = new List<ChatMessage>();
            while (_pendingMentions.Count > 0)
            {
                mentionsToAdd.Add(_pendingMentions.Dequeue());
            }

            // Insert mentions in reverse order so newest appears at top
            for (int i = mentionsToAdd.Count - 1; i >= 0; i--)
            {
                MentionMessages.Insert(0, mentionsToAdd[i]);
            }

            // Reset pending count
            PendingMentionCount = 0;

            // Maintain message limit for performance
            if (MentionMessages.Count > MAX_MENTIONS_IN_CHAT)
            {
                var messagesToRemove = MentionMessages.Count - MAX_MENTIONS_IN_CHAT;
                for (int i = 0; i < messagesToRemove; i++)
                {
                    MentionMessages.RemoveAt(MentionMessages.Count - 1);
                }
            }

            // Update mention count and visibility
            OnPropertyChanged(nameof(MentionCount));
            UpdateNoMentionsVisibility();
        }
    }
}
