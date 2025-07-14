using System;
using System.Collections.Generic;

namespace MultiChatViewer
{
    public class MessagePart
    {
        public string Text { get; set; } = string.Empty;
        public bool IsMention { get; set; } = false;
        public string MentionedUsername { get; set; } = string.Empty;
        public bool IsEmote { get; set; } = false;
        public string EmoteUrl { get; set; } = string.Empty;
    }

    public class ChatMessage
    {
        public string Username { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsSystemMessage { get; set; } = false;
        public string Color { get; set; } = "#569cd6"; // Default blue color for usernames
        public List<MessagePart> ParsedMessage { get; set; } = [];
        public string SourceChannel { get; set; } = string.Empty; // Channel where this message originated
        public Platform SourcePlatform { get; set; } = Platform.Twitch; // Platform where this message originated
    }
}
