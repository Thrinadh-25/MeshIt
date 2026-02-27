using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace meshIt.Converters;

/// <summary>
/// Converts a boolean to <see cref="Visibility"/>. True → Visible, False → Collapsed.
/// Pass "Inverse" as parameter to invert the logic.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter?.ToString() == "Inverse") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}
