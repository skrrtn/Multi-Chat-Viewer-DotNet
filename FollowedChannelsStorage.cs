using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public class FollowedChannelsStorage
    {
        private readonly ILogger<FollowedChannelsStorage> _logger;
        private readonly string _storageFilePath;

        public FollowedChannelsStorage(ILogger<FollowedChannelsStorage> logger)
        {
            _logger = logger;
            
            // Store in the same directory as the executable
            var appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            _storageFilePath = Path.Combine(appDirectory, "followed_channels.json");
            
            _logger.LogInformation("FollowedChannelsStorage initialized with path: {Path}", _storageFilePath);
        }

        public async Task<List<string>> LoadChannelsAsync()
        {
            try
            {
                if (!File.Exists(_storageFilePath))
                {
                    _logger.LogInformation("No followed channels file found, returning empty list");
                    return new List<string>();
                }

                var json = await File.ReadAllTextAsync(_storageFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogInformation("Followed channels file is empty, returning empty list");
                    return new List<string>();
                }

                var channels = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                _logger.LogInformation("Loaded {Count} followed channels from storage", channels.Count);
                return channels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading followed channels from {Path}", _storageFilePath);
                return new List<string>();
            }
        }

        public async Task SaveChannelsAsync(List<string> channels)
        {
            try
            {
                var json = JsonSerializer.Serialize(channels, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(_storageFilePath, json);
                _logger.LogInformation("Saved {Count} followed channels to storage", channels.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving followed channels to {Path}", _storageFilePath);
                throw;
            }
        }

        public async Task AddChannelAsync(string channelName)
        {
            var channels = await LoadChannelsAsync();
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            if (!channels.Contains(normalizedChannel))
            {
                channels.Add(normalizedChannel);
                await SaveChannelsAsync(channels);
                _logger.LogInformation("Added channel {Channel} to storage", normalizedChannel);
            }
            else
            {
                _logger.LogWarning("Channel {Channel} already exists in storage", normalizedChannel);
            }
        }

        public async Task RemoveChannelAsync(string channelName)
        {
            var channels = await LoadChannelsAsync();
            var normalizedChannel = channelName.ToLower().Replace("#", "");
            
            if (channels.Remove(normalizedChannel))
            {
                await SaveChannelsAsync(channels);
                _logger.LogInformation("Removed channel {Channel} from storage", normalizedChannel);
            }
            else
            {
                _logger.LogWarning("Channel {Channel} not found in storage", normalizedChannel);
            }
        }
    }
}
