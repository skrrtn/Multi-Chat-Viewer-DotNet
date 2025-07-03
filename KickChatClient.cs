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
        private KickUnofficialApi _kickUnofficialApi;
        private string _currentChannel;
        private bool _isConnected = false;
        private int _chatroomId;

        public event EventHandler<ChatMessage> MessageReceived;
        public event EventHandler<string> Connected;
        public event EventHandler Disconnected;
        public event EventHandler<string> Error;

        public string CurrentChannel => _currentChannel;
        public bool IsConnected => _isConnected;

        public KickChatClient(ILogger<KickChatClient> logger)
        {
            _logger = logger;
            _kickClient = new KickClient(logger);
            _kickUnofficialApi = new KickUnofficialApi(logger: logger);
            
            _kickClient.OnMessage += OnKickChatMessage;
            _kickClient.OnConnected += OnKickConnected;
            _kickClient.OnDisconnected += OnKickDisconnected;
        }

        public async Task ConnectAsync(string channel)
        {
            try
            {
                if (_isConnected)
                {
                    _logger.LogInformation("Disconnecting from previous Kick channel before connecting to new one");
                    await DisconnectAsync();
                }

                _currentChannel = channel.ToLower();
                _logger.LogInformation("Connecting to Kick channel: {Channel}...", _currentChannel);

                // Get chatroom ID using unofficial API (this is the correct ID for websocket connection)
                _logger.LogInformation("Getting chatroom information for Kick channel: {Channel}", _currentChannel);
                
                var chatroomResponse = await _kickUnofficialApi.Channels.GetChatroomAsync(_currentChannel);
                if (chatroomResponse == null)
                {
                    var errorMessage = $"Channel '{_currentChannel}' not found on Kick. Please verify the channel name exists and is public.";
                    _logger.LogError(errorMessage);
                    throw new Exception(errorMessage);
                }

                _chatroomId = chatroomResponse.Id;
                _logger.LogInformation("Retrieved chatroom ID {ChatroomId} for channel {Channel}", _chatroomId, _currentChannel);

                // Connect to chat using the official client
                _logger.LogInformation("Connecting to Kick chatroom {ChatroomId}", _chatroomId);
                await _kickClient.ListenToChatRoomAsync(_chatroomId);
                await _kickClient.ConnectAsync();

                _isConnected = true;
                _logger.LogInformation("Successfully connected to Kick channel: {Channel}", _currentChannel);
                Connected?.Invoke(this, _currentChannel);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to connect to Kick channel '{_currentChannel}': {ex.Message}";
                _logger.LogError(ex, errorMessage);
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
                    // Unsubscribe from events
                    _kickClient.OnMessage -= OnKickChatMessage;
                    _kickClient.OnConnected -= OnKickConnected;
                    _kickClient.OnDisconnected -= OnKickDisconnected;

                    await _kickClient.DisconnectAsync();
                    _kickClient = null;
                }

                _isConnected = false;
                _logger.LogInformation("Disconnected from Kick channel: {Channel}", _currentChannel);
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Kick disconnect");
                Error?.Invoke(this, ex.Message);
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
                    Color = GetUserColor(e.Data.Sender?.Username)
                };

                // Parse the message for @mentions
                MessageParser.ParseChatMessage(chatMessage);

                MessageReceived?.Invoke(this, chatMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Kick chat message");
            }
        }

        private void OnKickConnected(object sender, ClientConnectedArgs e)
        {
            _isConnected = true;
            _logger.LogInformation("Kick chat connected for channel: {Channel}", _currentChannel);
            Connected?.Invoke(this, _currentChannel);
        }

        private void OnKickDisconnected(object sender, EventArgs e)
        {
            _isConnected = false;
            _logger.LogInformation("Kick chat disconnected for channel: {Channel}", _currentChannel);
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
            DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
        }
    }
}
