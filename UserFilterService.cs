using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{    public class UserFilterService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };
        
        private readonly ILogger<UserFilterService> _logger;
        private readonly string _filtersFilePath;
        private readonly HashSet<string> _blacklistedUsers;
        private readonly object _lock = new();

        public UserFilterService(ILogger<UserFilterService> logger)
        {
            _logger = logger;
            _filtersFilePath = Path.Combine(AppContext.BaseDirectory, "user_filters.json");
            _blacklistedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            LoadFilters();
        }

        public event EventHandler<string> UserAdded;
        public event EventHandler<string> UserRemoved;

        public bool IsUserBlacklisted(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            lock (_lock)
            {
                return _blacklistedUsers.Contains(username.Trim());
            }
        }

        public async Task<bool> AddUserAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            username = username.Trim().ToLowerInvariant();

            lock (_lock)
            {
                if (_blacklistedUsers.Contains(username))
                    return false;

                _blacklistedUsers.Add(username);
            }

            await SaveFiltersAsync();
            UserAdded?.Invoke(this, username);
            _logger.LogInformation("Added user '{Username}' to blacklist", username);
            return true;
        }

        public async Task<bool> RemoveUserAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            username = username.Trim().ToLowerInvariant();

            lock (_lock)
            {
                if (!_blacklistedUsers.Remove(username))
                    return false;
            }

            await SaveFiltersAsync();
            UserRemoved?.Invoke(this, username);
            _logger.LogInformation("Removed user '{Username}' from blacklist", username);
            return true;
        }        public List<string> GetBlacklistedUsers()
        {
            lock (_lock)
            {
                return [.. _blacklistedUsers.OrderBy(u => u)];
            }
        }

        public async Task ClearAllAsync()
        {
            lock (_lock)
            {
                _blacklistedUsers.Clear();
            }

            await SaveFiltersAsync();
            _logger.LogInformation("Cleared all blacklisted users");
        }

        private void LoadFilters()
        {
            try
            {
                if (!File.Exists(_filtersFilePath))
                {
                    _logger.LogInformation("No user filters file found, starting with empty blacklist");
                    return;
                }

                var json = File.ReadAllText(_filtersFilePath);
                var data = JsonSerializer.Deserialize<UserFiltersData>(json);

                if (data?.BlacklistedUsers != null)
                {
                    lock (_lock)
                    {
                        _blacklistedUsers.Clear();
                        foreach (var user in data.BlacklistedUsers)
                        {
                            if (!string.IsNullOrWhiteSpace(user))
                            {
                                _blacklistedUsers.Add(user.Trim().ToLowerInvariant());
                            }
                        }
                    }

                    _logger.LogInformation("Loaded {Count} blacklisted users from file", _blacklistedUsers.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user filters from file");
            }
        }

        private async Task SaveFiltersAsync()
        {
            try
            {                var data = new UserFiltersData
                {
                    BlacklistedUsers = GetBlacklistedUsers()
                };

                var json = JsonSerializer.Serialize(data, SerializerOptions);

                await File.WriteAllTextAsync(_filtersFilePath, json);
                _logger.LogDebug("Saved user filters to file");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user filters to file");
            }
        }        private class UserFiltersData
        {
            public List<string> BlacklistedUsers { get; set; } = [];
        }
    }
}
