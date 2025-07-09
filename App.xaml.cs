using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MultiChatViewer
{    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Set up global exception handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);
            try
            {
                // Set up dependency injection
                var services = new ServiceCollection();
                ConfigureServices(services);
                _serviceProvider = services.BuildServiceProvider();
                // Create and show main window
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Application starting up...");

                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                logger.LogInformation("MainWindow created, showing window...");
                mainWindow.Show();

                // Check for updates after showing main window
                var updateResult = await UpdateChecker.CheckForUpdateAsync();
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
                if (updateResult.UpdateAvailable)
                {
                    var updateWindow = new UpdateAvailableWindow(currentVersion, updateResult.LatestTag, updateResult.ReleaseUrl)
                    {
                        Owner = mainWindow // Set main window as owner
                    };
                    updateWindow.ShowDialog();
                }

                logger.LogInformation("Application startup completed");
            }
            catch (Exception ex)
            {
                var errorDetails = $"Fatal Startup Error:\n\n" +
                                  $"Type: {ex.GetType().Name}\n" +
                                  $"Message: {ex.Message}\n\n" +
                                  $"Stack Trace:\n{ex.StackTrace}";
                
                if (ex.InnerException != null)
                {
                    errorDetails += $"\n\nInner Exception:\n" +
                                   $"Type: {ex.InnerException.GetType().Name}\n" +
                                   $"Message: {ex.InnerException.Message}\n" +
                                   $"Stack Trace: {ex.InnerException.StackTrace}";
                }
                
                var errorWindow = new ErrorWindow(errorDetails);
                errorWindow.ShowDialog();
                throw;
            }
        }        private static void ConfigureServices(ServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register the unified configuration service first
            services.AddSingleton<UnifiedConfigurationService>();

            // Register the blacklist manager for dedicated blacklist file management
            services.AddSingleton<BlacklistManager>();

            // Register chat clients
            services.AddSingleton<TwitchIrcClient>();
            // Note: KickChatClient is NOT registered as singleton - MultiChannelManager creates instances as needed
            services.AddSingleton<KickCredentialsService>();
            
            services.AddSingleton<ChatDatabaseService>();
            services.AddSingleton<ChannelSettingsManager>();
            services.AddSingleton<FollowedChannelsStorage>();
            services.AddSingleton<MultiChannelManager>();
            services.AddSingleton<UserFilterService>();
            services.AddSingleton<UserMessageLookupService>();

            services.AddTransient<FollowedChannelsWindow>();
            services.AddTransient<UserFiltersWindow>();
            services.AddTransient<UserMessagesWindow>();
            services.AddTransient<UserLookupWindow>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<ILogger<App>>(provider => provider.GetRequiredService<ILoggerFactory>().CreateLogger<App>());
        }        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var logger = _serviceProvider?.GetService<ILogger<App>>();
                logger?.LogInformation("Application shutdown initiated");
                
                _serviceProvider?.Dispose();
                
                logger?.LogInformation("Application shutdown completed");
            }
            catch (Exception ex)
            {
                // Log the error but don't prevent shutdown
                var logger = _serviceProvider?.GetService<ILogger<App>>();
                logger?.LogError(ex, "Error during application shutdown");
            }
            
            base.OnExit(e);
        }private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var errorDetails = $"Unhandled Exception Details:\n\n" +
                              $"Type: {e.Exception.GetType().Name}\n" +
                              $"Message: {e.Exception.Message}\n\n" +
                              $"Stack Trace:\n{e.Exception.StackTrace}";
            
            if (e.Exception.InnerException != null)
            {
                errorDetails += $"\n\nInner Exception:\n" +
                               $"Type: {e.Exception.InnerException.GetType().Name}\n" +
                               $"Message: {e.Exception.InnerException.Message}\n" +
                               $"Stack Trace: {e.Exception.InnerException.StackTrace}";
            }
            
            var errorWindow = new ErrorWindow(errorDetails);
            errorWindow.ShowDialog();
            e.Handled = true;        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            var errorDetails = $"Fatal Unhandled Exception Details:\n\n" +
                              $"Type: {exception?.GetType().Name ?? "Unknown"}\n" +
                              $"Message: {exception?.Message ?? "No message available"}\n\n" +
                              $"Stack Trace:\n{exception?.StackTrace ?? "No stack trace available"}\n\n" +
                              $"Is Terminating: {e.IsTerminating}";
            
            if (exception?.InnerException != null)
            {
                errorDetails += $"\n\nInner Exception:\n" +
                               $"Type: {exception.InnerException.GetType().Name}\n" +
                               $"Message: {exception.InnerException.Message}\n" +
                               $"Stack Trace: {exception.InnerException.StackTrace}";
            }
            
            var errorWindow = new ErrorWindow(errorDetails);
            errorWindow.ShowDialog();
        }
    }
}
