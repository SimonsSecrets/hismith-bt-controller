using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HismithController.Converters;

public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return Visibility.Collapsed;

        var enumString = value.ToString();
        var targetString = parameter.ToString();

        return string.Equals(enumString, targetString, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
