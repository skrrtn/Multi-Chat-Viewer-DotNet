using System.Windows;
using System.Windows.Controls;

namespace TwitchChatViewer
{
    /// <summary>
    /// Data template selector for message display items
    /// </summary>
    public class MessageDisplayTemplateSelector : DataTemplateSelector
    {
        public DataTemplate DateDividerTemplate { get; set; }
        public DataTemplate ChatMessageTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is DateDividerItem)
            {
                return DateDividerTemplate;
            }
            else if (item is ChatMessageDisplayItem)
            {
                return ChatMessageTemplate;
            }

            return base.SelectTemplate(item, container);
        }
    }
}
