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
        public List<string> FollowedChannels { get; set; } = [];
        public List<string> BlacklistedUsers { get; set; } = [];
        public string ConfigVersion { get; set; } = "1.0";
        public DateTime LastSaved { get; set; } = DateTime.Now;
    }

    public class ChannelConfig
    {
        public bool LoggingEnabled { get; set; } = true;
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

                lock (_lock)
                {
                    _config = JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
                }

                _logger.LogInformation("Loaded configuration: {ChannelCount} channels, {FollowedCount} followed, {BlacklistCount} blacklisted users",
                    _config.Channels.Count, _config.FollowedChannels.Count, _config.BlacklistedUsers.Count);
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

                _logger.LogInformation("Saved configuration: {ChannelCount} channels, {FollowedCount} followed, {BlacklistCount} blacklisted users",
                    configToSave.Channels.Count, configToSave.FollowedChannels.Count, configToSave.BlacklistedUsers.Count);
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
                return new List<string>(_config.FollowedChannels);
            }
        }

        public async Task AddFollowedChannelAsync(string channelName)
        {
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            lock (_lock)
            {
                if (!_config.FollowedChannels.Contains(normalizedChannel))
                {
                    _config.FollowedChannels.Add(normalizedChannel);
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
                removed = _config.FollowedChannels.Remove(normalizedChannel);
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
                return _config.BlacklistedUsers.OrderBy(u => u).ToList();
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
            if (_config.Channels.Count > 0 || _config.FollowedChannels.Count > 0 || _config.BlacklistedUsers.Count > 0)
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
                            LoggingEnabled = kvp.Value.LoggingEnabled
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
                    _config.FollowedChannels.AddRange(oldChannels);
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

        #endregion
    }
}
