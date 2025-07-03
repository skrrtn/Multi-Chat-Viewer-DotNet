using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public class KickCredentials
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
    }

    public class KickCredentialsService
    {
        private readonly ILogger<KickCredentialsService> _logger;
        private readonly UnifiedConfigurationService _configService;
        private KickCredentials _cachedCredentials;

        public KickCredentialsService(ILogger<KickCredentialsService> logger, UnifiedConfigurationService configService)
        {
            _logger = logger;
            _configService = configService;
        }

        public async Task<KickCredentials> GetCredentialsAsync()
        {
            if (_cachedCredentials != null)
                return _cachedCredentials;

            try
            {
                var encryptedClientId = await _configService.GetKickClientIdAsync();
                var encryptedClientSecret = await _configService.GetKickClientSecretAsync();

                if (string.IsNullOrEmpty(encryptedClientId) || string.IsNullOrEmpty(encryptedClientSecret))
                {
                    _logger.LogInformation("No Kick credentials found in configuration");
                    return null;
                }

                var clientId = DecryptString(encryptedClientId);
                var clientSecret = DecryptString(encryptedClientSecret);

                _cachedCredentials = new KickCredentials
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                };

                _logger.LogInformation("Successfully loaded Kick credentials");
                return _cachedCredentials;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Kick credentials");
                return null;
            }
        }

        public async Task<bool> SaveCredentialsAsync(string clientId, string clientSecret)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    _logger.LogWarning("Attempted to save invalid Kick credentials");
                    return false;
                }

                var encryptedClientId = EncryptString(clientId);
                var encryptedClientSecret = EncryptString(clientSecret);

                await _configService.SetKickClientIdAsync(encryptedClientId);
                await _configService.SetKickClientSecretAsync(encryptedClientSecret);

                _cachedCredentials = new KickCredentials
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                };

                _logger.LogInformation("Successfully saved Kick credentials");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Kick credentials");
                return false;
            }
        }

        public async Task<bool> HasCredentialsAsync()
        {
            var credentials = await GetCredentialsAsync();
            return credentials != null && 
                   !string.IsNullOrEmpty(credentials.ClientId) && 
                   !string.IsNullOrEmpty(credentials.ClientSecret);
        }

        public async Task ClearCredentialsAsync()
        {
            try
            {
                await _configService.SetKickClientIdAsync(string.Empty);
                await _configService.SetKickClientSecretAsync(string.Empty);
                _cachedCredentials = null;
                _logger.LogInformation("Cleared Kick credentials");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear Kick credentials");
            }
        }

        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception)
            {
                // If encryption fails, return empty string for security
                return string.Empty;
            }
        }

        private static string DecryptString(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception)
            {
                // If decryption fails, return empty string
                return string.Empty;
            }
        }
    }
}
