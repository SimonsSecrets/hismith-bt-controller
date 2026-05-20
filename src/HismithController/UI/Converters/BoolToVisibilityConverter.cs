using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HismithController.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value switch
        {
            bool b => b,
            int i => i > 0,
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => value is not null
        };
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
