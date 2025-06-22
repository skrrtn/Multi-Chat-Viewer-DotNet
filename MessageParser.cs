using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TwitchChatViewer
{
    public static class MessageParser
    {
        // Regex pattern to match @username mentions
        // Matches @ followed by alphanumeric characters, underscores, and hyphens
        private static readonly Regex MentionRegex = new Regex(@"@([a-zA-Z0-9_-]+)", RegexOptions.Compiled);

        /// <summary>
        /// Parses a chat message and identifies @username mentions
        /// </summary>
        /// <param name="message">The raw message text</param>
        /// <returns>List of MessagePart objects representing the parsed message</returns>
        public static List<MessagePart> ParseMessage(string message)
        {
            var parts = new List<MessagePart>();
            
            if (string.IsNullOrEmpty(message))
            {
                return parts;
            }

            var matches = MentionRegex.Matches(message);
            
            if (matches.Count == 0)
            {
                // No mentions found, return the entire message as a single part
                parts.Add(new MessagePart
                {
                    Text = message,
                    IsMention = false
                });
                return parts;
            }

            int lastIndex = 0;
            
            foreach (Match match in matches)
            {
                // Add text before the mention (if any)
                if (match.Index > lastIndex)
                {
                    var beforeText = message.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrEmpty(beforeText))
                    {
                        parts.Add(new MessagePart
                        {
                            Text = beforeText,
                            IsMention = false
                        });
                    }
                }

                // Add the mention
                parts.Add(new MessagePart
                {
                    Text = match.Value, // Include the @ symbol
                    IsMention = true,
                    MentionedUsername = match.Groups[1].Value // Username without @
                });

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text after the last mention (if any)
            if (lastIndex < message.Length)
            {
                var remainingText = message.Substring(lastIndex);
                if (!string.IsNullOrEmpty(remainingText))
                {
                    parts.Add(new MessagePart
                    {
                        Text = remainingText,
                        IsMention = false
                    });
                }
            }

            return parts;
        }

        /// <summary>
        /// Updates a ChatMessage with parsed message parts
        /// </summary>
        /// <param name="chatMessage">The ChatMessage to update</param>
        public static void ParseChatMessage(ChatMessage chatMessage)
        {
            if (chatMessage == null) return;
            
            chatMessage.ParsedMessage = ParseMessage(chatMessage.Message);
        }
    }
}
