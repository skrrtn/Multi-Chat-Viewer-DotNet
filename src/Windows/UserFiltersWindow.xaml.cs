using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace MultiChatViewer
{
    public partial class UserFiltersWindow : Window
    {
        private readonly UserFilterService _userFilterService;
        private readonly ILogger<UserFiltersWindow> _logger;
        private readonly ObservableCollection<string> _blacklistedUsers = [];
        public UserFiltersWindow(UserFilterService userFilterService, ILogger<UserFiltersWindow> logger)
        {
            InitializeComponent();
            
            _userFilterService = userFilterService;
            _logger = logger;

            // Enable dark mode title bar
            DarkModeHelper.EnableDarkMode(this);

            // Bind the list to the UI
            BlacklistedUsersListBox.ItemsSource = _blacklistedUsers;

            // Load existing blacklisted users
            LoadBlacklistedUsers();

            // Subscribe to service events
            _userFilterService.UserAdded += OnUserAdded;
            _userFilterService.UserRemoved += OnUserRemoved;

            // Update UI
            UpdateUI();

            _logger.LogInformation("User Filters window opened");
        }

        private void LoadBlacklistedUsers()
        {
            try
            {
                _blacklistedUsers.Clear();
                var users = _userFilterService.GetBlacklistedUsers();
                
                foreach (var user in users)
                {
                    _blacklistedUsers.Add(user);
                }

                _logger.LogDebug("Loaded {Count} blacklisted users into UI", users.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading blacklisted users");
                StatusTextBlock.Text = "Error loading blacklisted users";
            }
        }

        private async void AddUserButton_Click(object sender, RoutedEventArgs e)
        {
            await AddUserAsync();
        }

        private async void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AddUserAsync();
            }
        }

        private async System.Threading.Tasks.Task AddUserAsync()
        {
            try
            {
                var username = UsernameTextBox.Text?.Trim();
                
                if (string.IsNullOrEmpty(username))
                {
                    StatusTextBlock.Text = "Please enter a username";
                    UsernameTextBox.Focus();
                    return;
                }                // Remove @ symbol if present
                if (username.StartsWith('@'))
                {
                    username = username[1..];
                }

                var success = await _userFilterService.AddUserAsync(username);
                
                if (success)
                {
                    StatusTextBlock.Text = $"Added '{username}' to blacklist";
                    UsernameTextBox.Clear();
                    UsernameTextBox.Focus();
                }
                else
                {
                    StatusTextBlock.Text = $"'{username}' is already blacklisted";
                }

                UpdateUI();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding user to blacklist");
                StatusTextBlock.Text = "Error adding user to blacklist";
            }
        }

        private async void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {                var selectedUsers = BlacklistedUsersListBox.SelectedItems.Cast<string>().ToList();
                
                if (selectedUsers.Count == 0)
                {
                    StatusTextBlock.Text = "No users selected for removal";
                    return;
                }

                var removedCount = 0;
                foreach (var user in selectedUsers)
                {
                    var success = await _userFilterService.RemoveUserAsync(user);
                    if (success)
                    {
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    StatusTextBlock.Text = removedCount == 1 
                        ? $"Removed 1 user from blacklist"
                        : $"Removed {removedCount} users from blacklist";
                }

                UpdateUI();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing selected users from blacklist");
                StatusTextBlock.Text = "Error removing users from blacklist";
            }
        }

        private async void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_blacklistedUsers.Count == 0)
                {
                    StatusTextBlock.Text = "No users to clear";
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to remove all {_blacklistedUsers.Count} blacklisted users?",
                    "Clear All Users",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _userFilterService.ClearAllAsync();
                    StatusTextBlock.Text = "Cleared all blacklisted users";
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all blacklisted users");
                StatusTextBlock.Text = "Error clearing blacklisted users";
            }
        }

        private void BlacklistedUsersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUI();
        }

        private void OnUserAdded(object sender, string username)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_blacklistedUsers.Contains(username, StringComparer.OrdinalIgnoreCase))
                {
                    // Insert in alphabetical order
                    var insertIndex = 0;
                    for (int i = 0; i < _blacklistedUsers.Count; i++)
                    {
                        if (string.Compare(username, _blacklistedUsers[i], StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            insertIndex = i;
                            break;
                        }
                        insertIndex = i + 1;
                    }
                    
                    _blacklistedUsers.Insert(insertIndex, username);
                    UpdateUI();
                }
            });
        }

        private void OnUserRemoved(object sender, string username)
        {
            Dispatcher.Invoke(() =>
            {
                var userToRemove = _blacklistedUsers.FirstOrDefault(u => 
                    string.Equals(u, username, StringComparison.OrdinalIgnoreCase));
                
                if (userToRemove != null)
                {
                    _blacklistedUsers.Remove(userToRemove);
                    UpdateUI();
                }
            });
        }

        private void UpdateUI()
        {
            // Update count
            CountTextBlock.Text = _blacklistedUsers.Count == 1 
                ? "1 user blacklisted"
                : $"{_blacklistedUsers.Count} users blacklisted";

            // Update remove button state
            RemoveSelectedButton.IsEnabled = BlacklistedUsersListBox.SelectedItems.Count > 0;

            // Update clear all button state
            ClearAllButton.IsEnabled = _blacklistedUsers.Count > 0;

            // Clear status after a delay if it's not an error message
            if (!string.IsNullOrEmpty(StatusTextBlock.Text) && 
                !StatusTextBlock.Text.StartsWith("Error"))
            {
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (s, e) =>
                {
                    StatusTextBlock.Text = "";
                    timer.Stop();
                };
                timer.Start();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            _userFilterService.UserAdded -= OnUserAdded;
            _userFilterService.UserRemoved -= OnUserRemoved;

            _logger.LogInformation("User Filters window closed");
            base.OnClosed(e);
        }
    }
}
