using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RadioLogger
{
    public class PeakColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double peak)
            {
                double t = Math.Max(0, Math.Min(100, peak)) / 100.0;

                if (t <= 0.8)
                {
                    double localT = t / 0.8;
                    return new SolidColorBrush(InterpolateColor(
                        System.Windows.Media.Color.FromRgb(0, 200, 83),  // Green
                        System.Windows.Media.Color.FromRgb(255, 234, 0), // Yellow
                        localT));
                }
                else
                {
                    double localT = (t - 0.8) / 0.2;
                    return new SolidColorBrush(InterpolateColor(
                        System.Windows.Media.Color.FromRgb(255, 234, 0), // Yellow
                        System.Windows.Media.Color.FromRgb(255, 0, 50),   // Red
                        localT));
                }
            }
            return System.Windows.Media.Brushes.White;
        }

        private System.Windows.Media.Color InterpolateColor(System.Windows.Media.Color c1, System.Windows.Media.Color c2, double t)
        {
            byte r = (byte)(c1.R + (c2.R - c1.R) * t);
            byte g = (byte)(c1.G + (c2.G - c1.G) * t);
            byte b = (byte)(c1.B + (c2.B - c1.B) * t);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
