using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchChatViewer
{
    public class TwitchIrcClient : IChatClient
    {
        private readonly ILogger<TwitchIrcClient> _logger;
        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cancellationTokenSource;
        private string _currentChannel;        // IRC server details
        private const string Server = "irc.chat.twitch.tv";
        private const int Port = 6667;
        private const string Username = "justinfan67420"; // Anonymous user
        private const string Password = "oauth:"; // Anonymous password

        public event EventHandler<ChatMessage> MessageReceived;
        public event EventHandler<string> Connected;
        public event EventHandler Disconnected;
        public event EventHandler<string> Error;

        public string CurrentChannel => _currentChannel;
        public bool IsConnected => _tcpClient?.Connected == true;

        public TwitchIrcClient(ILogger<TwitchIrcClient> logger)
        {
            _logger = logger;
            _logger.LogInformation("TwitchIrcClient initialized");
        }

        public async Task ConnectAsync(string channel)
        {
            try
            {
                if (_tcpClient?.Connected == true)
                {
                    await DisconnectAsync();
                }

                _currentChannel = channel.ToLower().Replace("#", "");
                _cancellationTokenSource = new CancellationTokenSource();                _logger.LogInformation("Connecting to Twitch IRC server for channel: {Channel}...", _currentChannel);
                
                _tcpClient = new TcpClient();
                  // Set a reasonable timeout for connection
                var connectTask = _tcpClient.ConnectAsync(Server, Port);
                var timeoutTask = Task.Delay(15000); // 15 second timeout (increased from 10)
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    _tcpClient?.Close();
                    throw new TimeoutException($"Connection to Twitch IRC server timed out after 15 seconds");
                }
                
                // Ensure the connection task completed successfully
                await connectTask;
                
                if (!_tcpClient.Connected)
                {
                    throw new Exception("Failed to establish connection to Twitch IRC server");
                }

                var stream = _tcpClient.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                _logger.LogInformation("Sending authentication for channel: {Channel}", _currentChannel);
                
                // Send authentication
                await _writer.WriteLineAsync($"PASS {Password}");
                await _writer.WriteLineAsync($"NICK {Username}");
                await _writer.WriteLineAsync($"JOIN #{_currentChannel}");

                _logger.LogInformation("Connected to channel #{Channel}", _currentChannel);
                Connected?.Invoke(this, _currentChannel);

                // Start reading messages
                _ = Task.Run(ReadMessagesAsync, _cancellationTokenSource.Token);            }
            catch (Exception ex)
            {
                var errorMessage = ex switch
                {
                    TimeoutException => $"IRC Connection timeout: Unable to connect to Twitch IRC server within 15 seconds. This may indicate network issues or Twitch server problems.",
                    SocketException socketEx => $"Network error while connecting to IRC: {socketEx.Message} (Error code: {socketEx.ErrorCode})",
                    InvalidOperationException => $"IRC Connection state error: {ex.Message}",
                    UnauthorizedAccessException => $"IRC Authentication error: {ex.Message}",
                    _ => $"IRC Connection failed: {ex.Message}"
                };
                
                _logger.LogError(ex, "❌ Failed to connect to Twitch IRC for channel {Channel}. Error type: {ErrorType}, Details: {ErrorMessage}", 
                    _currentChannel, ex.GetType().Name, errorMessage);
                
                // Add network diagnostics
                try
                {
                    var ping = new System.Net.NetworkInformation.Ping();
                    var reply = await ping.SendPingAsync("8.8.8.8", 5000); // Test internet connectivity
                    _logger.LogInformation("Network connectivity test: {Status} (RTT: {RoundtripTime}ms)", reply.Status, reply.RoundtripTime);
                }
                catch (Exception pingEx)
                {
                    _logger.LogWarning(pingEx, "Could not test network connectivity");
                }
                
                Error?.Invoke(this, errorMessage);
                throw new Exception(errorMessage, ex);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                
                if (_writer != null)
                {
                    await _writer.WriteLineAsync($"PART #{_currentChannel}");
                    _writer.Close();
                    _writer = null;
                }

                _reader?.Close();
                _reader = null;

                _tcpClient?.Close();
                _tcpClient = null;

                _logger.LogInformation("Disconnected from Twitch IRC");
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnect");
                Error?.Invoke(this, ex.Message);
            }
        }

        private async Task ReadMessagesAsync()
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested && _reader != null)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    _logger.LogDebug("Received: {Line}", line);

                    // Handle PING to keep connection alive
                    if (line.StartsWith("PING"))
                    {
                        var pongResponse = line.Replace("PING", "PONG");
                        await _writer.WriteLineAsync(pongResponse);
                        _logger.LogDebug("Sent PONG response");
                        continue;
                    }

                    // Parse chat messages
                    if (line.Contains("PRIVMSG"))
                    {
                        var message = ParseChatMessage(line);
                        if (message != null)
                        {
                            MessageReceived?.Invoke(this, message);
                        }
                    }
                }
            }
            catch (Exception ex) when (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error reading messages");
                Error?.Invoke(this, ex.Message);
            }
        }        private ChatMessage ParseChatMessage(string rawMessage)
        {
            try
            {                // Example format: :username!username@username.tmi.twitch.tv PRIVMSG #channel :message
                var parts = rawMessage.Split(' ', 4);
                if (parts.Length < 4) return null;

                var userPart = parts[0][1..]; // Remove leading ':'
                var username = userPart.Split('!')[0];
                var messagePart = parts[3][1..]; // Remove leading ':'

                var chatMessage = new ChatMessage
                {
                    Username = username,
                    Message = messagePart,
                    Timestamp = DateTime.Now,
                    IsSystemMessage = false,
                    SourcePlatform = Platform.Twitch  // Set the correct platform for Twitch messages
                };

                // Parse the message for @mentions
                MessageParser.ParseChatMessage(chatMessage);

                return chatMessage;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse message: {RawMessage}", rawMessage);
                return null;
            }
        }

        public void Dispose()
        {
            DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
        }
    }
}
