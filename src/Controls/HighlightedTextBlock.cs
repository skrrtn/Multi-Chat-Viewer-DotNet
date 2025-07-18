using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MultiChatViewer
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
                new PropertyMetadata(false, OnIsSystemMessageChanged));

        public static readonly DependencyProperty SourcePlatformProperty =
            DependencyProperty.Register(nameof(SourcePlatform), typeof(Platform), typeof(HighlightedTextBlock),
                new PropertyMetadata(Platform.Twitch, OnSourcePlatformChanged));

        public static readonly DependencyProperty SourceChannelProperty =
            DependencyProperty.Register(nameof(SourceChannel), typeof(string), typeof(HighlightedTextBlock),
                new PropertyMetadata(string.Empty, OnSourceChannelChanged));

        public static readonly DependencyProperty ShowTimestampProperty =
            DependencyProperty.Register(nameof(ShowTimestamp), typeof(bool), typeof(HighlightedTextBlock),
                new PropertyMetadata(true, OnShowTimestampChanged));

        public static readonly DependencyProperty ShowEmotesProperty =
            DependencyProperty.Register(nameof(ShowEmotes), typeof(bool), typeof(HighlightedTextBlock),
                new PropertyMetadata(true, OnShowEmotesChanged));        // Event for username clicks
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
        }

        public Platform SourcePlatform
        {
            get => (Platform)GetValue(SourcePlatformProperty);
            set => SetValue(SourcePlatformProperty, value);
        }

        public string SourceChannel
        {
            get => (string)GetValue(SourceChannelProperty);
            set => SetValue(SourceChannelProperty, value);
        }

        public bool ShowTimestamp
        {
            get => (bool)GetValue(ShowTimestampProperty);
            set => SetValue(ShowTimestampProperty, value);
        }

        public bool ShowEmotes
        {
            get => (bool)GetValue(ShowEmotesProperty);
            set => SetValue(ShowEmotesProperty, value);
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
                    PagePadding = new Thickness(0),
                    LineHeight = double.NaN, // Auto line height for consistent spacing
                    TextAlignment = TextAlignment.Left
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
        }

        private static void OnSourcePlatformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control.UpdateContent();
            }
        }

        private static void OnSourceChannelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control.UpdateContent();
            }
        }

        private static void OnShowTimestampChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightedTextBlock control)
            {
                control.UpdateContent();
            }
        }

        private static void OnShowEmotesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
            
            double timestampWidth = 0;
            
            // Only add timestamp if ShowTimestamp is true
            if (ShowTimestamp)
            {
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

                timestampWidth = formattedText.Width;
                
                // Set hanging indent - subsequent lines will start at the beginning of the username
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
                paragraph.Inlines.Add(new Run(" "));
            }
            else
            {
                // No timestamp, reset margin and indent
                paragraph.TextIndent = 0;
                paragraph.Margin = new Thickness(0, 0, 0, 0);
            }            // Create username run with click functionality and platform-based coloring
            var usernameBrush = IsSystemMessage ? 
                new SolidColorBrush(Color.FromRgb(220, 220, 170)) : // #dcdcaa for system messages
                GetPlatformUsernameColor(SourcePlatform);

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
                // Raise the UsernameClick event with source channel and platform information
                var args = new UsernameClickEventArgs(UsernameClickEvent, this, Username, SourceChannel, SourcePlatform);
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
                        // Raise the MentionClick event with the mentioned username (without @) and source channel/platform
                        var args = new MentionClickEventArgs(MentionClickEvent, this, part.MentionedUsername, SourceChannel, SourcePlatform);
                        RaiseEvent(args);
                    };

                    paragraph.Inlines.Add(mentionHyperlink);
                }
                else if (part.IsEmote && !string.IsNullOrEmpty(part.EmoteUrl) && ShowEmotes)
                {
                    // Create emote image
                    try
                    {
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.UriSource = new Uri(part.EmoteUrl);
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        
                        // Size emotes to match the line height for consistent alignment
                        // Calculate line height based on font size and family
                        var fontFamily = _richTextBox.FontFamily;
                        var typeface = new Typeface(fontFamily, _richTextBox.FontStyle, _richTextBox.FontWeight, _richTextBox.FontStretch);
                        var formattedText = new FormattedText(
                            "Ag", // Use characters with ascenders and descenders to get full line height
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            _richTextBox.FontSize,
                            Brushes.Black,
                            VisualTreeHelper.GetDpi(this).PixelsPerDip);
                        
                        // Use the actual line height for emote sizing to ensure perfect alignment
                        var emoteSize = Math.Max(formattedText.Height * 0.9, _richTextBox.FontSize * 1.1); // Slightly smaller than line height
                        bitmapImage.DecodePixelHeight = (int)emoteSize; // Decode at display size for better performance
                        bitmapImage.EndInit();
                        
                        var emoteImage = new Image
                        {
                            Source = bitmapImage,
                            Width = emoteSize,
                            Height = emoteSize,
                            Margin = new Thickness(2, 0, 2, 0), // Small horizontal margin for spacing
                            ToolTip = part.Text, // Show emote name as tooltip
                            Stretch = Stretch.Uniform
                        };

                        // Handle image loading errors
                        bitmapImage.DownloadFailed += (sender, e) =>
                        {
                            // Image failed to load, will fall back to text
                        };

                        // Create InlineUIContainer to hold the image with proper baseline alignment
                        var container = new InlineUIContainer(emoteImage)
                        {
                            // Set baseline alignment to center the emote with the text baseline
                            BaselineAlignment = BaselineAlignment.Center
                        };
                        paragraph.Inlines.Add(container);
                    }
                    catch (Exception)
                    {
                        // If emote image fails to load, fall back to text
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

        private static SolidColorBrush GetPlatformUsernameColor(Platform platform)
        {
            return platform switch
            {
                Platform.Twitch => new SolidColorBrush(Color.FromRgb(100, 65, 165)),
                Platform.Kick => new SolidColorBrush(Color.FromRgb(83, 255, 26)),
                _ => new SolidColorBrush(Color.FromRgb(86, 156, 214))                  // #569cd6 - Default blue fallback
            };
        }
    }    // Custom event args for username clicks
    public class UsernameClickEventArgs(RoutedEvent routedEvent, object source, string username, string sourceChannel = null, Platform sourcePlatform = Platform.Twitch) : RoutedEventArgs(routedEvent, source)
    {
        public string Username { get; } = username;
        public string SourceChannel { get; } = sourceChannel ?? string.Empty;
        public Platform SourcePlatform { get; } = sourcePlatform;
    }    // Custom event args for mention clicks
    public class MentionClickEventArgs(RoutedEvent routedEvent, object source, string mentionedUsername, string sourceChannel = null, Platform sourcePlatform = Platform.Twitch) : RoutedEventArgs(routedEvent, source)
    {
        public string MentionedUsername { get; } = mentionedUsername;
        public string SourceChannel { get; } = sourceChannel ?? string.Empty;
        public Platform SourcePlatform { get; } = sourcePlatform;
    }
}
