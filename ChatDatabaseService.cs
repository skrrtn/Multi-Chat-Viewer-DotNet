using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{    public class ChatDatabaseService(ILogger<ChatDatabaseService> logger)
    {
        private readonly ILogger<ChatDatabaseService> _logger = logger;
        private string _currentDatabasePath;
        private SqliteConnection _connection;

        public async Task InitializeDatabaseAsync(string channelName, Platform platform)
        {
            try
            {
                _logger.LogInformation("🗄️ Starting database initialization for channel: {Channel} on {Platform}", channelName, platform);
                
                // Close existing connection if any
                await CloseConnectionAsync();

                // Create db directory if it doesn't exist
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                _logger.LogDebug("Checking database directory: {Directory}", dbDirectory);
                
                if (!Directory.Exists(dbDirectory))
                {
                    _logger.LogInformation("Creating database directory: {Directory}", dbDirectory);
                    Directory.CreateDirectory(dbDirectory);
                    _logger.LogInformation("✓ Created database directory: {Directory}", dbDirectory);
                }
                else
                {
                    _logger.LogDebug("✓ Database directory already exists: {Directory}", dbDirectory);
                }                // Create database file path with platform suffix
                _currentDatabasePath = Path.Combine(dbDirectory, $"{channelName.ToLower()}_{platform.ToString().ToLower()}.db");
                _logger.LogInformation("Database path for channel {Channel} on {Platform}: {Path}", channelName, platform, _currentDatabasePath);

                // Check if database file already exists
                bool fileExists = File.Exists(_currentDatabasePath);
                _logger.LogDebug("Database file exists: {Exists}", fileExists);

                // Check if we have write permissions
                try
                {
                    var testFile = Path.Combine(dbDirectory, "test_write_permission.tmp");
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                    _logger.LogDebug("✓ Write permissions verified for database directory");
                }
                catch (Exception permEx)
                {
                    _logger.LogError(permEx, "❌ No write permissions for database directory: {Directory}", dbDirectory);
                    throw new UnauthorizedAccessException($"No write permissions for database directory: {dbDirectory}", permEx);
                }

                // Check available disk space
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(dbDirectory));
                    var availableSpace = drive.AvailableFreeSpace;
                    _logger.LogDebug("Available disk space: {Space} bytes ({SpaceMB} MB)", availableSpace, availableSpace / (1024 * 1024));
                    
                    if (availableSpace < 10 * 1024 * 1024) // Less than 10MB
                    {
                        _logger.LogWarning("⚠️ Low disk space: {SpaceMB} MB available", availableSpace / (1024 * 1024));
                    }
                }
                catch (Exception spaceEx)
                {
                    _logger.LogWarning(spaceEx, "Could not check disk space");
                }                // Create connection with ReadWriteCreate mode to allow creating new database files
                var connectionString = $"Data Source={_currentDatabasePath};Mode=ReadWriteCreate";
                _logger.LogDebug("Creating database connection with string: {ConnectionString}", connectionString.Replace(_currentDatabasePath, "[PATH]"));
                
                try
                {
                    _connection = new SqliteConnection(connectionString);
                    await _connection.OpenAsync();
                    _logger.LogInformation("✓ Database connection opened successfully");
                }
                catch (SqliteException sqlEx)
                {
                    _logger.LogError(sqlEx, "❌ SQLite error opening database: Error {ErrorCode} - {Message}", sqlEx.SqliteErrorCode, sqlEx.Message);
                    throw new InvalidOperationException($"SQLite Error {sqlEx.SqliteErrorCode}: {sqlEx.Message}", sqlEx);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "❌ General error opening database: {Message}", dbEx.Message);
                    throw new InvalidOperationException($"Database Error: {dbEx.Message}", dbEx);
                }

                // Enable WAL mode for better concurrent access
                _logger.LogDebug("Setting WAL mode for database");
                using var walCommand = new SqliteCommand("PRAGMA journal_mode=WAL;", _connection);
                var walResult = await walCommand.ExecuteScalarAsync();
                _logger.LogDebug("WAL mode result: {Result}", walResult);
                
                // Set busy timeout for concurrent operations
                _logger.LogDebug("Setting busy timeout for database");
                using var timeoutCommand = new SqliteCommand("PRAGMA busy_timeout=5000;", _connection);
                await timeoutCommand.ExecuteNonQueryAsync();
                _logger.LogDebug("✓ Database timeout configured");

                // Create table if it doesn't exist
                _logger.LogDebug("Creating/verifying chat messages table");
                await CreateTableAsync();
                
                // Create metadata table if it doesn't exist
                _logger.LogDebug("Creating/verifying metadata table");
                await CreateMetadataTableAsync();

                _logger.LogInformation("✅ Database initialized successfully for channel: {Channel}", channelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize database for channel: {Channel}. Error type: {ErrorType}, Message: {Message}", 
                    channelName, ex.GetType().Name, ex.Message);
                
                // Add specific error context
                var errorContext = $"Channel: {channelName}\nDatabase Path: {_currentDatabasePath}\nWorking Directory: {Directory.GetCurrentDirectory()}";
                if (ex is SqliteException sqlEx)
                {
                    errorContext += $"\nSQLite Error Code: {sqlEx.SqliteErrorCode}\nSQLite Extended Error Code: {sqlEx.SqliteExtendedErrorCode}";
                }
                _logger.LogError("Database error context: {Context}", errorContext);
                
                throw;
            }
        }

        private async Task CreateTableAsync()
        {
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS chat_messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL,
                    message TEXT NOT NULL,
                    timestamp DATETIME NOT NULL,
                    is_system_message BOOLEAN NOT NULL DEFAULT 0,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";

            using var command = new SqliteCommand(createTableSql, _connection);
            await command.ExecuteNonQueryAsync();
            
            _logger.LogDebug("Chat messages table created/verified");
        }

        private async Task CreateMetadataTableAsync()
        {
            var createMetadataTableSql = @"
                CREATE TABLE IF NOT EXISTS channel_metadata (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    key TEXT NOT NULL UNIQUE,
                    value TEXT NOT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )";

            using var command = new SqliteCommand(createMetadataTableSql, _connection);
            await command.ExecuteNonQueryAsync();
            
            _logger.LogDebug("Channel metadata table created/verified");
        }

        public async Task LogMessageAsync(ChatMessage chatMessage)
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _logger.LogWarning("Database connection not available, cannot log message");
                return;
            }

            try
            {
                var insertSql = @"
                    INSERT INTO chat_messages (username, message, timestamp, is_system_message)
                    VALUES (@username, @message, @timestamp, @is_system_message)";

                using var command = new SqliteCommand(insertSql, _connection);
                command.Parameters.AddWithValue("@username", chatMessage.Username);
                command.Parameters.AddWithValue("@message", chatMessage.Message);
                command.Parameters.AddWithValue("@timestamp", chatMessage.Timestamp);
                command.Parameters.AddWithValue("@is_system_message", chatMessage.IsSystemMessage);

                await command.ExecuteNonQueryAsync();
                
                _logger.LogDebug("Logged message from {Username}: {Message}", chatMessage.Username, chatMessage.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log message to database: {Username} - {Message}", 
                    chatMessage.Username, chatMessage.Message);
            }
        }

        public async Task<int> GetMessageCountAsync()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                return 0;
            }

            try
            {
                var countSql = "SELECT COUNT(*) FROM chat_messages";
                using var command = new SqliteCommand(countSql, _connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get message count from database");
                return 0;
            }
        }        public Task<long> GetDatabaseSizeAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentDatabasePath) || !File.Exists(_currentDatabasePath))
                {
                    return Task.FromResult(0L);
                }

                var fileInfo = new FileInfo(_currentDatabasePath);
                return Task.FromResult(fileInfo.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get database size");
                return Task.FromResult(0L);
            }
        }

        public static long GetDatabaseSizeByPath(string channelName, Platform platform)
        {
            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                var dbPath = Path.Combine(dbDirectory, $"{channelName.ToLower()}_{platform.ToString().ToLower()}.db");
                
                if (!File.Exists(dbPath))
                {
                    return 0;
                }

                var fileInfo = new FileInfo(dbPath);
                return fileInfo.Length;
            }
            catch
            {
                return 0;
            }
        }

        public static async Task<int> GetMessageCountByPathAsync(string channelName, Platform platform)
        {
            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                var dbPath = Path.Combine(dbDirectory, $"{channelName.ToLower()}_{platform.ToString().ToLower()}.db");
                
                if (!File.Exists(dbPath))
                {
                    return 0;
                }

                var connectionString = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                
                // Set busy timeout for concurrent operations
                using var timeoutCommand = new SqliteCommand("PRAGMA busy_timeout=5000;", connection);
                await timeoutCommand.ExecuteNonQueryAsync();
                
                var countSql = "SELECT COUNT(*) FROM chat_messages";
                using var command = new SqliteCommand(countSql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch
            {
                return 0;
            }
        }        public async Task<List<ChatMessage>> GetRecentMessagesAsync(int count = 100)
        {
            var messages = new List<ChatMessage>();

            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _logger.LogWarning("Database connection not available, cannot retrieve messages");
                return messages;
            }

            try
            {
                var selectSql = @"
                    SELECT username, message, timestamp, is_system_message
                    FROM chat_messages
                    ORDER BY timestamp DESC
                    LIMIT @count";

                using var command = new SqliteCommand(selectSql, _connection);
                command.Parameters.AddWithValue("@count", count);                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var message = new ChatMessage
                    {
                        Username = reader.GetString(0),
                        Message = reader.GetString(1),
                        Timestamp = reader.GetDateTime(2),
                        IsSystemMessage = reader.GetBoolean(3)
                    };
                    
                    // Parse the message for @mentions
                    MessageParser.ParseChatMessage(message);
                    
                    messages.Add(message);
                }

                _logger.LogDebug("Retrieved {Count} recent messages from database", messages.Count);
                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve recent messages from database");
                return messages;
            }
        }

        public async Task ClearAllMessagesAsync()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _logger.LogWarning("Database connection not available, cannot clear messages");
                return;
            }

            try
            {
                var deleteSql = "DELETE FROM chat_messages";
                using var command = new SqliteCommand(deleteSql, _connection);
                var deletedRows = await command.ExecuteNonQueryAsync();
                
                _logger.LogInformation("Cleared {Count} messages from database", deletedRows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear messages from database");
                throw;
            }
        }

        public static async Task ClearAllMessagesForChannelAsync(string channelName, Platform platform, ILogger logger)
        {
            try
            {
                var dbDirectory = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "db");
                var dbPath = System.IO.Path.Combine(dbDirectory, $"{channelName.ToLower()}_{platform.ToString().ToLower()}.db");
                
                if (!File.Exists(dbPath))
                {
                    logger.LogWarning("Database file not found for channel: {Channel} on {Platform}", channelName, platform);
                    return;
                }

                var connectionString = $"Data Source={dbPath}";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                var deleteSql = "DELETE FROM chat_messages";
                using var command = new SqliteCommand(deleteSql, connection);
                var deletedRows = await command.ExecuteNonQueryAsync();
                
                logger.LogInformation("Cleared {Count} messages from {Channel} on {Platform} database", deletedRows, channelName, platform);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clear messages from {Channel} database", channelName);
                throw;
            }
        }        public static Task DeleteDatabaseForChannelAsync(string channelName, Platform platform, ILogger logger)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var dbDirectory = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "db");
                    var dbPath = System.IO.Path.Combine(dbDirectory, $"{channelName.ToLower()}_{platform.ToString().ToLower()}.db");
                    
                    if (!File.Exists(dbPath))
                    {
                        logger.LogWarning("Database file not found for channel: {Channel} on {Platform}", channelName, platform);
                        return;
                    }

                    // Force garbage collection to ensure any unreferenced connections are disposed
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    // Try to delete the file, with retries if it's locked
                    int retryCount = 0;
                    const int maxRetries = 5;
                    const int retryDelayMs = 100;

                    while (retryCount < maxRetries)
                    {
                        try
                        {
                            // Clear any SQLite connection pools for this database
                            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                            
                            // Delete the database file
                            File.Delete(dbPath);
                            
                            logger.LogInformation("Deleted database file for channel: {Channel} at {Path}", channelName, dbPath);
                            return;
                        }
                        catch (IOException ioEx) when (retryCount < maxRetries - 1)
                        {
                            retryCount++;
                            logger.LogWarning("Database file is locked (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}ms: {Channel} - {Error}", 
                                retryCount, maxRetries, retryDelayMs, channelName, ioEx.Message);
                            
                            await Task.Delay(retryDelayMs);
                        }
                    }
                    
                    // If we get here, all retries failed
                    throw new InvalidOperationException($"Could not delete database file for channel '{channelName}' after {maxRetries} attempts. The file may be locked by another process.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete database file for channel: {Channel}", channelName);
                    throw;
                }
            });
        }

        public async Task CloseConnectionAsync()
        {
            if (_connection != null)
            {
                try
                {
                    await _connection.CloseAsync();
                    await _connection.DisposeAsync();
                    _connection = null;
                    _logger.LogInformation("Database connection closed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing database connection");
                }
            }
        }

        public void Dispose()
        {
            CloseConnectionAsync().Wait(TimeSpan.FromSeconds(2));
        }

        public async Task SetPlatformAsync(Platform platform)
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _logger.LogWarning("Database connection not available, cannot set platform");
                return;
            }

            try
            {
                var upsertSql = @"
                    INSERT INTO channel_metadata (key, value, updated_at)
                    VALUES ('platform', @platform, CURRENT_TIMESTAMP)
                    ON CONFLICT(key) DO UPDATE SET 
                        value = @platform,
                        updated_at = CURRENT_TIMESTAMP";

                using var command = new SqliteCommand(upsertSql, _connection);
                command.Parameters.AddWithValue("@platform", platform.ToString());

                await command.ExecuteNonQueryAsync();
                
                _logger.LogDebug("Set platform metadata to: {Platform}", platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set platform metadata: {Platform}", platform);
            }
        }

        public async Task<Platform> GetPlatformAsync()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _logger.LogWarning("Database connection not available, returning default platform (Twitch)");
                return Platform.Twitch;
            }

            try
            {
                var selectSql = @"
                    SELECT value FROM channel_metadata WHERE key = 'platform' LIMIT 1";

                using var command = new SqliteCommand(selectSql, _connection);
                var result = await command.ExecuteScalarAsync();

                if (result != null && Enum.TryParse<Platform>(result.ToString(), out var platform))
                {
                    _logger.LogDebug("Retrieved platform metadata: {Platform}", platform);
                    return platform;
                }
                else
                {
                    _logger.LogDebug("No platform metadata found, defaulting to Twitch");
                    return Platform.Twitch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get platform metadata, defaulting to Twitch");
                return Platform.Twitch;
            }
        }

        public static Task<Platform> GetPlatformByPathAsync(string channelName, Platform platform, ILogger logger = null)
        {
            // With the new naming convention, platform is embedded in the filename
            // This method is kept for compatibility but platform is now explicit
            logger?.LogDebug("Platform for channel {Channel} is {Platform} (from filename)", channelName, platform);
            return Task.FromResult(platform);
        }

        // Legacy compatibility method - tries to detect platform from database metadata
        public static async Task<Platform> GetPlatformByPathAsync(string channelName, ILogger logger = null)
        {
            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                
                // First try new naming convention files
                foreach (var platform in Enum.GetValues<Platform>())
                {
                    var dbPath = Path.Combine(dbDirectory, $"{channelName.ToLower()}_{platform.ToString().ToLower()}.db");
                    if (File.Exists(dbPath))
                    {
                        logger?.LogDebug("Found database file for {Channel} with platform {Platform}", channelName, platform);
                        return platform;
                    }
                }
                
                // Fall back to legacy naming - check the database metadata
                var legacyDbPath = Path.Combine(dbDirectory, $"{channelName.ToLower()}.db");
                if (File.Exists(legacyDbPath))
                {
                    return await GetPlatformFromLegacyDatabaseAsync(legacyDbPath, logger);
                }
                
                logger?.LogWarning("No database file found for channel {Channel}, defaulting to Twitch", channelName);
                return Platform.Twitch;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error detecting platform for channel {Channel}, defaulting to Twitch", channelName);
                return Platform.Twitch;
            }
        }

        /// <summary>
        /// Discovers channel and platform information from database files in the directory
        /// Handles both old naming convention (channelname.db) and new naming convention (channelname_platform.db)
        /// </summary>
        public static async Task<List<(string ChannelName, Platform Platform)>> DiscoverChannelsFromDatabasesAsync(ILogger logger = null)
        {
            var channels = new List<(string ChannelName, Platform Platform)>();
            
            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                if (!Directory.Exists(dbDirectory))
                {
                    logger?.LogDebug("Database directory not found");
                    return channels;
                }

                var dbFiles = Directory.GetFiles(dbDirectory, "*.db")
                    .Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm"))
                    .ToList();
                
                foreach (var dbFile in dbFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(dbFile);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    // Check if this is the new naming convention (channelname_platform.db)
                    var parts = fileName.Split('_');
                    if (parts.Length >= 2)
                    {
                        var channelName = string.Join("_", parts.Take(parts.Length - 1)); // All parts except the last one
                        var platformString = parts[parts.Length - 1]; // Last part
                        
                        if (Enum.TryParse<Platform>(platformString, true, out var platform))
                        {
                            channels.Add((channelName, platform));
                            logger?.LogDebug("Discovered channel from new naming convention: {Channel} ({Platform})", channelName, platform);
                            continue;
                        }
                    }
                    
                    // Fall back to old naming convention - query database for platform
                    var legacyPlatform = await GetPlatformFromLegacyDatabaseAsync(dbFile, logger);
                    channels.Add((fileName, legacyPlatform));
                    logger?.LogDebug("Discovered channel from legacy naming convention: {Channel} ({Platform})", fileName, legacyPlatform);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error discovering channels from databases");
            }
            
            return channels;
        }

        /// <summary>
        /// Gets platform information from a legacy database file (old naming convention)
        /// </summary>
        private static async Task<Platform> GetPlatformFromLegacyDatabaseAsync(string dbPath, ILogger logger = null)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath};");
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
                    logger?.LogDebug("Metadata table not found in legacy database, defaulting to Twitch");
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
                    logger?.LogDebug("No platform metadata found in legacy database, defaulting to Twitch");
                    return Platform.Twitch;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error reading platform from legacy database, defaulting to Twitch");
                return Platform.Twitch;
            }
        }
    }
}
