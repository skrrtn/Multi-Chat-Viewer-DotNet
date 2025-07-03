using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TwitchChatViewer
{
    public partial class DatabaseMigrationWindow : Window
    {
        private readonly DatabaseMigrationHelper _migrationHelper;
        private readonly ObservableCollection<DatabaseInfoViewModel> _databases = [];

        public DatabaseMigrationWindow()
        {
            InitializeComponent();
            
            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);
            
            var dbDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db");
            _migrationHelper = new DatabaseMigrationHelper(dbDirectory);

            // Set up DataGrid binding
            if (FindName("DatabaseGrid") is DataGrid databaseGrid)
            {
                databaseGrid.ItemsSource = _databases;
            }

            // Auto-scan on startup
            _ = ScanDatabasesAsync();
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            await ScanDatabasesAsync();
        }

        private async void MigrateButton_Click(object sender, RoutedEventArgs e)
        {
            await MigrateDatabasesAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task ScanDatabasesAsync()
        {
            try
            {
                // Get controls by name
                var migrateButton = FindName("MigrateButton") as Button;
                var statusText = FindName("StatusText") as TextBlock;
                
                if (FindName("ScanButton") is Button scanButton) scanButton.IsEnabled = false;
                if (migrateButton != null) migrateButton.IsEnabled = false;
                if (statusText != null) statusText.Text = "Scanning databases...";
                Mouse.OverrideCursor = Cursors.Wait;

                _databases.Clear();

                // Check if database directory exists
                var dbDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db");
                if (!Directory.Exists(dbDirectory))
                {
                    if (statusText != null) statusText.Text = "No database directory found. No databases to migrate.";
                    return;
                }

                var databaseInfos = await _migrationHelper.GetDatabaseInfoAsync();
                
                if (databaseInfos.Count == 0)
                {
                    if (statusText != null) statusText.Text = "No database files found.";
                    return;
                }
                
                foreach (var info in databaseInfos.OrderBy(d => d.ChannelName))
                {
                    var viewModel = new DatabaseInfoViewModel
                    {
                        ChannelName = info.ChannelName,
                        Platform = info.Platform,
                        MessageCount = info.MessageCount,
                        FileSize = info.FileSize,
                        FileSizeFormatted = FormatFileSize(info.FileSize),
                        StatusText = GetStatusText(info),
                        NeedsMigration = info.Platform == "Unknown" || string.IsNullOrEmpty(info.Platform)
                    };
                    
                    _databases.Add(viewModel);
                }

                var needsMigration = _databases.Count(d => d.NeedsMigration);
                var total = _databases.Count;
                
                if (needsMigration > 0)
                {
                    if (statusText != null) statusText.Text = $"Found {needsMigration} database(s) that need migration out of {total} total.";
                    if (migrateButton != null) migrateButton.IsEnabled = true;
                }
                else
                {
                    if (statusText != null) statusText.Text = $"All {total} database(s) are up to date.";
                    if (migrateButton != null) migrateButton.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                if (FindName("StatusText") is TextBlock statusText) statusText.Text = $"Error scanning databases: {ex.Message}";
                MessageBox.Show($"Error scanning databases:\n\n{ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (FindName("ScanButton") is Button scanButton) scanButton.IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }

        private async Task MigrateDatabasesAsync()
        {
            try
            {
                // Get controls by name

                if (FindName("ScanButton") is Button scanButton) scanButton.IsEnabled = false;
                if (FindName("MigrateButton") is Button migrateButton) migrateButton.IsEnabled = false;
                if (FindName("StatusText") is TextBlock statusText) statusText.Text = "Migrating databases...";
                Mouse.OverrideCursor = Cursors.Wait;

                var result = await _migrationHelper.MigrateAllDatabasesAsync();
                
                if (result.HasErrors)
                {
                    var errorMessage = result.ErrorMessage;
                    if (result.FailedDatabases.Count > 0)
                    {
                        errorMessage += "\n\nFailed databases:\n" + 
                                        string.Join("\n", result.FailedDatabases.Select(kv => $"- {kv.Key}: {kv.Value}"));
                    }
                    
                    MessageBox.Show(errorMessage, "Migration Errors", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (result.HasChanges)
                {
                    var message = $"Successfully migrated {result.MigratedDatabases.Count} database(s):\n" +
                                  string.Join("\n", result.MigratedDatabases.Select(db => $"- {db}"));
                    
                    MessageBox.Show(message, "Migration Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No databases needed migration.", "Migration Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Refresh the display
                await ScanDatabasesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during migration: {ex.Message}", "Migration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (FindName("ScanButton") is Button scanButton) scanButton.IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }

        private static string GetStatusText(DatabaseInfo info)
        {
            if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                return "Error";
            }
            
            if (info.Platform == "Unknown" || string.IsNullOrEmpty(info.Platform))
            {
                return "Needs Migration";
            }
            
            return "Up to Date";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            else
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
    }

    public class DatabaseInfoViewModel
    {
        public string ChannelName { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public long MessageCount { get; set; }
        public long FileSize { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public bool NeedsMigration { get; set; }
    }
}
