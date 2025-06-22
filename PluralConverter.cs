using System;
using System.Globalization;
using System.Windows.Data;

namespace TwitchChatViewer
{
    public class PluralConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count == 1 ? "" : "s";
            }
            return "s"; // Default to plural
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
