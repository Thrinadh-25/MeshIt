using System.Globalization;
using System.Windows.Data;

namespace meshIt.Converters;

/// <summary>
/// Converts an RSSI integer (dBm) to a human-readable signal bar string.
/// </summary>
public class SignalStrengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int rssi) return "📶";

        return rssi switch
        {
            > -50 => "📶 ●●●●●",   // Excellent
            > -60 => "📶 ●●●●○",   // Very Good
            > -70 => "📶 ●●●○○",   // Good
            > -80 => "📶 ●●○○○",   // Fair
            > -90 => "📶 ●○○○○",   // Weak
            _     => "📶 ○○○○○"    // Very Weak
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
