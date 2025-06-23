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

        public string Name { get; set; }        public bool IsConnected
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
        }

        public int MessageCount
        {
            get => _messageCount;
            set
            {
                _messageCount = value;
                OnPropertyChanged(nameof(MessageCount));
            }
        }

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

        public string DatabaseSizeFormatted => FormatFileSize(DatabaseSize);

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }    public class MultiChannelManager : IDisposable
    {        private readonly ILogger<MultiChannelManager> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ChannelSettingsManager _settingsManager;
        private readonly UserFilterService _userFilterService;
        private readonly FollowedChannelsStorage _followedChannelsStorage;
        private readonly ConcurrentDictionary<string, TwitchIrcClient> _clients = new();
        private readonly ConcurrentDictionary<string, ChatDatabaseService> _databases = new();
        private readonly ConcurrentDictionary<string, FollowedChannel> _followedChannels = new();public event EventHandler<(string Channel, ChatMessage Message)> MessageReceived;
        public event EventHandler<string> ChannelConnected;
        public event EventHandler<string> ChannelDisconnected;
        public event EventHandler<string> ChannelRemoved;
        public event EventHandler<(string Channel, string Error)> ChannelError;        public MultiChannelManager(ILogger<MultiChannelManager> logger, ILoggerFactory loggerFactory, ChannelSettingsManager settingsManager, UserFilterService userFilterService, FollowedChannelsStorage followedChannelsStorage)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _settingsManager = settingsManager;
            _userFilterService = userFilterService;
            _followedChannelsStorage = followedChannelsStorage;
        }

        public List<FollowedChannel> GetFollowedChannels()
        {
            return _followedChannels.Values.ToList();
        }        public async Task LoadFollowedChannelsAsync()
        {
            try
            {
                _logger.LogInformation("Loading channel settings...");
                await _settingsManager.LoadSettingsAsync();
                
                _logger.LogInformation("Discovering channels from existing database files...");
                
                var discoveredChannels = DiscoverChannelsFromDatabases();
                  foreach (var channelName in discoveredChannels)
                {
                    _logger.LogInformation("Processing discovered channel: {Channel}", channelName);
                    
                    // Ensure the channel exists in settings
                    _settingsManager.EnsureChannelExists(channelName);
                    
                    // Get the logging preference from settings
                    var loggingEnabled = _settingsManager.GetLoggingEnabled(channelName);                    if (loggingEnabled)
                    {
                        _logger.LogInformation("Auto-connecting to enabled channel: {Channel}", channelName);
                        var success = await AddChannelAsync(channelName, loggingEnabled);
                        if (!success)
                        {
                            _logger.LogWarning("Failed to auto-connect to channel {Channel}", channelName);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Skipping disabled channel: {Channel}", channelName);
                        await AddChannelOfflineAsync(channelName);
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
        }private List<string> DiscoverChannelsFromDatabases()
        {
            var channels = new List<string>();
            
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
                        channels.Add(fileName);
                        _logger.LogInformation("Discovered channel from database: {Channel} (from file: {File})", fileName, dbFile);
                    }
                }}
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering channels from databases");
            }
            
            _logger.LogInformation("Discovery complete. Found {Count} channels: {Channels}", 
                channels.Count, string.Join(", ", channels));
            return channels;
        }        public async Task<bool> AddChannelAsync(string channelName, bool? loggingEnabled = null)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            if (_followedChannels.ContainsKey(normalizedChannel))
            {
                _logger.LogWarning("Channel {Channel} is already being followed", normalizedChannel);
                return false; // This will be handled better in the UI layer
            }

            FollowedChannel followedChannel = null;
            TwitchIrcClient client = null;
            ChatDatabaseService database = null;
            string currentStep = "Initialization";            try
            {
                _logger.LogInformation("🚀 Starting to add channel: {Channel}", normalizedChannel);
                
                // Step 0: Validate channel exists on Twitch
                currentStep = "Validating channel on Twitch";
                _logger.LogInformation("Step 0: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                var isValidChannel = await IsValidTwitchChannelAsync(normalizedChannel);
                if (!isValidChannel)
                {
                    _logger.LogWarning("❌ Channel validation failed: {Channel} does not appear to exist on Twitch", normalizedChannel);
                    throw new ArgumentException($"Channel '{normalizedChannel}' does not appear to exist on Twitch. Please check the channel name and try again.");
                }
                _logger.LogInformation("✓ Step 0: Channel {Channel} validated successfully", normalizedChannel);
                
                // Step 1: Create followed channel entry
                currentStep = "Creating channel entry";
                _logger.LogInformation("Step 1: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel = new FollowedChannel
                {
                    Name = normalizedChannel,
                    Status = "Adding...",
                    LoggingEnabled = loggingEnabled ?? _settingsManager.GetLoggingEnabled(normalizedChannel)
                };
                _followedChannels[normalizedChannel] = followedChannel;
                _logger.LogInformation("✓ Step 1: Successfully created followed channel entry for: {Channel}", normalizedChannel);

                // Step 2: Create IRC client
                currentStep = "Creating IRC client";
                _logger.LogInformation("Step 2: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                client = new TwitchIrcClient(_loggerFactory.CreateLogger<TwitchIrcClient>());
                client.MessageReceived += (sender, message) => OnClientMessageReceived(normalizedChannel, message);
                client.Connected += (sender, channel) => OnClientConnected(normalizedChannel);
                client.Disconnected += (sender, args) => OnClientDisconnected(normalizedChannel);
                client.Error += (sender, error) => OnClientError(normalizedChannel, error);
                _clients[normalizedChannel] = client;
                _logger.LogInformation("✓ Step 2: Successfully created IRC client for: {Channel}", normalizedChannel);

                // Step 3: Create and initialize database
                currentStep = "Initializing database";
                _logger.LogInformation("Step 3: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                database = new ChatDatabaseService(_loggerFactory.CreateLogger<ChatDatabaseService>());
                await database.InitializeDatabaseAsync(normalizedChannel);
                _databases[normalizedChannel] = database;
                _logger.LogInformation("✓ Step 3: Successfully initialized database for: {Channel}", normalizedChannel);

                // Step 4: Update database size
                currentStep = "Reading database size";
                _logger.LogInformation("Step 4: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(normalizedChannel);
                _logger.LogInformation("✓ Step 4: Database size calculated for: {Channel} ({Size} bytes)", normalizedChannel, followedChannel.DatabaseSize);

                // Step 5: Connect to IRC
                currentStep = "Connecting to IRC";
                followedChannel.Status = "Connecting to IRC...";
                _logger.LogInformation("Step 5: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                
                try
                {
                    await client.ConnectAsync(normalizedChannel);
                    _logger.LogInformation("✓ Step 5: Successfully connected to IRC for: {Channel}", normalizedChannel);
                    followedChannel.Status = "ONLINE";
                    followedChannel.IsConnected = true;
                }
                catch (Exception connectEx)
                {
                    _logger.LogWarning(connectEx, "⚠ Step 5: IRC connection failed for channel: {Channel}, but continuing with offline mode. Error: {Error}", 
                        normalizedChannel, connectEx.Message);
                    followedChannel.Status = "Connection Failed - Will Retry";
                    followedChannel.IsConnected = false;
                      // Don't throw the exception - allow the channel to be added in offline mode
                }

                // Add to storage
                await _followedChannelsStorage.AddChannelAsync(normalizedChannel);

                _logger.LogInformation("✅ Successfully added channel: {Channel} (Connected: {IsConnected})", normalizedChannel, followedChannel.IsConnected);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to add channel: {Channel} at step: '{Step}'. Detailed error: {ErrorMessage}", 
                    normalizedChannel, currentStep, ex.Message);
                
                // Clean up any partial state
                try
                {
                    _logger.LogInformation("🧹 Starting cleanup for failed channel: {Channel}", normalizedChannel);
                    
                    if (_followedChannels.TryRemove(normalizedChannel, out _))
                    {
                        _logger.LogInformation("✓ Removed channel from followed channels: {Channel}", normalizedChannel);
                    }
                    
                    if (_clients.TryRemove(normalizedChannel, out var clientToDispose))
                    {
                        try
                        {
                            await clientToDispose.DisconnectAsync();
                            clientToDispose.Dispose();
                            _logger.LogInformation("✓ Cleaned up IRC client for: {Channel}", normalizedChannel);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogWarning(cleanupEx, "Failed to cleanup IRC client for: {Channel}", normalizedChannel);
                        }
                    }
                    
                    if (_databases.TryRemove(normalizedChannel, out var databaseToDispose))
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
                
                return false;
            }
        }public async Task<bool> RemoveChannelAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");

            try
            {
                // Disconnect and dispose IRC client
                if (_clients.TryRemove(normalizedChannel, out var client))
                {
                    await client.DisconnectAsync();
                    client.Dispose();
                }

                // Close database connection
                if (_databases.TryRemove(normalizedChannel, out var database))
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
                await ChatDatabaseService.DeleteDatabaseForChannelAsync(normalizedChannel, _logger);                // Remove from followed channels
                _followedChannels.TryRemove(normalizedChannel, out _);

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
        }private async void OnClientMessageReceived(string channel, ChatMessage message)
        {
            try
            {
                // Check if user is blacklisted - filter out both from display AND database logging
                if (_userFilterService.IsUserBlacklisted(message.Username))
                {
                    _logger.LogDebug("Filtered out message from blacklisted user {Username} on channel {Channel}", message.Username, channel);
                    return; // Don't process the message further
                }

                // Log to database only if logging is enabled for this channel
                if (_followedChannels.TryGetValue(channel, out var followedChannel))
                {
                    _logger.LogDebug("Background client message received for channel {Channel}: LoggingEnabled={LoggingEnabled}, IsConnected={IsConnected}", 
                        channel, followedChannel.LoggingEnabled, followedChannel.IsConnected);
                      if (followedChannel.LoggingEnabled && _databases.TryGetValue(channel, out var database))
                    {
                        await database.LogMessageAsync(message);
                        _logger.LogDebug("Background client logged message for channel {Channel}: {Username} - {Message}", 
                            channel, message.Username, message.Message);
                        
                        // Update database size after logging (every 10 messages to reduce overhead)
                        if (followedChannel.MessageCount % 10 == 0)
                        {
                            followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(channel);
                        }
                    }
                    else if (!followedChannel.LoggingEnabled)
                    {
                        _logger.LogDebug("Skipping message logging for channel {Channel} - logging disabled", channel);
                    }
                    
                    // Update followed channel stats (always update stats regardless of logging preference)
                    followedChannel.MessageCount++;
                    followedChannel.LastMessageTime = DateTime.Now;
                }
                else
                {
                    _logger.LogWarning("Received message for unknown channel {Channel}: {Username} - {Message}", 
                        channel, message.Username, message.Message);
                }

                // Notify listeners (this will notify MainWindow to display the message)
                MessageReceived?.Invoke(this, (channel, message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message for channel {Channel}", channel);
            }
        }private void OnClientConnected(string channel)
        {
            if (_followedChannels.TryGetValue(channel, out var followedChannel))
            {
                followedChannel.IsConnected = true;
                followedChannel.Status = "ONLINE";
            }
            ChannelConnected?.Invoke(this, channel);
        }        private void OnClientDisconnected(string channel)
        {
            _logger.LogInformation("OnClientDisconnected called for channel: {Channel}", channel);
            if (_followedChannels.TryGetValue(channel, out var followedChannel))
            {
                // Only update if it's not already marked as disconnected.
                // This prevents overwriting a manually set "OFFLINE" status.
                if (followedChannel.IsConnected)
                {
                    followedChannel.IsConnected = false;
                    followedChannel.Status = "OFFLINE";
                    _logger.LogInformation("Updated channel {Channel} status to OFFLINE in OnClientDisconnected", channel);
                }
            }
            ChannelDisconnected?.Invoke(this, channel);
        }

        private void OnClientError(string channel, string error)
        {
            if (_followedChannels.TryGetValue(channel, out var followedChannel))
            {
                followedChannel.IsConnected = false;
                followedChannel.Status = $"Error: {error}";
            }
            ChannelError?.Invoke(this, (channel, error));
        }        public async Task UpdateChannelLoggingAsync(string channelName, bool loggingEnabled)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            if (!_followedChannels.TryGetValue(normalizedChannel, out var followedChannel))
            {
                _logger.LogWarning("Channel {Channel} not found in followed channels during logging update", normalizedChannel);
                return;
            }

            var isConnected = followedChannel.IsConnected;

            _logger.LogInformation("Updating logging for {Channel} to {loggingEnabled}. IsConnected: {isConnected}", 
                normalizedChannel, loggingEnabled, isConnected);

            // Persist the new setting immediately.
            await _settingsManager.SetLoggingEnabledAsync(normalizedChannel, loggingEnabled);
            followedChannel.LoggingEnabled = loggingEnabled;

            // If turning logging ON
            if (loggingEnabled)
            {
                // And it was previously disconnected
                if (!isConnected)
                {
                    _logger.LogInformation("Connecting channel {Channel} because logging was enabled.", normalizedChannel);
                    await ConnectChannelAsync(normalizedChannel);
                }
            }
            // If turning logging OFF
            else
            {
                // And it is currently connected
                if (isConnected)
                {
                    _logger.LogInformation("Disconnecting channel {Channel} because logging was disabled.", normalizedChannel);
                    await DisconnectChannelAsync(normalizedChannel);
                }
                else
                {
                    // Ensure status is correct even if it was already disconnected.
                    followedChannel.IsConnected = false;
                    followedChannel.Status = "OFFLINE";
                }
            }
            
            _logger.LogInformation("Final state for channel {Channel}: LoggingEnabled={Enabled}, IsConnected={IsConnected}, Status={Status}", 
                normalizedChannel, followedChannel.LoggingEnabled, followedChannel.IsConnected, followedChannel.Status);
        }

        public async Task<bool> AddChannelOfflineAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            if (_followedChannels.ContainsKey(normalizedChannel))
            {
                _logger.LogWarning("Channel {Channel} is already being followed", normalizedChannel);
                return false;
            }

            try
            {
                // Create followed channel entry without connecting
                var followedChannel = new FollowedChannel
                {
                    Name = normalizedChannel,
                    Status = "OFFLINE",
                    LoggingEnabled = false,
                    IsConnected = false
                };
                _followedChannels[normalizedChannel] = followedChannel;                // Create database service (for when logging is re-enabled)
                var database = new ChatDatabaseService(_loggerFactory.CreateLogger<ChatDatabaseService>());
                await database.InitializeDatabaseAsync(normalizedChannel);
                _databases[normalizedChannel] = database;

                // Update database size
                followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(normalizedChannel);

                _logger.LogInformation("Added offline channel: {Channel}", normalizedChannel);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add offline channel: {Channel}", normalizedChannel);
                return false;
            }
        }

        private async Task ConnectChannelAsync(string normalizedChannel)
        {
            try
            {
                if (_followedChannels.TryGetValue(normalizedChannel, out var followedChannel))
                {
                    followedChannel.Status = "Connecting...";
                    
                    // Create IRC client if it doesn't exist
                    if (!_clients.ContainsKey(normalizedChannel))
                    {
                        var client = new TwitchIrcClient(_loggerFactory.CreateLogger<TwitchIrcClient>());
                        client.MessageReceived += (sender, message) => OnClientMessageReceived(normalizedChannel, message);
                        client.Connected += (sender, channel) => OnClientConnected(normalizedChannel);
                        client.Disconnected += (sender, args) => OnClientDisconnected(normalizedChannel);
                        client.Error += (sender, error) => OnClientError(normalizedChannel, error);
                        _clients[normalizedChannel] = client;
                    }
                    
                    // Connect to IRC
                    await _clients[normalizedChannel].ConnectAsync(normalizedChannel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect channel: {Channel}", normalizedChannel);
                if (_followedChannels.TryGetValue(normalizedChannel, out var followedChannel))
                {
                    followedChannel.Status = "Connection Failed";
                    followedChannel.IsConnected = false;
                }
            }
        }        private async Task DisconnectChannelAsync(string normalizedChannel)
        {
            try
            {
                _logger.LogInformation("Starting disconnect for channel: {Channel}", normalizedChannel);
                
                if (_followedChannels.TryGetValue(normalizedChannel, out var followedChannel))
                {
                    // Immediately update the UI-facing properties to OFFLINE.
                    // This ensures the UI is responsive and reflects the user's action instantly.
                    followedChannel.IsConnected = false;
                    followedChannel.Status = "OFFLINE";
                    _logger.LogInformation("Immediately set channel {Channel} status to OFFLINE.", normalizedChannel);

                    // Now, perform the actual disconnection and cleanup.
                    if (_clients.TryRemove(normalizedChannel, out var client))
                    {
                        _logger.LogInformation("Found IRC client for channel {Channel}, disconnecting and disposing...", normalizedChannel);
                        try
                        {
                            await client.DisconnectAsync();
                            _logger.LogInformation("DisconnectAsync completed for channel: {Channel}", normalizedChannel);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during IRC client disconnect for channel: {Channel}, will still attempt to dispose.", normalizedChannel);
                        }
                        finally
                        {
                            try
                            {
                                client.Dispose();
                                _logger.LogInformation("IRC client disposed for channel: {Channel}", normalizedChannel);
                            }
                            catch (Exception disposeEx)
                            {
                                _logger.LogError(disposeEx, "Error during IRC client dispose for channel: {Channel}", normalizedChannel);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No active IRC client found for channel {Channel} during disconnect.", normalizedChannel);
                    }

                    // Notify other parts of the application that this channel is now disconnected.
                    ChannelDisconnected?.Invoke(this, normalizedChannel);
                    _logger.LogInformation("Fired ChannelDisconnected event for channel: {Channel}", normalizedChannel);
                }
                else
                {
                    _logger.LogWarning("Channel {Channel} not found in followed channels during disconnect", normalizedChannel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disconnect channel: {Channel}", normalizedChannel);
                
                // Even if disconnect failed, ensure the channel is marked as offline
                if (_followedChannels.TryGetValue(normalizedChannel, out var followedChannel))
                {
                    if (followedChannel.IsConnected || followedChannel.Status != "OFFLINE")
                    {
                        followedChannel.IsConnected = false;
                        followedChannel.Status = "OFFLINE";
                        ChannelDisconnected?.Invoke(this, normalizedChannel);
                        _logger.LogInformation("Forced channel {Channel} to OFFLINE state after disconnect error", normalizedChannel);
                    }
                }
            }
        }        public Task UpdateDatabaseSizesAsync()
        {
            return Task.Run(() =>
            {
                foreach (var channel in _followedChannels.Values)
                {
                    try
                    {
                        var dbSize = ChatDatabaseService.GetDatabaseSizeByPath(channel.Name);
                        channel.DatabaseSize = dbSize;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating database size for channel: {Channel}", channel.Name);
                        channel.DatabaseSize = 0;
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
        }

        public async Task RetryConnectionAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            if (!_followedChannels.TryGetValue(normalizedChannel, out var followedChannel) || 
                !_clients.TryGetValue(normalizedChannel, out var client))
            {
                _logger.LogWarning("Cannot retry connection for {Channel} - channel not found", normalizedChannel);
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

        public async Task<ChannelAddResult> AddChannelWithDetailsAsync(string channelName, bool? loggingEnabled = null)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            if (_followedChannels.ContainsKey(normalizedChannel))
            {
                _logger.LogWarning("Channel {Channel} is already being followed", normalizedChannel);
                return new ChannelAddResult 
                { 
                    Success = false, 
                    ErrorMessage = "Channel is already being followed",
                    FailedStep = "Validation"
                };
            }

            FollowedChannel followedChannel = null;
            TwitchIrcClient client = null;
            ChatDatabaseService database = null;
            string currentStep = "Initialization";

            try
            {
                _logger.LogInformation("🚀 Starting to add channel: {Channel}", normalizedChannel);
                
                // Step 0: Validate channel exists on Twitch
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
                _logger.LogInformation("✓ Step 0: Channel {Channel} validated successfully", normalizedChannel);
                
                // Step 1: Create followed channel entry
                currentStep = "Creating channel entry";
                _logger.LogInformation("Step 1: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel = new FollowedChannel
                {
                    Name = normalizedChannel,
                    Status = "Adding...",
                    LoggingEnabled = loggingEnabled ?? _settingsManager.GetLoggingEnabled(normalizedChannel)
                };
                _followedChannels[normalizedChannel] = followedChannel;
                _logger.LogInformation("✓ Step 1: Successfully created followed channel entry for: {Channel}", normalizedChannel);

                // Step 2: Create IRC client
                currentStep = "Creating IRC client";
                _logger.LogInformation("Step 2: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                client = new TwitchIrcClient(_loggerFactory.CreateLogger<TwitchIrcClient>());
                client.MessageReceived += (sender, message) => OnClientMessageReceived(normalizedChannel, message);
                client.Connected += (sender, channel) => OnClientConnected(normalizedChannel);
                client.Disconnected += (sender, args) => OnClientDisconnected(normalizedChannel);
                client.Error += (sender, error) => OnClientError(normalizedChannel, error);
                _clients[normalizedChannel] = client;
                _logger.LogInformation("✓ Step 2: Successfully created IRC client for: {Channel}", normalizedChannel);

                // Step 3: Create and initialize database
                currentStep = "Initializing database";
                _logger.LogInformation("Step 3: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                database = new ChatDatabaseService(_loggerFactory.CreateLogger<ChatDatabaseService>());
                await database.InitializeDatabaseAsync(normalizedChannel);
                _databases[normalizedChannel] = database;
                _logger.LogInformation("✓ Step 3: Successfully initialized database for: {Channel}", normalizedChannel);

                // Step 4: Update database size
                currentStep = "Reading database size";
                _logger.LogInformation("Step 4: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                followedChannel.DatabaseSize = ChatDatabaseService.GetDatabaseSizeByPath(normalizedChannel);
                _logger.LogInformation("✓ Step 4: Database size calculated for: {Channel} ({Size} bytes)", normalizedChannel, followedChannel.DatabaseSize);

                // Step 5: Connect to IRC
                currentStep = "Connecting to IRC";
                followedChannel.Status = "Connecting to IRC...";
                _logger.LogInformation("Step 5: {Step} for channel: {Channel}", currentStep, normalizedChannel);
                
                try
                {
                    await client.ConnectAsync(normalizedChannel);
                    _logger.LogInformation("✓ Step 5: Successfully connected to IRC for: {Channel}", normalizedChannel);
                    followedChannel.Status = "ONLINE";
                    followedChannel.IsConnected = true;
                }
                catch (Exception connectEx)
                {
                    _logger.LogWarning(connectEx, "⚠ Step 5: IRC connection failed for channel: {Channel}, but continuing with offline mode. Error: {Error}", 
                        normalizedChannel, connectEx.Message);
                    followedChannel.Status = "Connection Failed - Will Retry";
                    followedChannel.IsConnected = false;                    
                    // Don't throw the exception - allow the channel to be added in offline mode
                }

                // Add to storage
                await _followedChannelsStorage.AddChannelAsync(normalizedChannel);

                _logger.LogInformation("✅ Successfully added channel: {Channel} (Connected: {IsConnected})", normalizedChannel, followedChannel.IsConnected);
                return new ChannelAddResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to add channel: {Channel} at step: '{Step}'. Detailed error: {ErrorMessage}", 
                    normalizedChannel, currentStep, ex.Message);
                
                // Clean up any partial state
                try
                {
                    _logger.LogInformation("🧹 Starting cleanup for failed channel: {Channel}", normalizedChannel);
                    
                    if (_followedChannels.TryRemove(normalizedChannel, out _))
                    {
                        _logger.LogInformation("✓ Removed channel from followed channels: {Channel}", normalizedChannel);
                    }
                    
                    if (_clients.TryRemove(normalizedChannel, out var clientToDispose))
                    {
                        try
                        {
                            await clientToDispose.DisconnectAsync();
                            clientToDispose.Dispose();
                            _logger.LogInformation("✓ Cleaned up IRC client for: {Channel}", normalizedChannel);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogWarning(cleanupEx, "Failed to cleanup IRC client for: {Channel}", normalizedChannel);
                        }
                    }
                    
                    if (_databases.TryRemove(normalizedChannel, out var databaseToDispose))
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
