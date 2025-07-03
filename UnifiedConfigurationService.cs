using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public class AppConfiguration
    {
        public Dictionary<string, ChannelConfig> Channels { get; set; } = [];
        public List<string> BlacklistedUsers { get; set; } = [];
        public string ConfigVersion { get; set; } = "1.3";
        public DateTime LastSaved { get; set; } = DateTime.Now;
        
        // Kick credentials (encrypted)
        public string KickClientId { get; set; } = string.Empty;
        public string KickClientSecret { get; set; } = string.Empty;
    }

    public class ChannelConfig
    {
        public bool LoggingEnabled { get; set; } = true;
        public Platform Platform { get; set; } = Platform.Twitch;
    }

    public class UnifiedConfigurationService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ILogger<UnifiedConfigurationService> _logger;
        private readonly string _configFilePath;
        private readonly object _lock = new();

        // In-memory storage for last modified times (not persisted)
        private readonly Dictionary<string, DateTime> _channelLastModified = [];

        private AppConfiguration _config = new();

        public UnifiedConfigurationService(ILogger<UnifiedConfigurationService> logger)
        {
            _logger = logger;
            _configFilePath = Path.Combine(AppContext.BaseDirectory, "app_config.json");
            _logger.LogInformation("UnifiedConfigurationService initialized with path: {Path}", _configFilePath);
        }

        public event EventHandler<string> UserAdded;
        public event EventHandler<string> UserRemoved;

        #region Configuration Loading/Saving

        public async Task LoadConfigurationAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    _logger.LogInformation("No configuration file found, using defaults");
                    await MigrateFromOldConfigsAsync();
                    return;
                }

                var json = await File.ReadAllTextAsync(_configFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogInformation("Configuration file is empty, using defaults");
                    return;
                }

                // Check if we need to migrate from the old format with followedChannels array
                var migrationNeeded = await MigrateFromFollowedChannelsArrayAsync(json);
                
                if (!migrationNeeded)
                {
                    // No migration needed, load normally
                    lock (_lock)
                    {
                        _config = JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
                    }
                }

                _logger.LogInformation("Loaded configuration: {ChannelCount} channels, {BlacklistCount} blacklisted users",
                    _config.Channels.Count, _config.BlacklistedUsers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration from {Path}, using defaults", _configFilePath);
                _config = new AppConfiguration();
            }
        }

        public async Task SaveConfigurationAsync()
        {
            try
            {
                AppConfiguration configToSave;
                lock (_lock)
                {
                    _config.LastSaved = DateTime.Now;
                    configToSave = _config;
                }

                var json = JsonSerializer.Serialize(configToSave, JsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);

                _logger.LogInformation("Saved configuration: {ChannelCount} channels, {BlacklistCount} blacklisted users",
                    configToSave.Channels.Count, configToSave.BlacklistedUsers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving configuration to {Path}", _configFilePath);
                throw;
            }
        }

        #endregion

        #region Channel Settings

        public bool GetLoggingEnabled(string channelName)
        {
            var normalizedChannel = channelName.ToLower();
            lock (_lock)
            {
                if (_config.Channels.TryGetValue(normalizedChannel, out var channelConfig))
                {
                    return channelConfig.LoggingEnabled;
                }
            }
            
            // Default to enabled for new channels
            return true;
        }

        public async Task SetLoggingEnabledAsync(string channelName, bool enabled)
        {
            var normalizedChannel = channelName.ToLower();
            
            lock (_lock)
            {
                if (!_config.Channels.TryGetValue(normalizedChannel, out var channelConfig))
                {
                    channelConfig = new ChannelConfig();
                    _config.Channels[normalizedChannel] = channelConfig;
                }
                
                channelConfig.LoggingEnabled = enabled;
            }

            // Update in-memory last modified time
            _channelLastModified[normalizedChannel] = DateTime.Now;
            
            await SaveConfigurationAsync();
            _logger.LogInformation("Updated logging setting for channel {Channel}: {Enabled}", channelName, enabled);
        }

        public void EnsureChannelExists(string channelName)
        {
            var normalizedChannel = channelName.ToLower();
            lock (_lock)
            {
                if (!_config.Channels.ContainsKey(normalizedChannel))
                {
                    _config.Channels[normalizedChannel] = new ChannelConfig
                    {
                        LoggingEnabled = true
                    };
                    _channelLastModified[normalizedChannel] = DateTime.Now;
                }
            }
        }

        public Dictionary<string, ChannelConfig> GetAllChannelSettings()
        {
            lock (_lock)
            {
                return new Dictionary<string, ChannelConfig>(_config.Channels);
            }
        }

        public DateTime GetChannelLastModified(string channelName)
        {
            var normalizedChannel = channelName.ToLower();
            return _channelLastModified.TryGetValue(normalizedChannel, out var lastModified) 
                ? lastModified 
                : DateTime.Now;
        }

        #endregion

        #region Followed Channels

        public List<string> GetFollowedChannels()
        {
            lock (_lock)
            {
                return new List<string>(_config.Channels.Keys);
            }
        }

        public async Task AddFollowedChannelAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            lock (_lock)
            {
                if (!_config.Channels.ContainsKey(normalizedChannel))
                {
                    _config.Channels[normalizedChannel] = new ChannelConfig
                    {
                        LoggingEnabled = true,
                        Platform = Platform.Twitch // Default to Twitch for new channels
                    };
                }
                else
                {
                    _logger.LogWarning("Channel {Channel} already exists in followed channels", normalizedChannel);
                    return;
                }
            }

            await SaveConfigurationAsync();
            _logger.LogInformation("Added channel {Channel} to followed channels", normalizedChannel);
        }

        public async Task RemoveFollowedChannelAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            bool removed;
            lock (_lock)
            {
                removed = _config.Channels.Remove(normalizedChannel);
            }

            if (removed)
            {
                await SaveConfigurationAsync();
                _logger.LogInformation("Removed channel {Channel} from followed channels", normalizedChannel);
            }
            else
            {
                _logger.LogWarning("Channel {Channel} not found in followed channels", normalizedChannel);
            }
        }

        #endregion

        #region Kick Credentials

        public Task<string> GetKickClientIdAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(_config.KickClientId);
            }
        }

        public Task<string> GetKickClientSecretAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(_config.KickClientSecret);
            }
        }

        public async Task SetKickClientIdAsync(string clientId)
        {
            lock (_lock)
            {
                _config.KickClientId = clientId ?? string.Empty;
            }

            await SaveConfigurationAsync();
            _logger.LogInformation("Updated Kick Client ID");
        }

        public async Task SetKickClientSecretAsync(string clientSecret)
        {
            lock (_lock)
            {
                _config.KickClientSecret = clientSecret ?? string.Empty;
            }

            await SaveConfigurationAsync();
            _logger.LogInformation("Updated Kick Client Secret");
        }

        #endregion

        #region User Filtering

        public bool IsUserBlacklisted(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            lock (_lock)
            {
                return _config.BlacklistedUsers.Contains(username.Trim(), StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task<bool> AddBlacklistedUserAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            username = username.Trim().ToLowerInvariant();

            lock (_lock)
            {
                if (_config.BlacklistedUsers.Contains(username, StringComparer.OrdinalIgnoreCase))
                    return false;

                _config.BlacklistedUsers.Add(username);
            }

            await SaveConfigurationAsync();
            UserAdded?.Invoke(this, username);
            _logger.LogInformation("Added user '{Username}' to blacklist", username);
            return true;
        }

        public async Task<bool> RemoveBlacklistedUserAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            username = username.Trim().ToLowerInvariant();

            bool removed;
            lock (_lock)
            {
                removed = _config.BlacklistedUsers.RemoveAll(u => 
                    string.Equals(u, username, StringComparison.OrdinalIgnoreCase)) > 0;
            }

            if (removed)
            {
                await SaveConfigurationAsync();
                UserRemoved?.Invoke(this, username);
                _logger.LogInformation("Removed user '{Username}' from blacklist", username);
                return true;
            }

            return false;
        }

        public List<string> GetBlacklistedUsers()
        {
            lock (_lock)
            {
                return [.. _config.BlacklistedUsers.OrderBy(u => u)];
            }
        }

        public async Task ClearAllBlacklistedUsersAsync()
        {
            lock (_lock)
            {
                _config.BlacklistedUsers.Clear();
            }

            await SaveConfigurationAsync();
            _logger.LogInformation("Cleared all blacklisted users");
        }

        #endregion

        #region Migration from Old Config Files

        private async Task MigrateFromOldConfigsAsync()
        {
            _logger.LogInformation("Attempting to migrate from old configuration files...");

            var migrationTasks = new List<Task>
            {
                MigrateChannelSettingsAsync(),
                MigrateFollowedChannelsAsync(),
                MigrateUserFiltersAsync()
            };

            await Task.WhenAll(migrationTasks);

            // Save the migrated configuration
            if (_config.Channels.Count > 0 || _config.BlacklistedUsers.Count > 0)
            {
                await SaveConfigurationAsync();
                _logger.LogInformation("Migration completed successfully");
            }
            else
            {
                _logger.LogInformation("No old configuration files found to migrate");
            }
        }

        private async Task MigrateChannelSettingsAsync()
        {
            var oldPath = Path.Combine(AppContext.BaseDirectory, "channel_settings.json");
            if (!File.Exists(oldPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(oldPath);
                var oldSettings = JsonSerializer.Deserialize<Dictionary<string, ChannelSettings>>(json);
                
                if (oldSettings != null)
                {
                    foreach (var kvp in oldSettings)
                    {
                        _config.Channels[kvp.Key] = new ChannelConfig
                        {
                            LoggingEnabled = kvp.Value.LoggingEnabled,
                            Platform = Platform.Twitch // Default old channels to Twitch
                        };
                        _channelLastModified[kvp.Key] = kvp.Value.LastModified;
                    }
                    _logger.LogInformation("Migrated {Count} channel settings", oldSettings.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating channel settings from {Path}", oldPath);
            }
        }

        private async Task MigrateFollowedChannelsAsync()
        {
            var oldPath = Path.Combine(AppContext.BaseDirectory, "followed_channels.json");
            if (!File.Exists(oldPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(oldPath);
                var oldChannels = JsonSerializer.Deserialize<List<string>>(json);
                
                if (oldChannels != null)
                {
                    // Migrate old followed channels to the new channels structure
                    foreach (var channel in oldChannels)
                    {
                        var normalizedChannel = channel.ToLower().Replace("#", "");
                        if (!_config.Channels.ContainsKey(normalizedChannel))
                        {
                            _config.Channels[normalizedChannel] = new ChannelConfig
                            {
                                LoggingEnabled = true,
                                Platform = Platform.Twitch // Default to Twitch for migrated channels
                            };
                        }
                    }
                    _logger.LogInformation("Migrated {Count} followed channels", oldChannels.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating followed channels from {Path}", oldPath);
            }
        }

        private async Task MigrateUserFiltersAsync()
        {
            var oldPath = Path.Combine(AppContext.BaseDirectory, "user_filters.json");
            if (!File.Exists(oldPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(oldPath);
                var oldData = JsonSerializer.Deserialize<UserFiltersData>(json);
                
                if (oldData?.BlacklistedUsers != null)
                {
                    _config.BlacklistedUsers.AddRange(oldData.BlacklistedUsers.Where(u => !string.IsNullOrWhiteSpace(u)));
                    _logger.LogInformation("Migrated {Count} blacklisted users", oldData.BlacklistedUsers.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating user filters from {Path}", oldPath);
            }
        }

        private class UserFiltersData
        {
            public List<string> BlacklistedUsers { get; set; } = [];
        }

        /// <summary>
        /// Migrates from the old configuration format that had both 'channels' and 'followedChannels' arrays
        /// to the new unified format with just 'channels'
        /// </summary>
        /// <returns>True if migration was performed, false otherwise</returns>
        private async Task<bool> MigrateFromFollowedChannelsArrayAsync(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                // Check if this config has the old followedChannels array
                if (root.TryGetProperty("followedChannels", out var followedChannelsElement) && 
                    followedChannelsElement.ValueKind == JsonValueKind.Array)
                {
                    _logger.LogInformation("Detected old configuration format with followedChannels array, migrating...");

                    // Deserialize the old format to get both structures
                    var oldConfig = JsonSerializer.Deserialize<OldAppConfiguration>(json);
                    if (oldConfig?.FollowedChannels != null)
                    {
                        // Ensure all channels from followedChannels array exist in the channels dictionary
                        foreach (var channelName in oldConfig.FollowedChannels)
                        {
                            var normalizedChannel = channelName.ToLower().Replace("#", "");
                            if (!oldConfig.Channels.ContainsKey(normalizedChannel))
                            {
                                // Add missing channel with default settings
                                oldConfig.Channels[normalizedChannel] = new ChannelConfig
                                {
                                    LoggingEnabled = true,
                                    Platform = Platform.Twitch // Default to Twitch for legacy channels
                                };
                                _logger.LogInformation("Added missing channel from followedChannels array: {Channel}", normalizedChannel);
                            }
                        }

                        // Update the AppConfiguration to remove the followedChannels array
                        lock (_lock)
                        {
                            _config = new AppConfiguration
                            {
                                Channels = oldConfig.Channels,
                                BlacklistedUsers = oldConfig.BlacklistedUsers ?? [],
                                ConfigVersion = "1.3",
                                LastSaved = DateTime.Now,
                                KickClientId = oldConfig.KickClientId ?? string.Empty,
                                KickClientSecret = oldConfig.KickClientSecret ?? string.Empty
                            };
                        }

                        // Save the migrated configuration immediately to avoid re-migration
                        await SaveConfigurationAsync();
                        _logger.LogInformation("Successfully migrated configuration from old format with followedChannels array");
                        
                        return true; // Migration was performed
                    }
                }
                
                return false; // No migration needed
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during migration from followedChannels array format");
                return false;
            }
        }

        /// <summary>
        /// Temporary class to deserialize the old configuration format
        /// </summary>
        private class OldAppConfiguration
        {
            public Dictionary<string, ChannelConfig> Channels { get; set; } = [];
            public List<string> FollowedChannels { get; set; } = [];
            public List<string> BlacklistedUsers { get; set; } = [];
            public string ConfigVersion { get; set; } = "1.0";
            public DateTime LastSaved { get; set; } = DateTime.Now;
            public string KickClientId { get; set; } = string.Empty;
            public string KickClientSecret { get; set; } = string.Empty;
        }

        #endregion

        #region Channel Platform Support

        public Platform GetChannelPlatform(string channelName)
        {
            var normalizedChannel = channelName.ToLower();
            lock (_lock)
            {
                if (_config.Channels.TryGetValue(normalizedChannel, out var channelConfig))
                {
                    return channelConfig.Platform;
                }
            }
            
            // Default to Twitch for existing channels
            return Platform.Twitch;
        }

        public async Task SetChannelPlatformAsync(string channelName, Platform platform)
        {
            var normalizedChannel = channelName.ToLower();
            
            lock (_lock)
            {
                if (!_config.Channels.TryGetValue(normalizedChannel, out var channelConfig))
                {
                    channelConfig = new ChannelConfig();
                    _config.Channels[normalizedChannel] = channelConfig;
                }
                
                channelConfig.Platform = platform;
            }

            await SaveConfigurationAsync();
            _logger.LogInformation("Updated platform for channel {Channel}: {Platform}", channelName, platform);
        }

        #endregion
    }
}
