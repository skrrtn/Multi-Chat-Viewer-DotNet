using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TwitchChatViewer
{    public partial class HighlightedTextBlock : UserControl
    {
        public static readonly DependencyProperty MessagePartsProperty =
            DependencyProperty.Register(nameof(MessageParts), typeof(List<MessagePart>), typeof(HighlightedTextBlock),
                new PropertyMetadata(null, OnMessagePartsChanged));

        public static readonly DependencyProperty CustomFontSizeProperty =
            DependencyProperty.Register(nameof(CustomFontSize), typeof(double), typeof(HighlightedTextBlock),
                new PropertyMetadata(12.0, OnCustomFontSizeChanged));

        public static readonly DependencyProperty TimestampProperty =
            DependencyProperty.Register(nameof(Timestamp), typeof(DateTime), typeof(HighlightedTextBlock),
                new PropertyMetadata(default(DateTime), OnTimestampChanged));

        public static readonly DependencyProperty UsernameProperty =
            DependencyProperty.Register(nameof(Username), typeof(string), typeof(HighlightedTextBlock),
                new PropertyMetadata(string.Empty, OnUsernameChanged));

        public static readonly DependencyProperty IsSystemMessageProperty =
            DependencyProperty.Register(nameof(IsSystemMessage), typeof(bool), typeof(HighlightedTextBlock),
                new PropertyMetadata(false, OnIsSystemMessageChanged));        // Event for username clicks
        public static readonly RoutedEvent UsernameClickEvent = 
            EventManager.RegisterRoutedEvent(nameof(UsernameClick), RoutingStrategy.Bubble, 
                typeof(RoutedEventHandler), typeof(HighlightedTextBlock));

        public event RoutedEventHandler UsernameClick
        {
            add { AddHandler(UsernameClickEvent, value); }
            remove { RemoveHandler(UsernameClickEvent, value); }
        }

        // Event for mention clicks
        public static readonly RoutedEvent MentionClickEvent = 
            EventManager.RegisterRoutedEvent(nameof(MentionClick), RoutingStrategy.Bubble, 
                typeof(RoutedEventHandler), typeof(HighlightedTextBlock));

        public event RoutedEventHandler MentionClick
        {
            add { AddHandler(MentionClickEvent, value); }
            remove { RemoveHandler(MentionClickEvent, value); }
        }

        public List<MessagePart> MessageParts
        {
            get => (List<MessagePart>)GetValue(MessagePartsProperty);
            set => SetValue(MessagePartsProperty, value);
        }

        public double CustomFontSize
        {
            get => (double)GetValue(CustomFontSizeProperty);
            set => SetValue(CustomFontSizeProperty, value);
        }

        public DateTime Timestamp
        {
            get => (DateTime)GetValue(TimestampProperty);
            set => SetValue(TimestampProperty, value);
        }

        public string Username
        {
            get => (string)GetValue(UsernameProperty);
            set => SetValue(UsernameProperty, value);
        }

        public bool IsSystemMessage
        {
            get => (bool)GetValue(IsSystemMessageProperty);
            set => SetValue(IsSystemMessageProperty, value);
        }        private RichTextBox _richTextBox;

        public HighlightedTextBlock()
        {
            InitializeComponent();
        }        private void InitializeComponent()
        {            _richTextBox = new RichTextBox
            {
                IsReadOnly = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsDocumentEnabled = true,
                Document = new FlowDocument()
                {
                    PagePadding = new Thickness(0)
                }
            };

            Content = _richTextBox;
        }

        private static void OnMessagePartsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control.UpdateContent();
            }
        }        private static void OnCustomFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control._richTextBox.FontSize = (double)e.NewValue;
                control.UpdateContent(); // Recalculate hanging indent
            }
        }

        private static void OnTimestampChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control.UpdateContent();
            }
        }

        private static void OnUsernameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control.UpdateContent();
            }
        }

        private static void OnIsSystemMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control.UpdateContent();
            }
        }        private void UpdateContent()
        {
            _richTextBox.Document.Blocks.Clear();

            if (MessageParts == null || MessageParts.Count == 0)
                return;            var paragraph = new Paragraph();
              // Calculate the width of just the timestamp and space to align wrapped lines
            var timestampFontSize = _richTextBox.FontSize * 0.85; // Make timestamp 15% smaller
            var timestampText = $"[{Timestamp:hh:mm:ss tt}] ";
            var formattedText = new FormattedText(
                timestampText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(_richTextBox.FontFamily, _richTextBox.FontStyle, _richTextBox.FontWeight, _richTextBox.FontStretch),
                timestampFontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            var timestampWidth = formattedText.Width;            // Set hanging indent - subsequent lines will start at the beginning of the username
            // Reduce left margin by 3 pixels to move content closer to the left side
            paragraph.TextIndent = -timestampWidth;
            paragraph.Margin = new Thickness(timestampWidth - 3, 0, 0, 0);

            // Create timestamp run with brackets
            var timestampRun = new Run($"[{Timestamp:hh:mm:ss tt}]")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)), // #808080
                FontSize = timestampFontSize
            };
            paragraph.Inlines.Add(timestampRun);

            // Add space after timestamp
            paragraph.Inlines.Add(new Run(" "));            // Create username run with click functionality
            var usernameBrush = IsSystemMessage ? 
                new SolidColorBrush(Color.FromRgb(220, 220, 170)) : // #dcdcaa for system messages
                new SolidColorBrush(Color.FromRgb(86, 156, 214));   // #569cd6 for regular messages

            var usernameRun = new Run(Username)
            {
                Foreground = usernameBrush,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Create a hyperlink for the username to make it clickable
            var usernameHyperlink = new Hyperlink(usernameRun)
            {
                Foreground = usernameBrush,
                TextDecorations = null // Remove underline
            };
            
            usernameHyperlink.Click += (sender, e) =>
            {
                // Raise the UsernameClick event
                var args = new UsernameClickEventArgs(UsernameClickEvent, this, Username);
                RaiseEvent(args);
            };

            paragraph.Inlines.Add(usernameHyperlink);

            // Add colon and space
            paragraph.Inlines.Add(new Run(": "));            // Add message parts
            foreach (var part in MessageParts)
            {
                if (part.IsMention)
                {
                    // Create highlighted mention with click functionality
                    var mentionRun = new Run(part.Text)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)), // Orange
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Color.FromRgb(255, 165, 0)) { Opacity = 0.2 },
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    // Create a hyperlink for the mention to make it clickable
                    var mentionHyperlink = new Hyperlink(mentionRun)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)), // Orange
                        TextDecorations = null // Remove underline
                    };
                    
                    mentionHyperlink.Click += (sender, e) =>
                    {
                        // Raise the MentionClick event with the mentioned username (without @)
                        var args = new MentionClickEventArgs(MentionClickEvent, this, part.MentionedUsername);
                        RaiseEvent(args);
                    };

                    paragraph.Inlines.Add(mentionHyperlink);
                }
                else
                {
                    // Regular text
                    var messageBrush = IsSystemMessage ?
                        new SolidColorBrush(Color.FromRgb(156, 220, 254)) : // #9cdcfe for system messages
                        Brushes.White;

                    var textRun = new Run(part.Text)
                    {
                        Foreground = messageBrush
                    };
                    paragraph.Inlines.Add(textRun);
                }
            }

            _richTextBox.Document.Blocks.Add(paragraph);
        }
    }    // Custom event args for username clicks
    public class UsernameClickEventArgs : RoutedEventArgs
    {
        public string Username { get; }

        public UsernameClickEventArgs(RoutedEvent routedEvent, object source, string username) 
            : base(routedEvent, source)
        {
            Username = username;
        }
    }

    // Custom event args for mention clicks
    public class MentionClickEventArgs : RoutedEventArgs
    {
        public string MentionedUsername { get; }

        public MentionClickEventArgs(RoutedEvent routedEvent, object source, string mentionedUsername) 
            : base(routedEvent, source)
        {
            MentionedUsername = mentionedUsername;
        }
    }
}
