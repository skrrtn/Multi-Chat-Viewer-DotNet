using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{    public class FollowedChannel : INotifyPropertyChanged
    {
        private bool _isConnected;
        private int _messageCount;
        private DateTime _lastMessageTime;
        private string _status = "Disconnected";
        private bool _loggingEnabled = true; // Default to enabled
        private long _databaseSize;
        private Platform _platform = Platform.Twitch; // Default to Twitch

        public string Name { get; set; }
        
        public Platform Platform
        {
            get => _platform;
            set
            {
                _platform = value;
                OnPropertyChanged(nameof(Platform));
                OnPropertyChanged(nameof(PlatformName));
                OnPropertyChanged(nameof(PlatformColor));
            }
        }

        public string PlatformName => Platform.ToString();
        
        public string PlatformColor => Platform switch
        {
            Platform.Twitch => "#9146FF", // Twitch purple
            Platform.Kick => "#53FC18",   // Kick green
            _ => "#569cd6"               // Default blue
        };

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ConnectionStatus));
            }
        }

        public bool LoggingEnabled
        {
            get => _loggingEnabled;
            set
            {
                _loggingEnabled = value;
                OnPropertyChanged(nameof(LoggingEnabled));
            }
        }        public int MessageCount
        {
            get => _messageCount;
            set
            {
                _messageCount = value;
                OnPropertyChanged(nameof(MessageCount));
                OnPropertyChanged(nameof(MessageCountFormatted));
            }
        }

        public string MessageCountFormatted => FormatMessageCount(MessageCount);

        public DateTime LastMessageTime
        {
            get => _lastMessageTime;
            set
            {
                _lastMessageTime = value;
                OnPropertyChanged(nameof(LastMessageTime));
                OnPropertyChanged(nameof(LastMessageTimeFormatted));
            }
        }        public string LastMessageTimeFormatted 
        {
            get
            {
                if (LastMessageTime == default)
                    return "";
                
                var timeSpan = DateTime.Now - LastMessageTime;
                var totalSeconds = timeSpan.TotalSeconds;
                
                if (totalSeconds < 5)
                    return "Now";
                else if (totalSeconds < 30)
                    return $"{(int)totalSeconds} seconds ago";
                else if (totalSeconds < 60)
                    return "30 seconds ago";
                else if (totalSeconds < 300) // 5 minutes
                {
                    var minutes = (int)timeSpan.TotalMinutes;
                    return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
                }
                else if (totalSeconds < 600) // 10 minutes
                    return "5 minutes ago";
                else if (totalSeconds < 900) // 15 minutes
                    return "10 minutes ago";
                else
                    return LastMessageTime.ToString("h:mm tt"); // AM/PM format
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }        public string StatusColor => IsConnected ? "#4CAF50" : "#f44336"; // Green if connected, red if not
        
        public string ConnectionStatus => IsConnected ? "ONLINE" : "OFFLINE";        public long DatabaseSize
        {
            get => _databaseSize;
            set
            {
                _databaseSize = value;
                OnPropertyChanged(nameof(DatabaseSize));
                OnPropertyChanged(nameof(DatabaseSizeFormatted));
            }
        }

        public string DatabaseSizeFormatted => FormatFileSize(DatabaseSize);        private static string FormatFileSize(long bytes)
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

        private static string FormatMessageCount(int count)
        {
            if (count < 1000)
                return count.ToString();
            else if (count < 1000000)
                return $"{count / 1000.0:0.#}k";
            else if (count < 1000000000)
                return $"{count / 1000000.0:0.#}M";
            else
                return $"{count / 1000000000.0:0.#}B";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }    public class MultiChannelManager(ILogger<MultiChannelManager> logger, ILoggerFactory loggerFactory, ChannelSettingsManager settingsManager, UserFilterService userFilterService, FollowedChannelsStorage followedChannelsStorage, UnifiedConfigurationService configService, KickCredentialsService kickCredentialsService) : IDisposable
    {        private readonly ConcurrentDictionary<string, FollowedChannel> _followedChannels = new();
        private readonly ConcurrentDictionary<string, IChatClient> _clients = new(); // Store both TwitchIrcClient and KickChatClient
        private readonly ConcurrentDictionary<string, ChatDatabaseService> _databases = new();
        private readonly ILogger<MultiChannelManager> _logger = logger;
        private readonly ILoggerFactory _loggerFactory = loggerFactory;
        private readonly ChannelSettingsManager _settingsManager = settingsManager;
        private readonly UserFilterService _userFilterService = userFilterService;
        private readonly FollowedChannelsStorage _followedChannelsStorage = followedChannelsStorage;
        private readonly UnifiedConfigurationService _configService = configService;
        private readonly KickCredentialsService _kickCredentialsService = kickCredentialsService;

        // ...existing code...
        public event EventHandler<(string Channel, ChatMessage Message)> MessageReceived;
        public event EventHandler<string> ChannelConnected;
        public event EventHandler<string> ChannelDisconnected;
        public event EventHandler<string> ChannelRemoved;
        public event EventHandler<(string Channel, string Error)> ChannelError;

        // Generate unique key for channel storage using both name and platform
        private static string GenerateChannelKey(string channelName, Platform platform)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            return $"{normalizedChannel}_{platform}";
        }

        // Extract channel name from a channel key
        private static string ExtractChannelName(string channelKey)
        {
            var lastUnderscoreIndex = channelKey.LastIndexOf('_');
            if (lastUnderscoreIndex > 0)
            {
                return channelKey.Substring(0, lastUnderscoreIndex);
            }
            return channelKey; // Fallback for legacy keys
        }

        // Extract platform from a channel key
        private static Platform ExtractPlatform(string channelKey)
        {
            var lastUnderscoreIndex = channelKey.LastIndexOf('_');
            if (lastUnderscoreIndex > 0 && lastUnderscoreIndex < channelKey.Length - 1)
            {
                var platformString = channelKey.Substring(lastUnderscoreIndex + 1);
                if (Enum.TryParse<Platform>(platformString, true, out var platform))
                {
                    return platform;
                }
            }
            return Platform.Twitch; // Fallback for legacy keys
        }

        public List<FollowedChannel> GetFollowedChannels()
        {
            return [.. _followedChannels.Values];
        }public async Task LoadFollowedChannelsAsync()
        {
            try
            {
                _logger.LogInformation("Loading channel settings...");
                await _settingsManager.LoadSettingsAsync();
                
                _logger.LogInformation("Discovering channels from existing database files...");
                
                var discoveredChannels = await ChatDatabaseService.DiscoverChannelsFromDatabasesAsync(_logger);
                  foreach (var (channelName, platform) in discoveredChannels)
                {
                    _logger.LogInformation("Processing discovered channel: {Channel} ({Platform})", channelName, platform);
                    
                    // Ensure the channel exists in settings
                    _settingsManager.EnsureChannelExists(channelName);
                    
                    // Use platform from database metadata instead of config default
                    // Store in config if not already there
                    var configPlatform = _configService.GetChannelPlatform(channelName);
                    if (configPlatform != platform)
                    {
                        _logger.LogInformation("Updating platform in config for channel {Channel}: {OldPlatform} -> {NewPlatform}", 
                            channelName, configPlatform, platform);
                        await _configService.SetChannelPlatformAsync(channelName, platform);
                    }
                    
                    var loggingEnabled = _settingsManager.GetLoggingEnabled(channelName);

                    // Always try to connect since we now always enable logging
                    _logger.LogInformation("Auto-connecting to channel: {Channel} on {Platform}", channelName, platform);
                    var success = await AddChannelAsync(channelName, platform, true);  // Always enable logging
                    if (!success)
                    {
                        _logger.LogWarning("Failed to auto-connect to channel {Channel} on {Platform}", channelName, platform);
                    }
                }
                
                // Save settings in case we added any new channels
                await _settingsManager.SaveSettingsAsync();
                
                _logger.LogInformation("Finished loading {Count} discovered channels", discoveredChannels.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading discovered channels");
            }
        }private async Task<List<(string ChannelName, Platform Platform)>> DiscoverChannelsFromDatabasesAsync()
        {
            var channels = new List<(string ChannelName, Platform Platform)>();
            
            try
            {
                // Try multiple possible locations for the db directory
                var possibleDbDirectories = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db"),
                    Path.Combine(Directory.GetCurrentDirectory(), "db"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "db"),
                    "db"
                };
                
                string dbDirectory = null;
                foreach (var dir in possibleDbDirectories)
                {
                    if (Directory.Exists(dir))
                    {
                        dbDirectory = dir;
                        break;
                    }
                }
                
                _logger.LogInformation("Looking for database files in directory: {Directory}", dbDirectory);
                
                if (string.IsNullOrEmpty(dbDirectory))
                {
                    _logger.LogInformation("Database directory not found, no channels to discover");
                    return channels;
                }

                var dbFiles = Directory.GetFiles(dbDirectory, "*.db");
                _logger.LogInformation("Found {Count} database files", dbFiles.Length);
                
                foreach (var dbFile in dbFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(dbFile);
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        // Get platform information from database metadata
                        var platform = await ChatDatabaseService.GetPlatformByPathAsync(fileName, _logger);
                        channels.Add((fileName, platform));
                        _logger.LogInformation("Discovered channel from database: {Channel} ({Platform}) (from file: {File})", fileName, platform, dbFile);
                    }
                }}
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering channels from databases");
            }
            
            _logger.LogInformation("Discovery complete. Found {Count} channels: {Channels}", 
                channels.Count, string.Join(", ", channels.Select(c => $"{c.ChannelName}({c.Platform})")));
            return channels;
        }

        public async Task<bool> AddChannelAsync(string channelName, Platform platform, bool? loggingEnabled = null)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            var channelKey = GenerateChannelKey(normalizedChannel, platform);
            
            if (_followedChannels.ContainsKey(channelKey))
            {
                _logger.LogWarning("Channel {Channel} on {Platform} is already being followed", normalizedChannel, platform);
                return false; // This will be handled better in the UI layer
            }

            FollowedChannel followedChannel = null;
            IChatClient client = null;
            ChatDatabaseService database = null;
            string currentStep = "Initialization";

            try
            {
                _logger.LogInformation("🚀 Starting to add channel: {Channel} on platform: {Platform}", normalizedChannel, platform);
                
                // Step 0: Validate channel exists
                currentStep = platform == Platform.Twitch ? "Validating channel on Twitch" : "Validating channel on Kick";
                _logger.LogInformation("Step 0: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                
                if (platform == Platform.Twitch)
                {
                    var isValidChannel = await IsValidTwitchChannelAsync(normalizedChannel);
                    if (!isValidChannel)
                    {
                        _logger.LogWarning("❌ Channel validation failed: {Channel} does not appear to exist on Twitch", normalizedChannel);
                        throw new ArgumentException($"Channel '{normalizedChannel}' does not appear to exist on Twitch. Please check the channel name and try again.");
                    }
                }
                else if (platform == Platform.Kick)
                {
                    // For Kick channels, we'll validate during connection since we need credentials first
                    _logger.LogInformation("Kick channel validation will be performed during connection for: {Channel}", normalizedChannel);
                }
                
                _logger.LogInformation("✓ Step 0: Channel {Channel} validated successfully", normalizedChannel);
                
                // Step 1: Create followed channel entry
                currentStep = "Creating channel entry";
                _logger.LogInformation("Step 1: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel = new FollowedChannel
                {
                    Name = normalizedChannel,
                    Platform = platform,
                    Status = "Adding...",
                    LoggingEnabled = true  // Always enable logging
                };
                _followedChannels[channelKey] = followedChannel;
                
                // Store platform in config
                await _configService.SetChannelPlatformAsync(normalizedChannel, platform);
                
                _logger.LogInformation("✓ Step 1: Successfully created followed channel entry for: {Channel}", normalizedChannel);

                // Step 2: Create platform-specific client
                currentStep = $"Creating {platform} client";
                _logger.LogInformation("Step 2: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                
                if (platform == Platform.Twitch)
                {
                    var twitchClient = new TwitchIrcClient(_loggerFactory.CreateLogger<TwitchIrcClient>());
                    twitchClient.MessageReceived += (sender, message) => OnClientMessageReceived(channelKey, message);
                    twitchClient.Connected += (sender, channel) => OnClientConnected(channelKey);
                    twitchClient.Disconnected += (sender, args) => OnClientDisconnected(channelKey);
                    twitchClient.Error += (sender, error) => OnClientError(channelKey, error);
                    client = twitchClient;
                }
                else if (platform == Platform.Kick)
                {
                    _logger.LogInformation("Creating Kick client for channel: {Channel}", normalizedChannel);
                    
                    // Create Kick client (credentials are optional for reading public chat)
                    var kickClient = new KickChatClient(_loggerFactory.CreateLogger<KickChatClient>());
                    kickClient.MessageReceived += (sender, message) => OnClientMessageReceived(channelKey, message);
                    kickClient.Connected += (sender, channel) => OnClientConnected(channelKey);
                    kickClient.Disconnected += (sender, args) => OnClientDisconnected(channelKey);
                    kickClient.Error += (sender, error) => OnClientError(channelKey, error);
                    client = kickClient;
                    
                    _logger.LogInformation("Kick client created successfully for channel: {Channel}", normalizedChannel);
                }
                
                _clients[channelKey] = client;
                _logger.LogInformation("✓ Step 2: Successfully created {Platform} client for: {Channel}", platform, normalizedChannel);

                // Step 3: Create and initialize database
                currentStep = "Initializing database";
                _logger.LogInformation("Step 3: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                database = new ChatDatabaseService(_loggerFactory.CreateLogger<ChatDatabaseService>());
                await database.InitializeDatabaseAsync(normalizedChannel, platform);
                _databases[channelKey] = database;
                _logger.LogInformation("✓ Step 3: Successfully initialized database for: {Channel}", normalizedChannel);

                // Step 3.5: Store platform metadata in database
                currentStep = "Storing platform metadata";
                _logger.LogInformation("Step 3.5: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                await database.SetPlatformAsync(platform);
                _logger.LogInformation("✓ Step 3.5: Stored platform metadata ({Platform}) for: {Channel}", platform, normalizedChannel);

                // Step 4: Update database size
                currentStep = "Reading database size";
                _logger.LogInformation("Step 4: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(normalizedChannel, platform);
                _logger.LogInformation("✓ Step 4: Database size calculated for: {Channel} ({Size} bytes)", normalizedChannel, followedChannel.DatabaseSize);

                // Step 4.5: Load existing message count from database
                currentStep = "Loading existing message count";
                _logger.LogInformation("Step 4.5: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel.MessageCount = await ChatDatabaseService.GetMessageCountByPathAsync(normalizedChannel, platform);
                _logger.LogInformation("✓ Step 4.5: Loaded existing message count for: {Channel} ({Count} messages)", normalizedChannel, followedChannel.MessageCount);

                // Step 5: Connect to platform
                currentStep = $"Connecting to {platform}";
                followedChannel.Status = $"Connecting to {platform}...";
                _logger.LogInformation("Step 5: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                
                try
                {
                    _logger.LogInformation("Attempting to connect to {Platform} channel: {Channel}", platform, normalizedChannel);
                    await client.ConnectAsync(normalizedChannel);
                    _logger.LogInformation("✓ Step 5: Successfully connected to {Platform} for: {Channel}", platform, normalizedChannel);
                    followedChannel.Status = "ONLINE";
                    followedChannel.IsConnected = true;
                    
                    // For Kick channels, ensure logging is enabled by default
                    if (platform == Platform.Kick && followedChannel.LoggingEnabled)
                    {
                        _logger.LogInformation("Kick channel {Channel} connected successfully and logging is enabled", normalizedChannel);
                    }
                }
                catch (Exception connectEx)
                {
                    _logger.LogError(connectEx, "❌ Step 5: {Platform} connection failed for channel: {Channel}. Error: {Error}", 
                        platform, normalizedChannel, connectEx.Message);
                    
                    // For Kick channels, provide more specific error information but be more lenient with timeouts
                    if (platform == Platform.Kick)
                    {
                        if (connectEx.Message.Contains("not found"))
                        {
                            // Channel doesn't exist - this is a real error
                            var kickErrorMessage = $"Failed to connect to Kick channel '{normalizedChannel}': {connectEx.Message}. Please check that the channel name is correct and that the channel exists on Kick.com.";
                            followedChannel.Status = $"Not Found: {connectEx.Message}";
                            followedChannel.IsConnected = false;
                            throw new Exception(kickErrorMessage, connectEx);
                        }
                        else if (connectEx.Message.Contains("timed out"))
                        {
                            // Connection timeout - allow the channel to be added but keep trying in background
                            _logger.LogWarning("Kick connection timed out for {Channel}, but channel will be added and connection will be retried in background", normalizedChannel);
                            followedChannel.Status = "Connecting...";
                            followedChannel.IsConnected = false;
                            // Don't throw - allow the channel to be added
                        }
                        else
                        {
                            // Other errors - set status but don't throw
                            followedChannel.Status = $"Connection Failed: {connectEx.Message}";
                            followedChannel.IsConnected = false;
                            _logger.LogWarning("Kick connection failed for {Channel}: {Error}, but channel will be added for retry", normalizedChannel, connectEx.Message);
                        }
                    }
                    else
                    {
                        // For Twitch channels, continue with offline mode
                        followedChannel.Status = "Connection Failed - Will Retry";
                        followedChannel.IsConnected = false;
                        // Don't throw the exception - allow the channel to be added in offline mode
                    }
                }

                // Add to storage
                await _followedChannelsStorage.AddChannelAsync(normalizedChannel);

                _logger.LogInformation("✅ Successfully added channel: {Channel} on {Platform} (Connected: {IsConnected})", normalizedChannel, platform, followedChannel.IsConnected);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to add channel: {Channel} on {Platform} at step: '{Step}'. Detailed error: {ErrorMessage}", 
                    normalizedChannel, platform, currentStep, ex.Message);
                
                // Clean up any partial state
                try
                {
                    _logger.LogInformation("🧹 Starting cleanup for failed channel: {Channel}", normalizedChannel);
                    
                    if (_followedChannels.TryRemove(channelKey, out _))
                    {
                        _logger.LogInformation("✓ Removed channel from followed channels: {Channel}", normalizedChannel);
                    }
                    
                    if (_clients.TryRemove(channelKey, out var clientToDispose))
                    {
                        try
                        {
                            await clientToDispose.DisconnectAsync();
                            clientToDispose.Dispose();
                            _logger.LogInformation("✓ Cleaned up client for: {Channel}", normalizedChannel);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogError(cleanupEx, "Failed to clean up client for: {Channel}", normalizedChannel);
                        }
                    }
                    
                    if (_databases.TryRemove(channelKey, out var databaseToDispose))
                    {
                        try
                        {
                            databaseToDispose.Dispose();
                            _logger.LogInformation("✓ Cleaned up database for: {Channel}", normalizedChannel);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogError(cleanupEx, "Failed to clean up database for: {Channel}", normalizedChannel);
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "❌ Failed to clean up partial state for channel: {Channel}", normalizedChannel);
                }
                
                return false;
            }
        }

        // Keep existing method for backwards compatibility, defaults to Twitch
        public async Task<bool> AddChannelAsync(string channelName, bool? loggingEnabled = null)
        {
            return await AddChannelAsync(channelName, Platform.Twitch, loggingEnabled);
        }

        public async Task<bool> RemoveChannelAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");

            try
            {
                // Find the channel to get its platform and generate the correct key
                var channelToRemove = _followedChannels.Values.FirstOrDefault(c => 
                    c.Name.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase));
                
                if (channelToRemove == null)
                {
                    _logger.LogWarning("Channel {Channel} not found in followed channels", normalizedChannel);
                    return false;
                }

                var platform = channelToRemove.Platform;
                var channelKey = GenerateChannelKey(normalizedChannel, platform);

                // Disconnect and dispose IRC client
                if (_clients.TryRemove(channelKey, out var client))
                {
                    await client.DisconnectAsync();
                    client.Dispose();
                }

                // Close database connection
                if (_databases.TryRemove(channelKey, out var database))
                {
                    await database.CloseConnectionAsync();
                    database.Dispose();
                }

                // Force garbage collection to ensure connections are closed
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Clear SQLite connection pools before deletion
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                // Delete the database file completely
                await ChatDatabaseService.DeleteDatabaseForChannelAsync(normalizedChannel, platform, _logger);                // Remove from followed channels
                _followedChannels.TryRemove(channelKey, out _);

                // Remove from storage
                await _followedChannelsStorage.RemoveChannelAsync(normalizedChannel);

                // Trigger the ChannelRemoved event
                ChannelRemoved?.Invoke(this, normalizedChannel);

                _logger.LogInformation("Successfully removed channel and deleted database: {Channel}", normalizedChannel);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing channel: {Channel}", normalizedChannel);
                return false;
            }
        }        public async Task DisconnectAllAsync()
        {
            var tasks = _clients.Keys.Select(DisconnectChannelAsync);
            await Task.WhenAll(tasks);
            
            // Close all database connections but don't delete the files
            foreach (var database in _databases.Values)
            {
                try
                {
                    await database.CloseConnectionAsync();
                    database.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing database connection during shutdown");
                }
            }
            
            _databases.Clear();
            _logger.LogInformation("Disconnected all channels and closed database connections for app shutdown");
        }        private async void OnClientMessageReceived(string channelKey, ChatMessage message)
        {
            try
            {
                // Check if user is blacklisted - filter out both from display AND database logging
                if (_userFilterService.IsUserBlacklisted(message.Username))
                {
                    _logger.LogDebug("Filtered out message from blacklisted user {Username} on channel {Channel}", message.Username, channelKey);
                    return; // Don't process the message further
                }

                // Log to database only if logging is enabled for this channel
                if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
                {
                    var channelName = ExtractChannelName(channelKey);
                    _logger.LogDebug("Background client message received for channel {Channel}: LoggingEnabled={LoggingEnabled}, IsConnected={IsConnected}", 
                        channelName, followedChannel.LoggingEnabled, followedChannel.IsConnected);
                      if (followedChannel.LoggingEnabled && _databases.TryGetValue(channelKey, out var database))
                    {
                        await database.LogMessageAsync(message);
                        _logger.LogDebug("Background client logged message for channel {Channel}: {Username} - {Message}", 
                            channelName, message.Username, message.Message);
                        
                        // Update database size after logging (every 10 messages to reduce overhead)
                        if (followedChannel.MessageCount % 10 == 0)
                        {
                            followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(channelName, followedChannel.Platform);
                        }
                    }
                    else if (!followedChannel.LoggingEnabled)
                    {
                        _logger.LogDebug("Skipping message logging for channel {Channel} - logging disabled", channelName);
                    }
                    
                    // Update followed channel stats (always update stats regardless of logging preference)
                    followedChannel.MessageCount++;
                    followedChannel.LastMessageTime = DateTime.Now;
                }
                else
                {
                    var channelName = ExtractChannelName(channelKey);
                    _logger.LogWarning("Received message for unknown channel {Channel}: {Username} - {Message}", 
                        channelName, message.Username, message.Message);
                }

                // Notify listeners (this will notify MainWindow to display the message)
                // Use the channel name (not the key) for the event
                var eventChannelName = ExtractChannelName(channelKey);
                MessageReceived?.Invoke(this, (eventChannelName, message));
            }
            catch (Exception ex)
            {
                var channelName = ExtractChannelName(channelKey);
                _logger.LogError(ex, "Error processing message for channel {Channel}", channelName);
            }
        }        private void OnClientConnected(string channelKey)
        {
            if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
            {
                followedChannel.IsConnected = true;
                followedChannel.Status = "ONLINE";
            }
            var channelName = ExtractChannelName(channelKey);
            ChannelConnected?.Invoke(this, channelName);
        }

        private void OnClientDisconnected(string channelKey)
        {
            var channelName = ExtractChannelName(channelKey);
            _logger.LogInformation("OnClientDisconnected called for channel: {Channel}", channelName);
            if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
            {
                // Only update if it's not already marked as disconnected.
                // This prevents overwriting a manually set "OFFLINE" status.
                if (followedChannel.IsConnected)
                {
                    followedChannel.IsConnected = false;
                    followedChannel.Status = "OFFLINE";
                    _logger.LogInformation("Updated channel {Channel} status to OFFLINE in OnClientDisconnected", channelName);
                }
            }
            ChannelDisconnected?.Invoke(this, channelName);
        }

        private void OnClientError(string channelKey, string error)
        {
            if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
            {
                followedChannel.IsConnected = false;
                followedChannel.Status = $"Error: {error}";
            }
            var channelName = ExtractChannelName(channelKey);
            ChannelError?.Invoke(this, (channelName, error));
        }

        public async Task<bool> AddChannelOfflineAsync(string channelName, Platform platform = Platform.Twitch)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            var channelKey = GenerateChannelKey(normalizedChannel, platform);
            
            if (_followedChannels.ContainsKey(channelKey))
            {
                _logger.LogWarning("Channel {Channel} on {Platform} is already being followed", normalizedChannel, platform);
                return false;
            }

            try
            {
                // Create followed channel entry without connecting
                var followedChannel = new FollowedChannel
                {
                    Name = normalizedChannel,
                    Platform = platform,
                    Status = "OFFLINE",
                    LoggingEnabled = true,  // Always enable logging
                    IsConnected = false
                };
                _followedChannels[channelKey] = followedChannel;
                
                // Store platform in config
                await _configService.SetChannelPlatformAsync(normalizedChannel, platform);                // Create database service (for when logging is re-enabled)
                var database = new ChatDatabaseService(_loggerFactory.CreateLogger<ChatDatabaseService>());
                await database.InitializeDatabaseAsync(normalizedChannel, platform);
                _databases[channelKey] = database;

                // Update database size
                followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(normalizedChannel, platform);

                // Load existing message count from database
                followedChannel.MessageCount = await ChatDatabaseService.GetMessageCountByPathAsync(normalizedChannel, platform);

                _logger.LogInformation("Added offline channel: {Channel} on {Platform}", normalizedChannel, platform);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add offline channel: {Channel} on {Platform}", normalizedChannel, platform);
                return false;
            }
        }

        private async Task ConnectChannelAsync(string channelKey)
        {
            try
            {
                if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
                {
                    followedChannel.Status = "Connecting...";
                    var extractedChannelName = ExtractChannelName(channelKey);
                    
                    // Create platform-specific client if it doesn't exist
                    if (!_clients.TryGetValue(channelKey, out var existingClient))
                    {
                        IChatClient client;
                        
                        if (followedChannel.Platform == Platform.Twitch)
                        {
                            var twitchClient = new TwitchIrcClient(_loggerFactory.CreateLogger<TwitchIrcClient>());
                            twitchClient.MessageReceived += (sender, message) => OnClientMessageReceived(channelKey, message);
                            twitchClient.Connected += (sender, channel) => OnClientConnected(channelKey);
                            twitchClient.Disconnected += (sender, args) => OnClientDisconnected(channelKey);
                            twitchClient.Error += (sender, error) => OnClientError(channelKey, error);
                            client = twitchClient;
                        }
                        else if (followedChannel.Platform == Platform.Kick)
                        {
                            // Create Kick client (credentials optional for reading public chat)
                            var kickClient = new KickChatClient(_loggerFactory.CreateLogger<KickChatClient>());
                            kickClient.MessageReceived += (sender, message) => OnClientMessageReceived(channelKey, message);
                            kickClient.Connected += (sender, channel) => OnClientConnected(channelKey);
                            kickClient.Disconnected += (sender, args) => OnClientDisconnected(channelKey);
                            kickClient.Error += (sender, error) => OnClientError(channelKey, error);
                            client = kickClient;
                        }
                        else
                        {
                            throw new NotSupportedException($"Platform {followedChannel.Platform} is not supported");
                        }
                        
                        _clients[channelKey] = client;
                        existingClient = client;
                    }
                    
                    // Connect to platform
                    _logger.LogInformation("Attempting to connect to {Platform} channel: {Channel}", followedChannel.Platform, extractedChannelName);
                    await existingClient.ConnectAsync(extractedChannelName);
                    
                    // If we reach here, the connection was successful
                    _logger.LogInformation("Successfully connected to {Platform} channel: {Channel}", followedChannel.Platform, extractedChannelName);
                    
                    // Ensure the status is updated (OnClientConnected might be called async)
                    if (!followedChannel.IsConnected)
                    {
                        followedChannel.IsConnected = true;
                        followedChannel.Status = "ONLINE";
                        _logger.LogInformation("Updated status for {Channel} to ONLINE", extractedChannelName);
                    }
                }
            }
            catch (Exception ex)
            {
                var extractedChannelName = ExtractChannelName(channelKey);
                _logger.LogError(ex, "Failed to connect channel: {Channel} - Error: {Error}", extractedChannelName, ex.Message);
                if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
                {
                    followedChannel.Status = "Connection Failed";
                    followedChannel.IsConnected = false;
                }
            }
        }        private async Task DisconnectChannelAsync(string channelKey)
        {
            try
            {
                var extractedChannelName = ExtractChannelName(channelKey);
                _logger.LogInformation("Starting disconnect for channel: {Channel}", extractedChannelName);
                
                if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
                {
                    // Immediately update the UI-facing properties to OFFLINE.
                    // This ensures the UI is responsive and reflects the user's action instantly.
                    followedChannel.IsConnected = false;
                    followedChannel.Status = "OFFLINE";
                    _logger.LogInformation("Immediately set channel {Channel} status to OFFLINE.", extractedChannelName);

                    // Now, perform the actual disconnection and cleanup.
                    if (_clients.TryRemove(channelKey, out var client))
                    {
                        _logger.LogInformation("Found IRC client for channel {Channel}, disconnecting and disposing...", extractedChannelName);
                        try
                        {
                            await client.DisconnectAsync();
                            _logger.LogInformation("DisconnectAsync completed for channel: {Channel}", extractedChannelName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during IRC client disconnect for channel: {Channel}, will still attempt to dispose.", extractedChannelName);
                        }
                        finally
                        {
                            try
                            {
                                client.Dispose();
                                _logger.LogInformation("IRC client disposed for channel: {Channel}", extractedChannelName);
                            }
                            catch (Exception disposeEx)
                            {
                                _logger.LogError(disposeEx, "Error during IRC client dispose for channel: {Channel}", extractedChannelName);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No active IRC client found for channel {Channel} during disconnect.", extractedChannelName);
                    }

                    // Notify other parts of the application that this channel is now disconnected.
                    ChannelDisconnected?.Invoke(this, extractedChannelName);
                    _logger.LogInformation("Fired ChannelDisconnected event for channel: {Channel}", extractedChannelName);
                }
                else
                {
                    _logger.LogWarning("Channel {Channel} not found in followed channels during disconnect", extractedChannelName);
                }
            }
            catch (Exception ex)
            {
                var extractedChannelName = ExtractChannelName(channelKey);
                _logger.LogError(ex, "Failed to disconnect channel: {Channel}", extractedChannelName);
                
                // Even if disconnect failed, ensure the channel is marked as offline
                if (_followedChannels.TryGetValue(channelKey, out var followedChannel))
                {
                    if (followedChannel.IsConnected || followedChannel.Status != "OFFLINE")
                    {
                        followedChannel.IsConnected = false;
                        followedChannel.Status = "OFFLINE";
                        ChannelDisconnected?.Invoke(this, extractedChannelName);
                        _logger.LogInformation("Forced channel {Channel} to OFFLINE state after disconnect error", extractedChannelName);
                    }
                }
            }
        }        public Task UpdateDatabaseStatsAsync()
        {
            return Task.Run(async () =>
            {
                foreach (var channel in _followedChannels.Values)
                {
                    try
                    {
                        var dbSize = ChatDatabaseService.GetDatabaseSizeByPath(channel.Name, channel.Platform);
                        channel.DatabaseSize = dbSize;
                        
                        var messageCount = await ChatDatabaseService.GetMessageCountByPathAsync(channel.Name, channel.Platform);
                        channel.MessageCount = messageCount;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating database stats for channel: {Channel}", channel.Name);
                        channel.DatabaseSize = 0;
                        channel.MessageCount = 0;
                    }
                }
            });
        }        public void Dispose()
        {
            try
            {
                DisconnectAllAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during MultiChannelManager disposal");
            }
            
            GC.SuppressFinalize(this);
        }

        public async Task RetryConnectionAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            // Find the channel to get its platform and generate the correct key
            var followedChannel = _followedChannels.Values.FirstOrDefault(c => 
                c.Name.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase));
            
            if (followedChannel == null)
            {
                _logger.LogWarning("Cannot retry connection for {Channel} - channel not found", normalizedChannel);
                return;
            }

            var channelKey = GenerateChannelKey(normalizedChannel, followedChannel.Platform);
            
            if (!_clients.TryGetValue(channelKey, out var client))
            {
                _logger.LogWarning("Cannot retry connection for {Channel} - client not found", normalizedChannel);
                return;
            }

            if (followedChannel.IsConnected)
            {
                _logger.LogInformation("Channel {Channel} is already connected", normalizedChannel);
                return;
            }

            try
            {
                followedChannel.Status = "Reconnecting...";
                _logger.LogInformation("Retrying IRC connection for: {Channel}", normalizedChannel);
                
                await client.ConnectAsync(normalizedChannel);
                
                followedChannel.Status = "ONLINE";
                followedChannel.IsConnected = true;
                _logger.LogInformation("Successfully reconnected to IRC for: {Channel}", normalizedChannel);
                ChannelConnected?.Invoke(this, normalizedChannel);
            }
            catch (Exception ex)
            {
                followedChannel.Status = "Connection Failed";
                followedChannel.IsConnected = false;
                _logger.LogWarning(ex, "Retry connection failed for channel: {Channel}", normalizedChannel);
                ChannelError?.Invoke(this, (normalizedChannel, ex.Message));
            }
        }

        private async Task<bool> IsValidTwitchChannelAsync(string channelName)
        {
            try
            {
                _logger.LogInformation("🔍 Validating Twitch channel: {Channel}", channelName);
                
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                // Use Twitch's public API to check if channel exists
                var url = $"https://www.twitch.tv/{channelName.ToLower()}";
                var response = await httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // Check if the page contains indicators that the channel exists
                    bool channelExists = content.Contains("\"login\"") || content.Contains("channelLogin") || !content.Contains("Sorry. Unless you've got a time machine");
                    
                    _logger.LogInformation("Channel validation result for {Channel}: {Exists}", channelName, channelExists);
                    return channelExists;
                }
                else
                {
                    _logger.LogWarning("Channel validation failed for {Channel}: HTTP {StatusCode}", channelName, response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not validate channel {Channel}: {Error}", channelName, ex.Message);
                // If we can't validate, assume it might be valid to avoid false negatives
                return true;
            }
        }

        public async Task<string> RunDiagnosticsAsync()
        {
            var diagnostics = new StringBuilder();
            diagnostics.AppendLine("🔧 TwitchChatViewer Diagnostics");
            diagnostics.AppendLine("=" + new string('=', 40));
            
            try
            {
                // Test 1: Database directory access
                diagnostics.AppendLine("\n📁 Database Directory Test:");
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                diagnostics.AppendLine($"   Directory: {dbDirectory}");
                
                if (!Directory.Exists(dbDirectory))
                {
                    Directory.CreateDirectory(dbDirectory);
                    diagnostics.AppendLine("   ✓ Created database directory");
                }
                else
                {
                    diagnostics.AppendLine("   ✓ Database directory exists");
                }
                
                // Test write permissions
                var testFile = Path.Combine(dbDirectory, "diagnostic_test.tmp");
                await File.WriteAllTextAsync(testFile, "test");
                File.Delete(testFile);
                diagnostics.AppendLine("   ✓ Write permissions verified");
                
                // Test 2: Network connectivity
                diagnostics.AppendLine("\n🌐 Network Connectivity Test:");
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await httpClient.GetAsync("https://www.twitch.tv");
                diagnostics.AppendLine($"   Twitch.tv: {response.StatusCode} ✓");
                
                // Test 3: IRC connectivity
                diagnostics.AppendLine("\n🔌 IRC Connectivity Test:");
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync("irc.chat.twitch.tv", 6667);
                var timeoutTask = Task.Delay(5000);
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == connectTask)
                {
                    diagnostics.AppendLine("   ✓ IRC server reachable");
                    tcpClient.Close();
                }
                else
                {
                    diagnostics.AppendLine("   ⚠ IRC server connection timeout");
                }
                
                // Test 4: Settings manager
                diagnostics.AppendLine("\n⚙️ Settings Manager Test:");
                await _settingsManager.LoadSettingsAsync();
                diagnostics.AppendLine("   ✓ Settings loaded successfully");
                
                diagnostics.AppendLine("\n✅ All diagnostic tests completed");
                
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"\n❌ Diagnostic error: {ex.Message}");
            }
            
            return diagnostics.ToString();
        }

        public class ChannelAddResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public string FailedStep { get; set; }
            public Exception Exception { get; set; }
        }

        public async Task<ChannelAddResult> AddChannelWithDetailsAsync(string channelName, Platform platform = Platform.Twitch, bool? loggingEnabled = null)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            var channelKey = GenerateChannelKey(normalizedChannel, platform);
            
            if (_followedChannels.ContainsKey(channelKey))
            {
                _logger.LogWarning("Channel {Channel} on {Platform} is already being followed", normalizedChannel, platform);
                return new ChannelAddResult 
                { 
                    Success = false, 
                    ErrorMessage = $"Channel is already being followed on {platform}",
                    FailedStep = "Validation"
                };
            }

            FollowedChannel followedChannel = null;
            IChatClient client = null;
            ChatDatabaseService database = null;
            string currentStep = "Initialization";

            try
            {
                _logger.LogInformation("🚀 Starting to add channel: {Channel} on {Platform}", normalizedChannel, platform);
                
                // Step 0: Validate channel exists
                if (platform == Platform.Twitch)
                {
                    currentStep = "Validating channel on Twitch";
                    _logger.LogInformation("Step 0: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                    var isValidChannel = await IsValidTwitchChannelAsync(normalizedChannel);
                    if (!isValidChannel)
                    {
                        var errorMsg = $"Channel '{normalizedChannel}' does not appear to exist on Twitch. Please check the channel name and try again.";
                        _logger.LogWarning("❌ Channel validation failed: {Channel} does not appear to exist on Twitch", normalizedChannel);
                        return new ChannelAddResult 
                        { 
                            Success = false, 
                            ErrorMessage = errorMsg,
                            FailedStep = currentStep
                        };
                    }
                }
                _logger.LogInformation("✓ Step 0: Channel {Channel} validated successfully", normalizedChannel);
                
                // Step 1: Create followed channel entry
                currentStep = "Creating channel entry";
                _logger.LogInformation("Step 1: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel = new FollowedChannel
                {
                    Name = normalizedChannel,
                    Platform = platform,
                    Status = "Adding...",
                    LoggingEnabled = true  // Always enable logging
                };
                _followedChannels[channelKey] = followedChannel;
                _logger.LogInformation("✓ Step 1: Successfully created followed channel entry for: {Channel}", normalizedChannel);

                // Step 2: Create platform-specific client
                currentStep = $"Creating {platform} client";
                _logger.LogInformation("Step 2: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                
                if (platform == Platform.Twitch)
                {
                    var twitchClient = new TwitchIrcClient(_loggerFactory.CreateLogger<TwitchIrcClient>());
                    twitchClient.MessageReceived += (sender, message) => OnClientMessageReceived(channelKey, message);
                    twitchClient.Connected += (sender, channel) => OnClientConnected(channelKey);
                    twitchClient.Disconnected += (sender, args) => OnClientDisconnected(channelKey);
                    twitchClient.Error += (sender, error) => OnClientError(channelKey, error);
                    client = twitchClient;
                }
                else if (platform == Platform.Kick)
                {
                    var kickClient = new KickChatClient(_loggerFactory.CreateLogger<KickChatClient>());
                    kickClient.MessageReceived += (sender, message) => OnClientMessageReceived(channelKey, message);
                    kickClient.Connected += (sender, channel) => OnClientConnected(channelKey);
                    kickClient.Disconnected += (sender, args) => OnClientDisconnected(channelKey);
                    kickClient.Error += (sender, error) => OnClientError(channelKey, error);
                    client = kickClient;
                }
                else
                {
                    throw new NotSupportedException($"Platform {platform} is not supported");
                }
                
                _clients[channelKey] = client;
                _logger.LogInformation("✓ Step 2: Successfully created {Platform} client for: {Channel}", platform, normalizedChannel);

                // Step 3: Create and initialize database
                currentStep = "Initializing database";
                _logger.LogInformation("Step 3: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                database = new ChatDatabaseService(_loggerFactory.CreateLogger<ChatDatabaseService>());
                await database.InitializeDatabaseAsync(normalizedChannel, platform);
                _databases[channelKey] = database;
                _logger.LogInformation("✓ Step 3: Successfully initialized database for: {Channel}", normalizedChannel);                // Step 4: Update database size
                currentStep = "Reading database size";
                _logger.LogInformation("Step 4: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(normalizedChannel, platform);
                _logger.LogInformation("✓ Step 4: Database size calculated for: {Channel} ({Size} bytes)", normalizedChannel, followedChannel.DatabaseSize);

                // Step 4.5: Load existing message count from database
                currentStep = "Loading existing message count";
                _logger.LogInformation("Step 4.5: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel.MessageCount = await ChatDatabaseService.GetMessageCountByPathAsync(normalizedChannel, platform);
                _logger.LogInformation("✓ Step 4.5: Loaded existing message count for: {Channel} ({Count} messages)", normalizedChannel, followedChannel.MessageCount);

                // Step 5: Connect to platform
                currentStep = $"Connecting to {platform}";
                followedChannel.Status = $"Connecting to {platform}...";
                _logger.LogInformation("Step 5: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                
                try
                {
                    await client.ConnectAsync(normalizedChannel);
                    _logger.LogInformation("✓ Step 5: Successfully connected to {Platform} for: {Channel}", platform, normalizedChannel);
                    followedChannel.Status = "ONLINE";
                    followedChannel.IsConnected = true;
                }
                catch (Exception connectEx)
                {
                    _logger.LogWarning(connectEx, "⚠ Step 5: {Platform} connection failed for channel: {Channel}, but continuing with offline mode. Error: {Error}", 
                        platform, normalizedChannel, connectEx.Message);
                    followedChannel.Status = "Connection Failed - Will Retry";
                    followedChannel.IsConnected = false;
                      // Don't throw the exception - allow the channel to be added in offline mode
                }

                // Add to storage
                await _followedChannelsStorage.AddChannelAsync(normalizedChannel);

                _logger.LogInformation("✅ Successfully added channel: {Channel} on {Platform} (Connected: {IsConnected})", normalizedChannel, platform, followedChannel.IsConnected);
                return new ChannelAddResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to add channel: {Channel} on {Platform} at step: '{Step}'. Detailed error: {ErrorMessage}", 
                    normalizedChannel, platform, currentStep, ex.Message);
                
                // Clean up any partial state
                try
                {
                    _logger.LogInformation("🧹 Starting cleanup for failed channel: {Channel}", normalizedChannel);
                    
                    if (_followedChannels.TryRemove(channelKey, out _))
                    {
                        _logger.LogInformation("✓ Removed channel from followed channels: {Channel}", normalizedChannel);
                    }
                    
                    if (_clients.TryRemove(channelKey, out var clientToDispose))
                    {
                        try
                        {
                            await clientToDispose.DisconnectAsync();
                            clientToDispose.Dispose();
                            _logger.LogInformation("✓ Cleaned up client for: {Channel}", normalizedChannel);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogWarning(cleanupEx, "Failed to cleanup client for: {Channel}", normalizedChannel);
                        }
                    }
                    
                    if (_databases.TryRemove(channelKey, out var databaseToDispose))
                    {
                        try
                        {
                            await databaseToDispose.CloseConnectionAsync();
                            _logger.LogInformation("✓ Cleaned up database for: {Channel}", normalizedChannel);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogWarning(cleanupEx, "Failed to cleanup database for: {Channel}", normalizedChannel);
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "❌ Failed to clean up partial state for channel: {Channel}", normalizedChannel);
                }
                
                return new ChannelAddResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message,
                    FailedStep = currentStep,
                    Exception = ex
                };
            }
        }
    }
}
