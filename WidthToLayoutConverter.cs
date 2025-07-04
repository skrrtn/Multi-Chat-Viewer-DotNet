using System;
using System.Globalization;
using System.Windows.Data;

namespace TwitchChatViewer
{
    /// <summary>
    /// Converter that returns true when window width is narrow (triggers vertical layout)
    /// </summary>
    public class WidthToLayoutConverter : IValueConverter
    {
        public static readonly WidthToLayoutConverter Instance = new();
        
        // Threshold width below which we switch to vertical layout
        private const double NarrowWidthThreshold = 600.0;
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                // Return true if window is narrow (should use vertical layout)
                return width < NarrowWidthThreshold;
            }
            
            return false; // Default to horizontal layout
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ConvertBack is not supported");
        }
    }
}
