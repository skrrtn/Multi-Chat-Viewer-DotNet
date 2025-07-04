using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using KickLib;
using KickLib.Client;
using KickLib.Client.Interfaces;
using KickLib.Client.Models.Args;
using KickLib.Core;
using KickLib.Api.Unofficial;

namespace TwitchChatViewer
{
    public class KickChatClient : IChatClient
    {
        private readonly ILogger<KickChatClient> _logger;
        private IKickClient _kickClient;
        private readonly KickUnofficialApi _kickUnofficialApi;
        private string _currentChannel;
        private bool _isConnected = false;
        private int _chatroomId;
        private readonly string _instanceId;

        public event EventHandler<ChatMessage> MessageReceived;
        public event EventHandler<string> Connected;
        public event EventHandler Disconnected;
        public event EventHandler<string> Error;

        public string CurrentChannel => _currentChannel;
        public bool IsConnected => _isConnected;
        
        /// <summary>
        /// Gets the unique instance ID for this KickChatClient for debugging purposes
        /// </summary>
        public string InstanceId => _instanceId;

        public KickChatClient(ILogger<KickChatClient> logger)
        {
            _logger = logger;
            _kickUnofficialApi = new KickUnofficialApi(logger: logger);
            _instanceId = Guid.NewGuid().ToString("N")[0..8];
            
            // Don't create the KickClient in constructor - create it when needed for better isolation
            _logger.LogInformation("[{InstanceId}] KickChatClient instance created - client will be created on connect", _instanceId);
        }

        public async Task ConnectAsync(string channel)
        {
            try
            {
                // Check if we're already connected to this specific channel
                if (_isConnected && _currentChannel == channel.ToLower())
                {
                    _logger.LogInformation("[{InstanceId}] Already connected to Kick channel: {Channel}", _instanceId, channel);
                    return;
                }

                // Only disconnect if we're connected to a different channel
                if (_isConnected && _currentChannel != channel.ToLower())
                {
                    _logger.LogInformation("[{InstanceId}] Disconnecting from previous Kick channel '{PreviousChannel}' before connecting to '{NewChannel}'", _instanceId, _currentChannel, channel);
                    await DisconnectAsync();
                }

                // Always create a fresh client instance for each new connection to ensure complete isolation
                _logger.LogInformation("[{InstanceId}] Creating new Kick client instance for channel: {Channel}", _instanceId, channel);
                
                // Dispose the old client if it exists
                if (_kickClient != null)
                {
                    try
                    {
                        _kickClient.OnMessage -= OnKickChatMessage;
                        _kickClient.OnConnected -= OnKickConnected;
                        _kickClient.OnDisconnected -= OnKickDisconnected;
                        await _kickClient.DisconnectAsync();
                        
                        // Try to dispose if the client implements IDisposable
                        if (_kickClient is IDisposable disposableClient)
                        {
                            disposableClient.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[{InstanceId}] Error disposing previous KickClient for channel: {Channel}", _instanceId, _currentChannel);
                    }
                }
                
                // Create a completely new KickClient instance with maximum isolation
                _kickClient = new KickClient(_logger);
                _kickClient.OnMessage += OnKickChatMessage;
                _kickClient.OnConnected += OnKickConnected;
                _kickClient.OnDisconnected += OnKickDisconnected;

                _currentChannel = channel.ToLower();
                _logger.LogInformation("[{InstanceId}] Connecting to Kick channel: {Channel}...", _instanceId, _currentChannel);

                // Get chatroom ID using unofficial API (this is the correct ID for websocket connection)
                _logger.LogInformation("[{InstanceId}] Getting chatroom information for Kick channel: {Channel}", _instanceId, _currentChannel);
                
                var chatroomResponse = await _kickUnofficialApi.Channels.GetChatroomAsync(_currentChannel);
                if (chatroomResponse == null)
                {
                    var errorMessage = $"Channel '{_currentChannel}' not found on Kick. Please verify the channel name exists and is public.";
                    _logger.LogError("[{InstanceId}] {ErrorMessage}", _instanceId, errorMessage);
                    throw new Exception(errorMessage);
                }

                _chatroomId = chatroomResponse.Id;
                _logger.LogInformation("[{InstanceId}] Retrieved chatroom ID {ChatroomId} for channel {Channel}", _instanceId, _chatroomId, _currentChannel);

                // Connect to chat using the official client with enhanced isolation
                _logger.LogInformation("[{InstanceId}] Connecting to Kick chatroom {ChatroomId} for channel {Channel}", _instanceId, _chatroomId, _currentChannel);
                
                // First, set up the chatroom listener
                await _kickClient.ListenToChatRoomAsync(_chatroomId);
                
                // Add a significant delay to prevent any potential race conditions or library-level conflicts
                // This is especially important when connecting to multiple Kick channels
                var delayMs = 1000 + (new Random().Next(500, 1500)); // Random delay between 1.5-2.5 seconds
                _logger.LogInformation("[{InstanceId}] Adding random delay of {DelayMs}ms before connecting to prevent conflicts", _instanceId, delayMs);
                await Task.Delay(delayMs);
                
                // Then connect the client
                await _kickClient.ConnectAsync();

                // Wait a moment for the connection to establish and OnKickConnected to be called
                _logger.LogInformation("[{InstanceId}] Kick connection initiated for channel: {Channel}, waiting for connection confirmation...", _instanceId, _currentChannel);
                
                // Wait up to 20 seconds for the connection to be confirmed (increased timeout for multiple connections)
                var timeout = DateTime.Now.AddSeconds(20);
                while (!_isConnected && DateTime.Now < timeout)
                {
                    await Task.Delay(300); // Increased delay to reduce CPU usage
                }

                if (!_isConnected)
                {
                    throw new Exception($"Connection to Kick channel '{_currentChannel}' timed out - no connection confirmation received");
                }

                _logger.LogInformation("[{InstanceId}] Successfully confirmed connection to Kick channel: {Channel}", _instanceId, _currentChannel);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to connect to Kick channel '{_currentChannel}': {ex.Message}";
                _logger.LogError(ex, "[{InstanceId}] {ErrorMessage}", _instanceId, errorMessage);
                Error?.Invoke(this, errorMessage);
                throw new Exception(errorMessage, ex);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_kickClient != null)
                {
                    _logger.LogInformation("[{InstanceId}] Disconnecting from Kick channel: {Channel}", _instanceId, _currentChannel);
                    
                    // Unsubscribe from events first
                    _kickClient.OnMessage -= OnKickChatMessage;
                    _kickClient.OnConnected -= OnKickConnected;
                    _kickClient.OnDisconnected -= OnKickDisconnected;

                    // Disconnect the client
                    await _kickClient.DisconnectAsync();
                    
                    // Try to dispose if the client implements IDisposable
                    if (_kickClient is IDisposable disposableClient)
                    {
                        disposableClient.Dispose();
                    }
                    
                    _kickClient = null;
                }

                _isConnected = false;
                _logger.LogInformation("[{InstanceId}] Successfully disconnected from Kick channel: {Channel}", _instanceId, _currentChannel);
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{InstanceId}] Error during Kick disconnect for channel: {Channel}", _instanceId, _currentChannel);
                Error?.Invoke(this, ex.Message);
                
                // Still mark as disconnected even if there was an error
                _isConnected = false;
            }
        }

        private void OnKickChatMessage(object sender, ChatMessageEventArgs e)
        {
            try
            {
                // Convert Kick chat message to our ChatMessage format
                var chatMessage = new ChatMessage
                {
                    Username = e.Data.Sender?.Username ?? "Unknown",
                    Message = e.Data.Content ?? string.Empty,
                    Timestamp = e.Data.CreatedAt,
                    IsSystemMessage = false,
                    Color = GetUserColor(e.Data.Sender?.Username),
                    SourcePlatform = Platform.Kick  // Set the correct platform for Kick messages
                };

                // Parse the message for @mentions
                MessageParser.ParseChatMessage(chatMessage);

                _logger.LogDebug("[{InstanceId}] Received message on channel {Channel}: {Username} - {Message}", 
                    _instanceId, _currentChannel, chatMessage.Username, chatMessage.Message);
                MessageReceived?.Invoke(this, chatMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{InstanceId}] Error processing Kick chat message", _instanceId);
            }
        }

        private void OnKickConnected(object sender, ClientConnectedArgs e)
        {
            _isConnected = true;
            _logger.LogInformation("[{InstanceId}] Kick chat connected for channel: {Channel}", _instanceId, _currentChannel);
            Connected?.Invoke(this, _currentChannel);
        }

        private void OnKickDisconnected(object sender, EventArgs e)
        {
            _isConnected = false;
            _logger.LogInformation("[{InstanceId}] Kick chat disconnected for channel: {Channel}", _instanceId, _currentChannel);
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        private static string GetUserColor(string username)
        {
            if (string.IsNullOrEmpty(username))
                return "#569cd6"; // Default blue

            // Generate a consistent color based on username hash
            var hash = username.GetHashCode();
            var colors = new[]
            {
                "#ff6b6b", "#4ecdc4", "#45b7d1", "#f9ca24", "#f0932b",
                "#eb4d4b", "#6c5ce7", "#fd79a8", "#fdcb6e", "#e17055",
                "#00b894", "#00cec9", "#0984e3", "#6c5ce7", "#a29bfe"
            };
            
            return colors[Math.Abs(hash) % colors.Length];
        }

        public void Dispose()
        {
            try
            {
                _logger.LogInformation("[{InstanceId}] Disposing KickChatClient for channel: {Channel}", _instanceId, _currentChannel);
                DisconnectAsync().Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{InstanceId}] Error during KickChatClient disposal for channel: {Channel}", _instanceId, _currentChannel);
            }
        }
    }
}
