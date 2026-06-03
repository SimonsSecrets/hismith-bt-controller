using System.Globalization;
using System.Windows.Data;

namespace HismithController.Converters;

/// <summary>
/// Maps the current connect step to one of "Inactive" / "Active" / "Done" for a given
/// target step (the ConverterParameter). Unlike <see cref="StepActiveConverter"/> (which
/// returns >= as a bool), this yields mutually exclusive states so the XAML can style
/// the active (rose) and done (sage + check) stepper visuals without overlapping triggers.
/// </summary>
public sealed class StepStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int currentStep && parameter is string paramStr && int.TryParse(paramStr, out int targetStep))
        {
            if (currentStep > targetStep) return "Done";
            if (currentStep == targetStep) return "Active";
        }
        return "Inactive";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
