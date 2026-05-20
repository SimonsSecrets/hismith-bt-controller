using System.Globalization;
using System.Windows.Data;

namespace HismithController.Converters;

public sealed class StringEqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return parameter?.ToString() ?? string.Empty;
        return Binding.DoNothing;
    }
}
