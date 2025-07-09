using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultiChatViewer
{
    public class BlacklistData
    {
        public List<string> BlacklistedUsers { get; set; } = [];
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public string Version { get; set; } = "1.0";
    }

    public class BlacklistManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ILogger<BlacklistManager> _logger;
        private readonly string _blacklistFilePath;
        private readonly object _lock = new();
        private BlacklistData _blacklistData = new();

        public BlacklistManager(ILogger<BlacklistManager> logger)
        {
            _logger = logger;
            _blacklistFilePath = Path.Combine(AppContext.BaseDirectory, "blacklist_users.json");
            _logger.LogInformation("BlacklistManager initialized with path: {Path}", _blacklistFilePath);
            
            // Validate the directory exists and is writable
            try
            {
                var directory = Path.GetDirectoryName(_blacklistFilePath);
                if (!Directory.Exists(directory))
                {
                    _logger.LogWarning("Blacklist directory does not exist, creating: {Directory}", directory);
                    Directory.CreateDirectory(directory);
                }
                
                // Test write access
                var testFile = Path.Combine(directory, "blacklist_write_test.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                _logger.LogDebug("Blacklist directory write access verified");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify write access to blacklist directory: {Directory}", 
                    Path.GetDirectoryName(_blacklistFilePath));
            }
        }

        public event EventHandler<string> UserAdded;
        public event EventHandler<string> UserRemoved;

        public async Task LoadBlacklistAsync()
        {
            try
            {
                if (!File.Exists(_blacklistFilePath))
                {
                    _logger.LogInformation("No blacklist file found, creating new one");
                    await SaveBlacklistAsync();
                    return;
                }

                var json = await File.ReadAllTextAsync(_blacklistFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogInformation("Blacklist file is empty, using defaults");
                    return;
                }

                lock (_lock)
                {
                    _blacklistData = JsonSerializer.Deserialize<BlacklistData>(json) ?? new BlacklistData();
                    
                    // Ensure blacklisted users list is properly initialized
                    _blacklistData.BlacklistedUsers ??= [];
                }

                _logger.LogInformation("Loaded blacklist: {Count} blacklisted users", _blacklistData.BlacklistedUsers.Count);
                
                if (_blacklistData.BlacklistedUsers.Count > 0)
                {
                    _logger.LogInformation("Blacklisted users: [{Users}]", string.Join(", ", _blacklistData.BlacklistedUsers));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading blacklist from {Path}, using defaults", _blacklistFilePath);
                _blacklistData = new BlacklistData();
            }
        }

        public async Task SaveBlacklistAsync()
        {
            try
            {
                BlacklistData dataToSave;
                lock (_lock)
                {
                    _blacklistData.LastUpdated = DateTime.Now;
                    // Create a deep copy to ensure we're not saving a reference that could change
                    dataToSave = new BlacklistData
                    {
                        BlacklistedUsers = new List<string>(_blacklistData.BlacklistedUsers),
                        LastUpdated = _blacklistData.LastUpdated,
                        Version = _blacklistData.Version
                    };
                }

                var json = JsonSerializer.Serialize(dataToSave, JsonOptions);
                await File.WriteAllTextAsync(_blacklistFilePath, json);

                _logger.LogInformation("Successfully saved blacklist to {Path}: {Count} blacklisted users",
                    _blacklistFilePath, dataToSave.BlacklistedUsers.Count);
                
                if (dataToSave.BlacklistedUsers.Count > 0)
                {
                    _logger.LogInformation("Saved blacklisted users: [{Users}]", string.Join(", ", dataToSave.BlacklistedUsers));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied when saving blacklist to {Path}. Check file permissions.", _blacklistFilePath);
                throw new InvalidOperationException($"Cannot save blacklist - access denied to file: {_blacklistFilePath}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "Directory not found when saving blacklist to {Path}", _blacklistFilePath);
                throw new InvalidOperationException($"Cannot save blacklist - directory not found: {Path.GetDirectoryName(_blacklistFilePath)}", ex);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error when saving blacklist to {Path}", _blacklistFilePath);
                throw new InvalidOperationException($"Cannot save blacklist - IO error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error saving blacklist to {Path}: {ErrorType} - {ErrorMessage}", 
                    _blacklistFilePath, ex.GetType().Name, ex.Message);
                throw;
            }
        }

        public bool IsUserBlacklisted(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            lock (_lock)
            {
                return _blacklistData.BlacklistedUsers.Contains(username.Trim(), StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task<bool> AddUserAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogWarning("Attempted to add null or empty username to blacklist");
                return false;
            }

            username = username.Trim().ToLowerInvariant();

            // Remove @ symbol if present
            if (username.StartsWith('@'))
            {
                username = username[1..];
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogWarning("Username became empty after processing");
                return false;
            }

            _logger.LogInformation("Attempting to add user '{Username}' to blacklist", username);

            bool wasAdded;
            lock (_lock)
            {
                if (_blacklistData.BlacklistedUsers.Contains(username, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("User '{Username}' is already blacklisted", username);
                    return false;
                }

                _blacklistData.BlacklistedUsers.Add(username);
                wasAdded = true;
                _logger.LogDebug("User '{Username}' added to in-memory blacklist. Total count: {Count}", username, _blacklistData.BlacklistedUsers.Count);
            }

            if (wasAdded)
            {
                try
                {
                    _logger.LogInformation("Saving blacklist immediately after adding user '{Username}'", username);
                    await SaveBlacklistAsync();
                    
                    UserAdded?.Invoke(this, username);
                    _logger.LogInformation("Successfully added user '{Username}' to blacklist and saved", username);
                    return true;
                }
                catch (Exception ex)
                {
                    // Revert the change if saving failed
                    lock (_lock)
                    {
                        _blacklistData.BlacklistedUsers.Remove(username);
                    }
                    _logger.LogError(ex, "Failed to save blacklist after adding user '{Username}'", username);
                    throw;
                }
            }

            return false;
        }

        public async Task<bool> RemoveUserAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogWarning("Attempted to remove null or empty username from blacklist");
                return false;
            }

            username = username.Trim().ToLowerInvariant();

            // Remove @ symbol if present
            if (username.StartsWith('@'))
            {
                username = username[1..];
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogWarning("Username became empty after processing");
                return false;
            }

            bool removed;
            var removedUsers = new List<string>();
            
            lock (_lock)
            {
                // Find all matching users (case-insensitive) and store them for restoration if save fails
                removedUsers = _blacklistData.BlacklistedUsers
                    .Where(u => string.Equals(u, username, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                removed = _blacklistData.BlacklistedUsers.RemoveAll(u => 
                    string.Equals(u, username, StringComparison.OrdinalIgnoreCase)) > 0;
            }

            if (removed)
            {
                try
                {
                    _logger.LogInformation("Saving blacklist immediately after removing user '{Username}'", username);
                    await SaveBlacklistAsync();
                    
                    UserRemoved?.Invoke(this, username);
                    _logger.LogInformation("Successfully removed user '{Username}' from blacklist and saved", username);
                    return true;
                }
                catch (Exception ex)
                {
                    // Revert the change if saving failed
                    lock (_lock)
                    {
                        _blacklistData.BlacklistedUsers.AddRange(removedUsers);
                    }
                    _logger.LogError(ex, "Failed to save blacklist after removing user '{Username}'", username);
                    throw;
                }
            }
            else
            {
                _logger.LogDebug("User '{Username}' not found in blacklist", username);
            }

            return false;
        }

        public List<string> GetBlacklistedUsers()
        {
            lock (_lock)
            {
                return [.. _blacklistData.BlacklistedUsers.OrderBy(u => u)];
            }
        }

        public async Task ClearAllUsersAsync()
        {
            List<string> previousUsers;
            lock (_lock)
            {
                previousUsers = new List<string>(_blacklistData.BlacklistedUsers);
                _blacklistData.BlacklistedUsers.Clear();
            }

            try
            {
                await SaveBlacklistAsync();
                _logger.LogInformation("Cleared all blacklisted users ({Count} users removed)", previousUsers.Count);
            }
            catch (Exception ex)
            {
                // Revert the change if saving failed
                lock (_lock)
                {
                    _blacklistData.BlacklistedUsers.AddRange(previousUsers);
                }
                _logger.LogError(ex, "Failed to save blacklist after clearing all users");
                throw;
            }
        }

        public void DebugLogCurrentState()
        {
            lock (_lock)
            {
                _logger.LogInformation("=== Blacklist State Debug ===");
                _logger.LogInformation("Blacklist file path: {Path}", _blacklistFilePath);
                _logger.LogInformation("Blacklisted users: {Count}", _blacklistData.BlacklistedUsers.Count);
                
                if (_blacklistData.BlacklistedUsers.Count > 0)
                {
                    _logger.LogInformation("Blacklisted users list: [{Users}]", string.Join(", ", _blacklistData.BlacklistedUsers));
                }
                else
                {
                    _logger.LogInformation("No blacklisted users in memory");
                }
                
                _logger.LogInformation("Last updated: {LastUpdated}", _blacklistData.LastUpdated);
                _logger.LogInformation("=== End Blacklist State ===");
            }
        }
    }
}
