using System;

namespace TwitchChatViewer
{
    /// <summary>
    /// Base class for items that can be displayed in the messages list
    /// </summary>
    public abstract class MessageDisplayItem
    {
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Represents a date divider shown between messages from different days
    /// </summary>
    public class DateDividerItem : MessageDisplayItem
    {
        public string DateText { get; set; } = string.Empty;
        
        public DateDividerItem(DateTime date)
        {
            Timestamp = date;
            
            // Format the date based on how old it is
            var today = DateTime.Today;
            var messageDate = date.Date;
            
            // Skip "Today" - no divider needed for current day messages
            if (messageDate == today.AddDays(-1))
            {
                DateText = "Yesterday";
            }
            else if (messageDate >= today.AddDays(-7))
            {
                // Within the last week - show day name
                DateText = messageDate.ToString("dddd, MMMM d");
            }
            else if (messageDate.Year == today.Year)
            {
                // Same year - show month and day
                DateText = messageDate.ToString("MMMM d");
            }
            else
            {
                // Different year - show full date
                DateText = messageDate.ToString("MMMM d, yyyy");
            }
        }
    }

    /// <summary>
    /// Wrapper for chat messages to be displayed in the list
    /// </summary>
    public class ChatMessageDisplayItem : MessageDisplayItem
    {
        public ChatMessageWithChannel Message { get; set; }
        
        public ChatMessageDisplayItem(ChatMessageWithChannel message)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Timestamp = message.Timestamp;
        }
    }
}
