using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultiChatViewer
{
    public class FollowedChannelsStorage
    {
        private readonly ILogger<FollowedChannelsStorage> _logger;
        private readonly UnifiedConfigurationService _configService;

        public FollowedChannelsStorage(ILogger<FollowedChannelsStorage> logger, UnifiedConfigurationService configService)
        {
            _logger = logger;
            _configService = configService;
            _logger.LogInformation("FollowedChannelsStorage initialized with unified configuration service");
        }

        public async Task<List<string>> LoadChannelsAsync()
        {
            try
            {
                // Ensure configuration is loaded
                await _configService.LoadConfigurationAsync();
                var channels = _configService.GetFollowedChannels();
                _logger.LogInformation("Loaded {Count} followed channels from unified configuration", channels.Count);
                return channels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading followed channels");
                return [];
            }
        }

        public async Task SaveChannelsAsync(List<string> channels)
        {
            try
            {
                // Clear existing followed channels and add new ones
                var currentChannels = _configService.GetFollowedChannels();
                
                // Remove all current channels first
                var removeTask = Task.WhenAll(currentChannels.Select(c => _configService.RemoveFollowedChannelAsync(c)));
                await removeTask;
                
                // Add all new channels
                var addTasks = channels.Select(c => _configService.AddFollowedChannelAsync(c));
                await Task.WhenAll(addTasks);
                
                _logger.LogInformation("Saved {Count} followed channels to unified configuration", channels.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving followed channels");
                throw;
            }
        }

        public async Task AddChannelAsync(string channelName)
        {
            await _configService.AddFollowedChannelAsync(channelName);
        }

        public async Task RemoveChannelAsync(string channelName)
        {
            await _configService.RemoveFollowedChannelAsync(channelName);
        }
    }
}
