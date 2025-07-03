using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TwitchChatViewer
{
    /// <summary>
    /// Helper class for migrating legacy databases to include platform metadata
    /// </summary>
    public class DatabaseMigrationHelper(string dbDirectory)
    {
        private readonly string _dbDirectory = dbDirectory;

        /// <summary>
        /// Migrates all databases in the db directory to include platform metadata
        /// </summary>
        public async Task<MigrationResult> MigrateAllDatabasesAsync()
        {
            var result = new MigrationResult();

            if (!Directory.Exists(_dbDirectory))
            {
                result.ErrorMessage = $"Database directory not found: {_dbDirectory}";
                return result;
            }

            // Get all .db files (excluding WAL and SHM files)
            var dbFiles = Directory.GetFiles(_dbDirectory, "*.db")
                .Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm"))
                .ToList();

            foreach (var dbFile in dbFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(dbFile);
                    
                    // Skip files that already follow the new naming convention (channelname_platform.db)
                    var parts = fileName.Split('_');
                    if (parts.Length >= 2)
                    {
                        var platformString = parts[parts.Length - 1];
                        if (Enum.TryParse<Platform>(platformString, true, out _))
                        {
                            result.AlreadyMigratedDatabases.Add(Path.GetFileName(dbFile));
                            continue; // Skip this file - it's already using the new naming convention
                        }
                    }
                    
                    var migrationNeeded = await CheckIfMigrationNeededAsync(dbFile);
                    if (migrationNeeded)
                    {
                        await MigrateDatabaseAsync(dbFile);
                        result.MigratedDatabases.Add(Path.GetFileName(dbFile));
                    }
                    else
                    {
                        result.AlreadyMigratedDatabases.Add(Path.GetFileName(dbFile));
                    }
                }
                catch (Exception ex)
                {
                    result.FailedDatabases.Add(Path.GetFileName(dbFile), ex.Message);
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if a database needs migration (lacks platform metadata or uses old naming convention)
        /// </summary>
        private static async Task<bool> CheckIfMigrationNeededAsync(string dbPath)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(dbPath);
                
                // Files that don't contain underscore are legacy files that need migration
                if (!fileName.Contains('_'))
                {
                    return true; // Needs migration - old naming convention
                }
                
                // Files with underscores might already be migrated, but check the metadata structure
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                await connection.OpenAsync();

                // Check if the new metadata table exists
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='channel_metadata';";
                var newTableExists = (long)await command.ExecuteScalarAsync() > 0;

                if (newTableExists)
                {
                    // Check if platform info exists in the new table
                    command.CommandText = "SELECT COUNT(*) FROM channel_metadata WHERE key='platform';";
                    var platformExists = (long)await command.ExecuteScalarAsync() > 0;
                    return !platformExists; // Needs migration if no platform info
                }

                // Check if the old metadata table exists
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='metadata';";
                var oldTableExists = (long)await command.ExecuteScalarAsync() > 0;

                if (oldTableExists)
                {
                    // Has old table structure, needs migration
                    return true;
                }

                return true; // Needs migration - no metadata table at all
            }
            catch
            {
                return true; // If we can't check, assume it needs migration
            }
        }

        /// <summary>
        /// Migrates a single database to include platform metadata and rename to new naming convention
        /// </summary>
        private static async Task MigrateDatabaseAsync(string dbPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(dbPath);
            var directory = Path.GetDirectoryName(dbPath);
            
            // First, update the database metadata
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();

                // Initialize metadata table if it doesn't exist
                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS channel_metadata (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        key TEXT NOT NULL UNIQUE,
                        value TEXT NOT NULL,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";
                await command.ExecuteNonQueryAsync();

                // Migrate data from old metadata table if it exists
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='metadata';";
                var oldTableExists = (long)await command.ExecuteScalarAsync() > 0;

                if (oldTableExists)
                {
                    // Copy platform data from old table if it exists
                    command.CommandText = @"
                        INSERT OR IGNORE INTO channel_metadata (key, value, updated_at)
                        SELECT key, value, CURRENT_TIMESTAMP FROM metadata WHERE key = 'platform'";
                    await command.ExecuteNonQueryAsync();
                    
                    // Drop the old table
                    command.CommandText = "DROP TABLE metadata;";
                    await command.ExecuteNonQueryAsync();
                }

                // Set platform to Twitch for legacy databases if not already set
                command.CommandText = @"
                    INSERT INTO channel_metadata (key, value, updated_at)
                    VALUES ('platform', 'Twitch', CURRENT_TIMESTAMP)
                    ON CONFLICT(key) DO UPDATE SET 
                        value = CASE WHEN channel_metadata.value = '' THEN 'Twitch' ELSE channel_metadata.value END,
                        updated_at = CURRENT_TIMESTAMP";
                await command.ExecuteNonQueryAsync();

                // Also add migration timestamp
                command.CommandText = @"
                    INSERT INTO channel_metadata (key, value, updated_at)
                    VALUES ('migrated_at', @timestamp, CURRENT_TIMESTAMP)
                    ON CONFLICT(key) DO UPDATE SET 
                        value = @timestamp,
                        updated_at = CURRENT_TIMESTAMP";
                command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            // Only rename file if it doesn't already follow the new naming convention
            if (!fileName.Contains('_'))
            {
                // Ensure all connections are closed and SQLite connection pools are cleared
                SqliteConnection.ClearAllPools();
                
                // Force garbage collection to ensure file handles are released
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Now rename the file to follow the new naming convention
                var newFileName = $"{fileName}_twitch.db";
                var newDbPath = Path.Combine(directory, newFileName);
                
                // Check if target file already exists
                if (File.Exists(newDbPath))
                {
                    throw new InvalidOperationException($"Cannot rename database '{fileName}.db' to '{newFileName}' because target file already exists.");
                }
                
                // Wait a moment for any file handles to be released
                await Task.Delay(100);
                
                try
                {
                    // Rename the database file
                    File.Move(dbPath, newDbPath);
                    
                    // Also move any associated WAL and SHM files
                    var walPath = dbPath + "-wal";
                    var shmPath = dbPath + "-shm";
                    var newWalPath = newDbPath + "-wal";
                    var newShmPath = newDbPath + "-shm";
                    
                    if (File.Exists(walPath))
                    {
                        File.Move(walPath, newWalPath);
                    }
                    
                    if (File.Exists(shmPath))
                    {
                        File.Move(shmPath, newShmPath);
                    }
                }
                catch (IOException ex) when (ex.Message.Contains("being used by another process"))
                {
                    throw new InvalidOperationException($"Cannot rename database file '{fileName}.db' because it is currently in use. Please close all connections to this database and try again.", ex);
                }
            }
        }

        /// <summary>
        /// Gets platform information from a database file
        /// </summary>
        private static async Task<Platform?> GetPlatformFromDatabaseAsync(string dbPath)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM metadata WHERE key = 'platform';";

                if (await command.ExecuteScalarAsync() is string result && Enum.TryParse<Platform>(result, out var platform))
                {
                    return platform;
                }
            }
            catch
            {
                // Ignore errors and return null
            }

            return null;
        }

        /// <summary>
        /// Gets a summary of databases and their platform information
        /// </summary>
        public async Task<List<DatabaseInfo>> GetDatabaseInfoAsync()
        {
            var result = new List<DatabaseInfo>();

            if (!Directory.Exists(_dbDirectory))
            {
                return result;
            }

            var dbFiles = Directory.GetFiles(_dbDirectory, "*.db")
                .Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm"))
                .ToList();

            foreach (var dbFile in dbFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(dbFile);
                    var info = new DatabaseInfo
                    {
                        FileName = Path.GetFileName(dbFile),
                        ChannelName = fileName,
                        FilePath = dbFile,
                        FileSize = new FileInfo(dbFile).Length
                    };

                    // Determine platform and if migration is needed
                    var usesOldNaming = !fileName.Contains('_');
                    
                    if (usesOldNaming)
                    {
                        // Old naming convention - check database metadata for platform
                        var platform = await GetPlatformFromDatabaseAsync(dbFile);
                        info.Platform = platform?.ToString() ?? "Unknown (Needs Migration)";
                        info.NeedsMigration = true;
                    }
                    else
                    {
                        // New naming convention - extract platform from filename
                        var parts = fileName.Split('_');
                        if (parts.Length >= 2)
                        {
                            var platformString = parts[parts.Length - 1];
                            if (Enum.TryParse<Platform>(platformString, true, out var platform))
                            {
                                info.Platform = platform.ToString();
                                info.ChannelName = string.Join("_", parts.Take(parts.Length - 1));
                                
                                // Still check if metadata exists in case it was manually renamed
                                var dbPlatform = await GetPlatformFromDatabaseAsync(dbFile);
                                info.NeedsMigration = dbPlatform == null;
                            }
                            else
                            {
                                info.Platform = "Unknown Platform";
                                info.NeedsMigration = true;
                            }
                        }
                        else
                        {
                            info.Platform = "Invalid Filename";
                            info.NeedsMigration = true;
                        }
                    }

                    // Get message count
                    using var connection = new SqliteConnection($"Data Source={dbFile}");
                    await connection.OpenAsync();
                    var command = connection.CreateCommand();
                    
                    // Try both possible table names for backward compatibility
                    try
                    {
                        command.CommandText = "SELECT COUNT(*) FROM chat_messages;";
                        info.MessageCount = (long)await command.ExecuteScalarAsync();
                    }
                    catch
                    {
                        // Fallback to old table name if it exists
                        try
                        {
                            command.CommandText = "SELECT COUNT(*) FROM messages;";
                            info.MessageCount = (long)await command.ExecuteScalarAsync();
                        }
                        catch
                        {
                            info.MessageCount = 0;
                        }
                    }

                    result.Add(info);
                }
                catch (Exception ex)
                {
                    result.Add(new DatabaseInfo
                    {
                        FileName = Path.GetFileName(dbFile),
                        ChannelName = Path.GetFileNameWithoutExtension(dbFile),
                        FilePath = dbFile,
                        Platform = "Error",
                        ErrorMessage = ex.Message,
                        NeedsMigration = true
                    });
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Result of database migration operation
    /// </summary>
    public class MigrationResult
    {
        public List<string> MigratedDatabases { get; set; } = [];
        public List<string> AlreadyMigratedDatabases { get; set; } = [];
        public Dictionary<string, string> FailedDatabases { get; set; } = [];
        public string ErrorMessage { get; set; } = string.Empty;

        public bool HasErrors => !string.IsNullOrEmpty(ErrorMessage) || FailedDatabases.Count != 0;
        public bool HasChanges => MigratedDatabases.Count != 0;
        public int TotalDatabases => MigratedDatabases.Count + AlreadyMigratedDatabases.Count + FailedDatabases.Count;
    }

    /// <summary>
    /// Information about a database file
    /// </summary>
    public class DatabaseInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public long MessageCount { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public bool NeedsMigration { get; set; }
    }
}
