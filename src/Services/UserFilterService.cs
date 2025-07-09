using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultiChatViewer
{
    public class UserFilterService
    {
        private readonly ILogger<UserFilterService> _logger;
        private readonly BlacklistManager _blacklistManager;
        private bool _isInitialized = false;

        public UserFilterService(ILogger<UserFilterService> logger, BlacklistManager blacklistManager)
        {
            _logger = logger;
            _blacklistManager = blacklistManager;
            
            // Subscribe to events from the blacklist manager
            _blacklistManager.UserAdded += OnUserAdded;
            _blacklistManager.UserRemoved += OnUserRemoved;
            
            _logger.LogInformation("UserFilterService initialized with BlacklistManager");
        }

        /// <summary>
        /// Ensures the configuration is loaded for user filtering
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                _logger.LogDebug("UserFilterService already initialized, skipping");
                return;
            }

            try
            {
                _logger.LogInformation("Initializing UserFilterService...");
                
                // Ensure the blacklist manager is properly initialized
                if (_blacklistManager == null)
                {
                    _logger.LogError("Blacklist manager is null during UserFilterService initialization");
                    throw new InvalidOperationException("Blacklist manager is not available");
                }
                
                await _blacklistManager.LoadBlacklistAsync();
                var blacklistedUsers = _blacklistManager.GetBlacklistedUsers();
                _logger.LogInformation("UserFilterService initialized with {Count} blacklisted users loaded from blacklist manager", blacklistedUsers.Count);
                
                // Always log all loaded users for debugging
                if (blacklistedUsers.Count > 0)
                {
                    _logger.LogInformation("Loaded blacklisted users: {Users}", string.Join(", ", blacklistedUsers));
                }
                else
                {
                    _logger.LogInformation("No blacklisted users found in blacklist manager");
                }
                
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing UserFilterService: {ErrorType} - {ErrorMessage}", 
                    ex.GetType().Name, ex.Message);
                throw;
            }
        }

        public event EventHandler<string> UserAdded;
        public event EventHandler<string> UserRemoved;

        private void OnUserAdded(object sender, string username)
        {
            UserAdded?.Invoke(this, username);
        }

        private void OnUserRemoved(object sender, string username)
        {
            UserRemoved?.Invoke(this, username);
        }

        public bool IsUserBlacklisted(string username)
        {
            if (!_isInitialized)
            {
                _logger.LogDebug("UserFilterService not initialized when checking if user is blacklisted, returning false");
                return false;
            }

            return _blacklistManager.IsUserBlacklisted(username);
        }

        public async Task<bool> AddUserAsync(string username)
        {
            try
            {
                if (!_isInitialized)
                {
                    _logger.LogWarning("UserFilterService not initialized, initializing now before adding user");
                    await InitializeAsync();
                }

                if (string.IsNullOrWhiteSpace(username))
                {
                    _logger.LogWarning("Cannot add null or empty username to blacklist");
                    return false;
                }

                _logger.LogInformation("UserFilterService: Adding user '{Username}' to blacklist", username);
                var result = await _blacklistManager.AddUserAsync(username);
                
                if (result)
                {
                    _logger.LogInformation("Successfully added user '{Username}' to blacklist via UserFilterService", username);
                }
                else
                {
                    _logger.LogDebug("User '{Username}' was not added to blacklist (may already exist)", username);
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in UserFilterService.AddUserAsync for user '{Username}': {ErrorType} - {ErrorMessage}", 
                    username, ex.GetType().Name, ex.Message);
                throw; // Re-throw to let the UI handle it
            }
        }

        public async Task<bool> RemoveUserAsync(string username)
        {
            if (!_isInitialized)
            {
                _logger.LogWarning("UserFilterService not initialized, initializing now before removing user");
                await InitializeAsync();
            }

            _logger.LogInformation("UserFilterService: Removing user '{Username}' from blacklist", username);
            var result = await _blacklistManager.RemoveUserAsync(username);
            
            if (result)
            {
                _logger.LogInformation("Successfully removed user '{Username}' from blacklist via UserFilterService", username);
            }
            else
            {
                _logger.LogDebug("User '{Username}' was not removed from blacklist (may not exist)", username);
            }
            return result;
        }

        public List<string> GetBlacklistedUsers()
        {
            if (!_isInitialized)
            {
                _logger.LogWarning("UserFilterService not initialized when getting blacklisted users, returning empty list");
                return [];
            }

            return _blacklistManager.GetBlacklistedUsers();
        }

        public async Task ClearAllAsync()
        {
            if (!_isInitialized)
            {
                _logger.LogWarning("UserFilterService not initialized, initializing now before clearing users");
                await InitializeAsync();
            }

            await _blacklistManager.ClearAllUsersAsync();
            _logger.LogInformation("Cleared all blacklisted users via UserFilterService");
        }
    }
}
