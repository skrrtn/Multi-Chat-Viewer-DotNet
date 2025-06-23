using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public class ChannelSettings
    {
        public bool LoggingEnabled { get; set; } = true;
        public DateTime LastModified { get; set; } = DateTime.Now;
    }    public class ChannelSettingsManager(ILogger<ChannelSettingsManager> logger)
    {
        private readonly ILogger<ChannelSettingsManager> _logger = logger;
        private readonly string _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "channel_settings.json");
        private Dictionary<string, ChannelSettings> _channelSettings = [];
        
        // Cache JsonSerializerOptions to avoid creating new instances
        private static readonly JsonSerializerOptions _jsonOptions = new() 
        { 
            WriteIndented = true 
        };

        public async Task LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    _channelSettings = JsonSerializer.Deserialize<Dictionary<string, ChannelSettings>>(json) 
                                      ?? [];
                    _logger.LogInformation("Loaded settings for {Count} channels", _channelSettings.Count);
                }else
                {
                    _channelSettings = [];
                    _logger.LogInformation("No existing settings file found, starting with default settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading channel settings, using defaults");
                _channelSettings = [];
            }
        }        public async Task SaveSettingsAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_channelSettings, _jsonOptions);
                await File.WriteAllTextAsync(_settingsFilePath, json);
                _logger.LogInformation("Saved settings for {Count} channels", _channelSettings.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving channel settings");
            }
        }

        public bool GetLoggingEnabled(string channelName)
        {
            var normalizedChannel = channelName.ToLower();
            if (_channelSettings.TryGetValue(normalizedChannel, out var settings))
            {
                return settings.LoggingEnabled;
            }
            
            // Default to enabled for new channels
            return true;
        }        public async Task SetLoggingEnabledAsync(string channelName, bool enabled)
        {
            var normalizedChannel = channelName.ToLower();
            
            if (!_channelSettings.TryGetValue(normalizedChannel, out var settings))
            {
                settings = new ChannelSettings();
                _channelSettings[normalizedChannel] = settings;
            }
            
            settings.LoggingEnabled = enabled;
            settings.LastModified = DateTime.Now;
            
            await SaveSettingsAsync();
            _logger.LogInformation("Updated logging setting for channel {Channel}: {Enabled}", channelName, enabled);
        }

        public void EnsureChannelExists(string channelName)
        {
            var normalizedChannel = channelName.ToLower();
            if (!_channelSettings.ContainsKey(normalizedChannel))
            {
                _channelSettings[normalizedChannel] = new ChannelSettings
                {
                    LoggingEnabled = true,
                    LastModified = DateTime.Now
                };
            }
        }

        public Dictionary<string, ChannelSettings> GetAllSettings()
        {
            return new Dictionary<string, ChannelSettings>(_channelSettings);
        }
    }
}
