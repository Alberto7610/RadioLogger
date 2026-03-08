using System;
using System.Globalization;
using System.Windows.Data;

namespace RadioLogger
{
    public class InversePercentageToPixelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && 
                values[0] is double percentage && 
                values[1] is double width)
            {
                // Invert logic: Calculate the "remaining" space
                double remaining = 100.0 - Math.Max(0, Math.Min(100, percentage));
                
                return (remaining / 100.0) * width;
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
