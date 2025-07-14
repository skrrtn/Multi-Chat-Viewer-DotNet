using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Linq;
using MultiChatViewer.Services;

namespace MultiChatViewer
{
    public class UserMessageLookupService(ILogger<UserMessageLookupService> logger, EmoteService emoteService)
    {
        private readonly ILogger<UserMessageLookupService> _logger = logger;
        private readonly EmoteService _emoteService = emoteService;

        /// <summary>
        /// Gets all channels where the specified user has posted messages
        /// </summary>
        public async Task<List<UserChannelInfo>> GetChannelsForUserAsync(string username)
        {
            var channels = new List<UserChannelInfo>();

            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                if (!Directory.Exists(dbDirectory))
                {
                    _logger.LogWarning("Database directory not found: {Directory}", dbDirectory);
                    return channels;
                }

                var dbFiles = Directory.GetFiles(dbDirectory, "*.db");
                _logger.LogDebug("Scanning {Count} database files for user: {Username}", dbFiles.Length, username);

                foreach (var dbFile in dbFiles)
                {
                    try
                    {
                        var channelName = Path.GetFileNameWithoutExtension(dbFile);
                        var messageCount = await GetUserMessageCountInChannelAsync(username, channelName, dbFile);
                        
                        if (messageCount > 0)
                        {
                            var lastMessageTime = await GetUserLastMessageTimeInChannelAsync(username, channelName, dbFile);
                            
                            channels.Add(new UserChannelInfo
                            {
                                ChannelName = channelName,
                                MessageCount = messageCount,
                                LastMessageTime = lastMessageTime
                            });
                            
                            _logger.LogDebug("Found {MessageCount} messages for user {Username} in channel {Channel}", 
                                messageCount, username, channelName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error scanning database file: {File}", dbFile);
                        // Continue with other files
                    }
                }

                _logger.LogInformation("Found user {Username} in {ChannelCount} channels with total of {TotalMessages} messages", 
                    username, channels.Count, channels.Sum(c => c.MessageCount));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channels for user: {Username}", username);
                throw;
            }

            return channels;
        }

        /// <summary>
        /// Gets all messages from a specific user in a specific channel
        /// </summary>
        public async Task<List<ChatMessageWithChannel>> GetUserMessagesFromChannelAsync(string username, string channelName)
        {
            var messages = new List<ChatMessageWithChannel>();

            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                
                // First try to find the database file with the new naming convention (channelname_platform.db)
                string dbPath = null;
                Platform detectedPlatform = Platform.Twitch; // Default fallback
                
                var dbFiles = Directory.GetFiles(dbDirectory, $"{channelName.ToLower()}_*.db");
                if (dbFiles.Length > 0)
                {
                    // Use the first matching file with platform naming convention
                    dbPath = dbFiles[0];
                    var fileName = Path.GetFileNameWithoutExtension(dbPath);
                    var parts = fileName.Split('_');
                    if (parts.Length >= 2)
                    {
                        var platformString = parts[parts.Length - 1];
                        if (Enum.TryParse<Platform>(platformString, true, out var platform))
                        {
                            detectedPlatform = platform;
                        }
                    }
                }
                else
                {
                    // Fall back to legacy naming convention
                    dbPath = Path.Combine(dbDirectory, $"{channelName.ToLower()}.db");
                    if (File.Exists(dbPath))
                    {
                        // Try to get platform from database metadata
                        detectedPlatform = await GetPlatformFromDatabaseAsync(dbPath);
                    }
                }

                if (dbPath == null || !File.Exists(dbPath))
                {
                    _logger.LogWarning("Database file not found for channel: {Channel}", channelName);
                    return messages;
                }                // Use a separate connection for reading to avoid conflicts with the main database service
                // Enable WAL mode support and set timeout for concurrent operations
                var connectionString = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                // Set busy timeout for concurrent operations
                using var timeoutCommand = new SqliteCommand("PRAGMA busy_timeout=5000;", connection);
                await timeoutCommand.ExecuteNonQueryAsync();                var selectSql = @"
                    SELECT username, message, timestamp, is_system_message
                    FROM chat_messages 
                    WHERE LOWER(username) = LOWER(@username)
                      AND is_system_message = 0
                    ORDER BY timestamp DESC";

                using var command = new SqliteCommand(selectSql, connection);
                command.Parameters.AddWithValue("@username", username);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var message = new ChatMessageWithChannel
                    {
                        Username = reader.GetString(0),
                        Message = reader.GetString(1),
                        Timestamp = reader.GetDateTime(2),
                        IsSystemMessage = reader.GetBoolean(3),
                        ChannelName = channelName,
                        SourceChannel = channelName,  // Set source channel for proper context
                        SourcePlatform = detectedPlatform  // Set the detected platform
                    };
                    
                        // Parse the message for @mentions and emotes
                    MessageParser.ParseChatMessage(message, _emoteService);
                    
                    messages.Add(message);
                }

                _logger.LogDebug("Retrieved {Count} messages for user {Username} from channel {Channel}", 
                    messages.Count, username, channelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for user {Username} from channel {Channel}", username, channelName);
                throw;
            }

            return messages;
        }        /// <summary>
        /// Gets the count of messages from a user in a specific channel database file
        /// </summary>
        private async Task<int> GetUserMessageCountInChannelAsync(string username, string channelName, string dbPath)
        {
            try
            {
                var connectionString = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                
                // Set busy timeout for concurrent operations
                using var timeoutCommand = new SqliteCommand("PRAGMA busy_timeout=5000;", connection);
                await timeoutCommand.ExecuteNonQueryAsync();                var countSql = @"
                    SELECT COUNT(*) 
                    FROM chat_messages 
                    WHERE LOWER(username) = LOWER(@username)
                      AND is_system_message = 0";

                using var command = new SqliteCommand(countSql, connection);
                command.Parameters.AddWithValue("@username", username);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting message count for user {Username} in channel {Channel}", username, channelName);
                return 0;
            }
        }        /// <summary>
        /// Gets the timestamp of the last message from a user in a specific channel database file
        /// </summary>
        private async Task<DateTime> GetUserLastMessageTimeInChannelAsync(string username, string channelName, string dbPath)
        {
            try
            {
                var connectionString = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                
                // Set busy timeout for concurrent operations
                using var timeoutCommand = new SqliteCommand("PRAGMA busy_timeout=5000;", connection);
                await timeoutCommand.ExecuteNonQueryAsync();                var lastMessageSql = @"
                    SELECT timestamp 
                    FROM chat_messages 
                    WHERE LOWER(username) = LOWER(@username)
                      AND is_system_message = 0
                    ORDER BY timestamp DESC 
                    LIMIT 1";

                using var command = new SqliteCommand(lastMessageSql, connection);
                command.Parameters.AddWithValue("@username", username);

                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToDateTime(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting last message time for user {Username} in channel {Channel}", username, channelName);
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Searches for messages containing specific text from a user across all channels
        /// </summary>
        public async Task<List<ChatMessageWithChannel>> SearchUserMessagesAsync(string username, string searchText)
        {
            var messages = new List<ChatMessageWithChannel>();

            try
            {
                var channels = await GetChannelsForUserAsync(username);
                
                foreach (var channel in channels)
                {
                    var channelMessages = await SearchUserMessagesInChannelAsync(username, channel.ChannelName, searchText);
                    messages.AddRange(channelMessages);                }

                // Sort by timestamp across all channels
                messages = [.. messages.OrderBy(m => m.Timestamp)];
                
                _logger.LogInformation("Found {Count} messages containing '{SearchText}' for user {Username} across {ChannelCount} channels",
                    messages.Count, searchText, username, channels.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages for user {Username} with text '{SearchText}'", username, searchText);
                throw;
            }

            return messages;
        }

        /// <summary>
        /// Searches for messages containing specific text from a user in a specific channel
        /// </summary>
        private async Task<List<ChatMessageWithChannel>> SearchUserMessagesInChannelAsync(string username, string channelName, string searchText)
        {
            var messages = new List<ChatMessageWithChannel>();

            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                
                // First try to find the database file with the new naming convention (channelname_platform.db)
                string dbPath = null;
                Platform detectedPlatform = Platform.Twitch; // Default fallback
                
                var dbFiles = Directory.GetFiles(dbDirectory, $"{channelName.ToLower()}_*.db");
                if (dbFiles.Length > 0)
                {
                    // Use the first matching file with platform naming convention
                    dbPath = dbFiles[0];
                    var fileName = Path.GetFileNameWithoutExtension(dbPath);
                    var parts = fileName.Split('_');
                    if (parts.Length >= 2)
                    {
                        var platformString = parts[parts.Length - 1];
                        if (Enum.TryParse<Platform>(platformString, true, out var platform))
                        {
                            detectedPlatform = platform;
                        }
                    }
                }
                else
                {
                    // Fall back to legacy naming convention
                    dbPath = Path.Combine(dbDirectory, $"{channelName.ToLower()}.db");
                    if (File.Exists(dbPath))
                    {
                        // Try to get platform from database metadata
                        detectedPlatform = await GetPlatformFromDatabaseAsync(dbPath);
                    }
                }

                if (dbPath == null || !File.Exists(dbPath))
                {
                    return messages;
                }                var connectionString = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                
                // Set busy timeout for concurrent operations
                using var timeoutCommand = new SqliteCommand("PRAGMA busy_timeout=5000;", connection);
                await timeoutCommand.ExecuteNonQueryAsync();                var searchSql = @"
                    SELECT username, message, timestamp, is_system_message
                    FROM chat_messages 
                    WHERE LOWER(username) = LOWER(@username)
                      AND LOWER(message) LIKE LOWER(@searchText)
                      AND is_system_message = 0
                    ORDER BY timestamp DESC";

                using var command = new SqliteCommand(searchSql, connection);
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@searchText", $"%{searchText}%");

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var message = new ChatMessageWithChannel
                    {
                        Username = reader.GetString(0),
                        Message = reader.GetString(1),
                        Timestamp = reader.GetDateTime(2),
                        IsSystemMessage = reader.GetBoolean(3),
                        ChannelName = channelName,
                        SourceChannel = channelName,  // Set source channel for proper context
                        SourcePlatform = detectedPlatform  // Set the detected platform
                    };
                    
                    // Parse the message for @mentions and emotes
                    MessageParser.ParseChatMessage(message, _emoteService);
                    
                    messages.Add(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching messages for user {Username} in channel {Channel} with text '{SearchText}'", 
                    username, channelName, searchText);
            }

            return messages;
        }

        /// <summary>
        /// Gets platform information from a database file by reading metadata
        /// </summary>
        private async Task<Platform> GetPlatformFromDatabaseAsync(string dbPath)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                await connection.OpenAsync();

                // Check if metadata table exists
                var checkTableSql = @"
                    SELECT COUNT(*)
                    FROM sqlite_master 
                    WHERE type='table' AND name='channel_metadata'";

                using var checkCommand = new SqliteCommand(checkTableSql, connection);
                var tableExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;

                if (!tableExists)
                {
                    _logger.LogDebug("Metadata table not found in database, defaulting to Twitch");
                    return Platform.Twitch;
                }

                // Get platform metadata
                var selectSql = @"
                    SELECT value FROM channel_metadata WHERE key = 'platform' LIMIT 1";

                using var command = new SqliteCommand(selectSql, connection);
                var result = await command.ExecuteScalarAsync();

                if (result != null && Enum.TryParse<Platform>(result.ToString(), out var platform))
                {
                    return platform;
                }
                else
                {
                    _logger.LogDebug("No platform metadata found in database, defaulting to Twitch");
                    return Platform.Twitch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading platform from database, defaulting to Twitch");
                return Platform.Twitch;
            }
        }
    }
}
