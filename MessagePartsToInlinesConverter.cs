using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace TwitchChatViewer
{
    public class MessagePartsToInlinesConverter : IMultiValueConverter
    {        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 1 && values[0] is List<MessagePart> messageParts)
            {
                var inlines = new List<Inline>();
                
                // Check if font size is provided as second parameter
                double fontSize = values.Length >= 2 && values[1] is double providedFontSize ? providedFontSize : 12.0;

                foreach (var part in messageParts)
                {
                    if (part.IsMention)
                    {
                        // Create a highlighted run for mentions
                        var mentionRun = new Run(part.Text)
                        {
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)), // Orange color
                            FontWeight = FontWeights.Bold,
                            Background = new SolidColorBrush(Color.FromRgb(255, 165, 0)) { Opacity = 0.2 },
                            FontSize = fontSize
                        };
                        
                        inlines.Add(mentionRun);
                    }
                    else
                    {
                        // Regular text
                        var textRun = new Run(part.Text)
                        {
                            Foreground = Brushes.White,
                            FontSize = fontSize
                        };
                        inlines.Add(textRun);
                    }
                }

                return inlines;
            }            // Fallback: if no parsed parts, return the original message as a single run
            if (parameter is string fallbackMessage)
            {
                double fontSize = values.Length >= 2 && values[1] is double providedFontSize ? providedFontSize : 12.0;
                return new List<Inline> { new Run(fallbackMessage) { Foreground = Brushes.White, FontSize = fontSize } };
            }

            return new List<Inline>();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
