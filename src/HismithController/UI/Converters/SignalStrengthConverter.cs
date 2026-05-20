using System.Globalization;
using System.Windows.Data;

namespace HismithController.Converters;

public sealed class SignalStrengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var strength = value is int s ? s : 0;
        var barIndex = int.TryParse(parameter?.ToString(), out var idx) ? idx : 1;

        return strength >= barIndex ? 1.0 : 0.25;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
