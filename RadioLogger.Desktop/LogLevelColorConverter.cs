using System;
using System.Globalization;
using System.Windows.Data;

namespace RadioLogger
{
    /// <summary>
    /// Extracts the log level tag ([INF], [WRN], [ERR], [FTL], [DBG]) from a log line string.
    /// Used by the Logs tab to colorize lines by severity.
    /// </summary>
    public class LogLevelColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string line)
            {
                if (line.Contains("[ERR]")) return "ERR";
                if (line.Contains("[FTL]")) return "FTL";
                if (line.Contains("[WRN]")) return "WRN";
                if (line.Contains("[INF]")) return "INF";
                if (line.Contains("[DBG]")) return "DBG";
            }
            return "INF";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
