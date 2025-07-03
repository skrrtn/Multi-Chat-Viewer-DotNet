using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public class UserFilterService
    {
        private readonly ILogger<UserFilterService> _logger;
        private readonly UnifiedConfigurationService _configService;

        public UserFilterService(ILogger<UserFilterService> logger, UnifiedConfigurationService configService)
        {
            _logger = logger;
            _configService = configService;
            
            // Subscribe to events from the unified configuration service
            _configService.UserAdded += OnUserAdded;
            _configService.UserRemoved += OnUserRemoved;
            
            _logger.LogInformation("UserFilterService initialized with unified configuration service");
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
            return _configService.IsUserBlacklisted(username);
        }

        public async Task<bool> AddUserAsync(string username)
        {
            return await _configService.AddBlacklistedUserAsync(username);
        }

        public async Task<bool> RemoveUserAsync(string username)
        {
            return await _configService.RemoveBlacklistedUserAsync(username);
        }

        public List<string> GetBlacklistedUsers()
        {
            return _configService.GetBlacklistedUsers();
        }

        public async Task ClearAllAsync()
        {
            await _configService.ClearAllBlacklistedUsersAsync();
        }
    }
}
