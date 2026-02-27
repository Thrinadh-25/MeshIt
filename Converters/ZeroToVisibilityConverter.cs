using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace meshIt.Converters;

/// <summary>
/// Converts an integer (e.g. collection Count) to Visibility.
/// Returns Visible when value is 0, Collapsed otherwise.
/// Useful for showing "empty state" text.
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
