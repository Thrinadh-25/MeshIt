using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using meshIt.Models;

namespace meshIt.Converters;

/// <summary>
/// Converts a <see cref="PeerStatus"/> to a SolidColorBrush for status indicators.
///   Online    → Green
///   Connecting → Yellow/Amber
///   Offline   → Gray
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OnlineBrush = new(Color.FromRgb(76, 175, 80));     // #4CAF50
    private static readonly SolidColorBrush ConnectingBrush = new(Color.FromRgb(255, 193, 7));  // #FFC107
    private static readonly SolidColorBrush OfflineBrush = new(Color.FromRgb(158, 158, 158));   // #9E9E9E

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PeerStatus status)
        {
            return status switch
            {
                PeerStatus.Online => OnlineBrush,
                PeerStatus.Connecting => ConnectingBrush,
                PeerStatus.Offline => OfflineBrush,
                _ => OfflineBrush
            };
        }
        return OfflineBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
