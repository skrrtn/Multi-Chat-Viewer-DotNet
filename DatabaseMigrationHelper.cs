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
        /// Checks if a database needs migration (lacks platform metadata)
        /// </summary>
        private static async Task<bool> CheckIfMigrationNeededAsync(string dbPath)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                await connection.OpenAsync();

                // Check if metadata table exists
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='metadata';";
                var tableExists = (long)await command.ExecuteScalarAsync() > 0;

                if (!tableExists)
                {
                    return true; // Needs migration
                }

                // Check if platform info exists
                command.CommandText = "SELECT COUNT(*) FROM metadata WHERE key='platform';";
                var platformExists = (long)await command.ExecuteScalarAsync() > 0;

                return !platformExists; // Needs migration if no platform info
            }
            catch
            {
                return true; // If we can't check, assume it needs migration
            }
        }

        /// <summary>
        /// Migrates a single database to include platform metadata
        /// </summary>
        private static async Task MigrateDatabaseAsync(string dbPath)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            // Initialize metadata table if it doesn't exist
            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );";
            await command.ExecuteNonQueryAsync();

            // Set platform to Twitch for legacy databases
            // This is a safe assumption since the app originally only supported Twitch
            command.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES ('platform', 'Twitch');";
            await command.ExecuteNonQueryAsync();

            // Also add migration timestamp
            command.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES ('migrated_at', @timestamp);";
            command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
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
                    var info = new DatabaseInfo
                    {
                        FileName = Path.GetFileName(dbFile),
                        ChannelName = Path.GetFileNameWithoutExtension(dbFile),
                        FilePath = dbFile,
                        FileSize = new FileInfo(dbFile).Length
                    };

                    // Get platform info
                    var platform = await GetPlatformFromDatabaseAsync(dbFile);
                    info.Platform = platform?.ToString() ?? "Unknown";

                    // Get message count
                    using var connection = new SqliteConnection($"Data Source={dbFile}");
                    await connection.OpenAsync();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM messages;";
                    info.MessageCount = (long)await command.ExecuteScalarAsync();

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
                        ErrorMessage = ex.Message
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
    }
}
