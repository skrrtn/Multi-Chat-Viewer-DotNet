using System;
using System.Threading.Tasks;

namespace TwitchChatViewer
{
    public interface IChatClient : IDisposable
    {
        event EventHandler<ChatMessage> MessageReceived;
        event EventHandler<string> Connected;
        event EventHandler Disconnected;
        event EventHandler<string> Error;

        string CurrentChannel { get; }
        bool IsConnected { get; }

        Task ConnectAsync(string channel);
        Task DisconnectAsync();
    }
}
