using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MultiChatViewer.Services;

namespace MultiChatViewer
{
    public static partial class MessageParser
    {
        // Regex pattern to match @username mentions
        // Matches @ followed by alphanumeric characters, underscores, and hyphens
        [GeneratedRegex(@"@([a-zA-Z0-9_-]+)", RegexOptions.Compiled)]
        private static partial Regex MentionRegex();

        // Regex pattern to match potential emote words
        // Matches words that could be emotes (alphanumeric characters, underscores, etc.)
        [GeneratedRegex(@"\b[a-zA-Z0-9_]+\b", RegexOptions.Compiled)]
        private static partial Regex WordRegex();

        /// <summary>
        /// Parses a chat message and identifies @username mentions and emotes
        /// </summary>
        /// <param name="message">The raw message text</param>
        /// <param name="emoteService">Optional emote service for emote detection</param>
        /// <returns>List of MessagePart objects representing the parsed message</returns>
        public static List<MessagePart> ParseMessage(string message, EmoteService emoteService = null)
        {
            var parts = new List<MessagePart>();
            
            if (string.IsNullOrEmpty(message))
            {
                return parts;
            }

            var mentionMatches = MentionRegex().Matches(message);
            var allMatches = new List<(int Index, int Length, string Text, bool IsMention, string MentionedUsername, bool IsEmote, string EmoteUrl)>();

            // Add mention matches
            foreach (Match match in mentionMatches)
            {
                allMatches.Add((match.Index, match.Length, match.Value, true, match.Groups[1].Value, false, string.Empty));
            }

            // Add emote matches if emote service is available
            if (emoteService != null)
            {
                var wordMatches = WordRegex().Matches(message);
                foreach (Match match in wordMatches)
                {
                    // Skip if this word is already part of a mention
                    bool isPartOfMention = false;
                    foreach (Match mentionMatch in mentionMatches)
                    {
                        if (match.Index >= mentionMatch.Index && match.Index < mentionMatch.Index + mentionMatch.Length)
                        {
                            isPartOfMention = true;
                            break;
                        }
                    }

                    if (!isPartOfMention && emoteService.IsEmote(match.Value))
                    {
                        var emote = emoteService.GetEmote(match.Value);
                        if (emote != null)
                        {
                            allMatches.Add((match.Index, match.Length, match.Value, false, string.Empty, true, emote.Url));
                        }
                    }
                }
            }

            // Sort matches by index
            allMatches.Sort((a, b) => a.Index.CompareTo(b.Index));

            int lastIndex = 0;
            
            foreach (var match in allMatches)
            {
                // Add text before this match (if any)
                if (match.Index > lastIndex)
                {
                    var beforeText = message[lastIndex..match.Index];
                    if (!string.IsNullOrEmpty(beforeText))
                    {
                        parts.Add(new MessagePart
                        {
                            Text = beforeText,
                            IsMention = false,
                            IsEmote = false
                        });
                    }
                }

                // Add the match (mention or emote)
                parts.Add(new MessagePart
                {
                    Text = match.Text,
                    IsMention = match.IsMention,
                    MentionedUsername = match.MentionedUsername,
                    IsEmote = match.IsEmote,
                    EmoteUrl = match.EmoteUrl
                });

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text after the last match (if any)
            if (lastIndex < message.Length)
            {
                var remainingText = message[lastIndex..];
                if (!string.IsNullOrEmpty(remainingText))
                {
                    parts.Add(new MessagePart
                    {
                        Text = remainingText,
                        IsMention = false,
                        IsEmote = false
                    });
                }
            }

            // If no matches were found, return the entire message as a single part
            if (parts.Count == 0)
            {
                parts.Add(new MessagePart
                {
                    Text = message,
                    IsMention = false,
                    IsEmote = false
                });
            }

            return parts;
        }

        /// <summary>
        /// Updates a ChatMessage with parsed message parts
        /// </summary>
        /// <param name="chatMessage">The ChatMessage to update</param>
        /// <param name="emoteService">Optional emote service for emote detection</param>
        public static void ParseChatMessage(ChatMessage chatMessage, EmoteService emoteService = null)
        {
            if (chatMessage == null) return;
            
            // For system messages, skip emote parsing to preserve original text
            // Only parse for mentions but not emotes
            if (chatMessage.IsSystemMessage)
            {
                chatMessage.ParsedMessage = ParseMessage(chatMessage.Message, null); // Pass null to skip emote parsing
            }
            else
            {
                chatMessage.ParsedMessage = ParseMessage(chatMessage.Message, emoteService);
            }
        }
    }
}
