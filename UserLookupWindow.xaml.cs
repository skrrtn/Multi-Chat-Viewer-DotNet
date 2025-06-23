using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public partial class UserLookupWindow : Window, INotifyPropertyChanged
    {
        private readonly ILogger<UserLookupWindow> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _currentChannel;

        public ObservableCollection<UserSearchResult> SearchResults { get; } = [];
        
        private bool _isSearching = false;
        private string _lastSearchText = "";
        private UserSearchResult _selectedUser;

        public event PropertyChangedEventHandler PropertyChanged;

        public UserLookupWindow(
            ILogger<UserLookupWindow> logger,
            IServiceProvider serviceProvider,
            string currentChannel = null)
        {
            InitializeComponent();
            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _currentChannel = currentChannel;

            DataContext = this;

            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);

            // Bind collections
            UsersListBox.ItemsSource = SearchResults;

            // Focus on search box
            SearchTextBox.Focus();

            _logger.LogInformation("User Lookup window opened");
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = SearchTextBox.Text?.Trim();
            
            // Don't search for very short strings or if already searching the same text
            if (string.IsNullOrEmpty(searchText) || searchText.Length < 2)
            {
                ClearResults();
                ShowStatus("Enter at least 2 characters to search...");
                return;
            }

            if (searchText == _lastSearchText)
                return;

            _lastSearchText = searchText;

            // Debounce the search - wait a bit before searching
            await Task.Delay(300);
            
            // Check if the text is still the same after delay (user might have continued typing)
            if (SearchTextBox.Text?.Trim() != searchText)
                return;

            await PerformSearchAsync(searchText);
        }

        private async Task PerformSearchAsync(string searchText)
        {
            if (_isSearching)
                return;

            try
            {
                _isSearching = true;
                ShowLoading(true);
                ClearResults();                var users = await SearchUsersAsync(searchText);
                
                foreach (var user in users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
                {
                    SearchResults.Add(user);
                }

                ShowLoading(false);
                UpdateResultCount();

                if (SearchResults.Count == 0)
                {
                    ShowStatus($"No users found matching '{searchText}'");
                }
                else
                {
                    ShowStatus("");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for users with text: {SearchText}", searchText);
                ShowStatus("Error occurred while searching. Please try again.");
                ShowLoading(false);
            }
            finally
            {
                _isSearching = false;
            }
        }

        private async Task<List<UserSearchResult>> SearchUsersAsync(string searchText)
        {
            var userResults = new Dictionary<string, UserSearchResult>();

            try
            {
                var dbDirectory = Path.Combine(Directory.GetCurrentDirectory(), "db");
                if (!Directory.Exists(dbDirectory))
                {
                    _logger.LogWarning("Database directory not found: {Directory}", dbDirectory);
                    return [];
                }

                var dbFiles = Directory.GetFiles(dbDirectory, "*.db");
                _logger.LogDebug("Searching {Count} database files for users matching: {SearchText}", dbFiles.Length, searchText);

                foreach (var dbFile in dbFiles)
                {
                    try
                    {
                        var channelName = Path.GetFileNameWithoutExtension(dbFile);
                        var channelUsers = await SearchUsersInChannelAsync(searchText, channelName, dbFile);
                        
                        foreach (var channelUser in channelUsers)
                        {
                            if (userResults.TryGetValue(channelUser.Username.ToLower(), out var existingUser))
                            {
                                // User already found in another channel - update totals
                                existingUser.TotalMessages += channelUser.MessageCount;
                                existingUser.ChannelCount++;
                                existingUser.Channels.Add(new UserChannelInfo 
                                { 
                                    ChannelName = channelName, 
                                    MessageCount = channelUser.MessageCount,
                                    LastMessageTime = channelUser.LastMessageTime
                                });
                            }
                            else
                            {
                                // New user found
                                var userResult = new UserSearchResult
                                {
                                    Username = channelUser.Username, // Use original casing from first occurrence
                                    TotalMessages = channelUser.MessageCount,
                                    ChannelCount = 1,
                                    Channels =
                                    [
                                        new()
                                        {
                                            ChannelName = channelName,
                                            MessageCount = channelUser.MessageCount,
                                            LastMessageTime = channelUser.LastMessageTime
                                        }
                                    ]
                                };
                                userResults[channelUser.Username.ToLower()] = userResult;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error searching database file: {File}", dbFile);
                        // Continue with other files
                    }
                }

                _logger.LogInformation("Found {UserCount} users matching '{SearchText}' across {FileCount} database files", 
                    userResults.Count, searchText, dbFiles.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for users with text: {SearchText}", searchText);
                throw;
            }

            return [..userResults.Values];
        }

        private async Task<List<ChannelUserResult>> SearchUsersInChannelAsync(string searchText, string channelName, string dbPath)
        {
            var users = new List<ChannelUserResult>();

            try
            {
                var connectionString = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                // Set busy timeout for concurrent operations
                using var timeoutCommand = new SqliteCommand("PRAGMA busy_timeout=5000;", connection);
                await timeoutCommand.ExecuteNonQueryAsync();

                var searchSql = @"
                    SELECT username, COUNT(*) as message_count, MAX(timestamp) as last_message
                    FROM chat_messages 
                    WHERE LOWER(username) LIKE LOWER(@searchText)
                      AND is_system_message = 0
                    GROUP BY LOWER(username)
                    ORDER BY message_count DESC";

                using var command = new SqliteCommand(searchSql, connection);
                command.Parameters.AddWithValue("@searchText", $"%{searchText}%");

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    users.Add(new ChannelUserResult
                    {
                        Username = reader.GetString(0),
                        MessageCount = reader.GetInt32(1),
                        LastMessageTime = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2)
                    });
                }

                _logger.LogDebug("Found {UserCount} users matching '{SearchText}' in channel {Channel}", 
                    users.Count, searchText, channelName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching users in channel {Channel} with text '{SearchText}'", 
                    channelName, searchText);
            }

            return users;
        }        private void ClearResults()
        {
            SearchResults.Clear();
            UpdateResultCount();
            _selectedUser = null;
        }

        private void ShowLoading(bool isLoading)
        {
            LoadingTextBlock.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            
            if (isLoading)
            {
                StatusPanel.Visibility = Visibility.Visible;
                UsersListBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusPanel.Visibility = SearchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UsersListBox.Visibility = SearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ShowStatus(string message)
        {
            StatusTextBlock.Text = message;
            StatusPanel.Visibility = !string.IsNullOrEmpty(message) && SearchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UsersListBox.Visibility = SearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateResultCount()
        {
            if (SearchResults.Count == 0)
            {
                ResultCountTextBlock.Text = "";
            }
            else
            {
                var totalMessages = SearchResults.Sum(u => u.TotalMessages);
                ResultCountTextBlock.Text = $"{SearchResults.Count} user{(SearchResults.Count != 1 ? "s" : "")} • {totalMessages:N0} total messages";
            }
        }        private void UsersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedUser = UsersListBox.SelectedItem as UserSearchResult;
        }        private void UsersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedUser != null)
            {
                OpenUserMessagesWindow(_selectedUser);
            }
        }        private void OpenUserMessagesWindow(UserSearchResult user)
        {
            if (user == null)
                return;

            try
            {
                // Get required dependencies from service provider
                var userMessageService = _serviceProvider.GetRequiredService<UserMessageLookupService>();
                var logger = _serviceProvider.GetRequiredService<ILogger<UserMessagesWindow>>();
                
                // Create UserMessagesWindow with required dependencies
                var userMessagesWindow = new UserMessagesWindow(
                    userMessageService,
                    logger,
                    user.Username,
                    _currentChannel)
                {
                    Owner = this
                };
                userMessagesWindow.Show();

                _logger.LogInformation("Opened user messages window for user: {Username}", user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening user messages window for user: {Username}", user.Username);                
                System.Windows.MessageBox.Show("Error opening user messages window. Please try again.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosed(EventArgs e)
        {
            _logger.LogInformation("User Lookup window closed");
            base.OnClosed(e);
        }    }

    public class UserSearchResult
    {        public string Username { get; set; }
        public int TotalMessages { get; set; }
        public int ChannelCount { get; set; }
        public List<UserChannelInfo> Channels { get; set; } = [];
        
        public string DisplayText => $"({TotalMessages} message{(TotalMessages != 1 ? "s" : "")} | {ChannelCount} channel{(ChannelCount != 1 ? "s" : "")})";
    }

    public class ChannelUserResult
    {
        public string Username { get; set; }
        public int MessageCount { get; set; }
        public DateTime LastMessageTime { get; set; }
    }
}
