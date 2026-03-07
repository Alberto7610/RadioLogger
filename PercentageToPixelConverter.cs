using System;
using System.Globalization;
using System.Windows.Data;

namespace RadioLogger
{
    public class PercentageToPixelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && 
                values[0] is double percentage && 
                values[1] is double width)
            {
                // Ensure percentage is 0-100
                double p = Math.Max(0, Math.Min(100, percentage));
                
                // Calculate pixel position
                // Subtract a bit so the line stays inside the bar at 100%
                double pos = (p / 100.0) * width;
                return Math.Max(0, pos - 2); 
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
