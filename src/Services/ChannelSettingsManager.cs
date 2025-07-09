using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultiChatViewer
{
    public class ChannelSettings
    {
        public bool LoggingEnabled { get; set; } = true;
        public DateTime LastModified { get; set; } = DateTime.Now;
    }

    public class ChannelSettingsManager(ILogger<ChannelSettingsManager> logger, UnifiedConfigurationService configService)
    {
        private readonly ILogger<ChannelSettingsManager> _logger = logger;
        private readonly UnifiedConfigurationService _configService = configService;

        public async Task LoadSettingsAsync()
        {
            // This is now handled by the unified configuration service
            await _configService.LoadConfigurationAsync();
            _logger.LogInformation("Channel settings loaded via unified configuration service");
        }

        public async Task SaveSettingsAsync()
        {
            // This is now handled by the unified configuration service
            await _configService.SaveConfigurationAsync();
            _logger.LogInformation("Channel settings saved via unified configuration service");
        }

        public bool GetLoggingEnabled(string channelName)
        {
            return _configService.GetLoggingEnabled(channelName);
        }

        public async Task SetLoggingEnabledAsync(string channelName, bool enabled)
        {
            await _configService.SetLoggingEnabledAsync(channelName, enabled);
        }

        public void EnsureChannelExists(string channelName)
        {
            _configService.EnsureChannelExists(channelName);
        }

        public Dictionary<string, ChannelSettings> GetAllSettings()
        {
            var channelConfigs = _configService.GetAllChannelSettings();
            var result = new Dictionary<string, ChannelSettings>();
            
            foreach (var kvp in channelConfigs)
            {
                result[kvp.Key] = new ChannelSettings
                {
                    LoggingEnabled = kvp.Value.LoggingEnabled,
                    LastModified = _configService.GetChannelLastModified(kvp.Key)
                };
            }
            
            return result;
        }
    }
}
