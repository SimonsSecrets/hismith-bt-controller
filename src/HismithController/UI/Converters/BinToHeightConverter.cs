using System.Globalization;
using System.Windows.Data;

namespace HismithController.Converters;

// Scales a normalised linear spectrum bin magnitude to a pixel height for the
// visualizer bars. Each bar fills the full container height and is centered
// vertically (VerticalAlignment="Center"), so the height drives growth from the
// middle outward — matching the design's transform-origin:50%50% / scaleY pattern.
//
// WHY logarithmic, not linear:
//   Individual FFT bins for broadband music at a typical listening level sit at
//   −60 to −100 dB on a linear magnitude scale. A linear mapping compresses the
//   entire audible dynamic range into the bottom 1–2 pixels. Log scaling maps
//   equal perceived-loudness steps to equal visual steps.
//
// Range calibration — maps [DbFloor, DbCeiling] → [0 %, 100 %]:
//   DbCeiling = −20 dB (0.1 linear):  loud music peaks → full bar
//   DbFloor   = −130 dB (3.2e-7):     below this is invisible
//   This 110 dB window is calibrated so that real WASAPI loopback signals in the
//   −80 to −100 dBFS range (seen when the loopback device is quieter than the
//   playback device) still produce clearly visible bars:
//
//     −130 dB (3.2e-7):   0 %  — floor
//     −100 dB (1.0e-5):  27 %  — quiet high-frequency transients (~38 px)
//      −86 dB (5.0e-5):  40 %  — dominant bins at quiet capture levels
//      −60 dB (1.0e-3):  64 %  — moderate signal
//      −40 dB (1.0e-2):  82 %  — typical music content
//      −20 dB (1.0e-1): 100 %  — loud / ceiling (clamped)
//
// WHY Height binding, not ScaleTransform.ScaleY:
//   ScaleTransform (a Freezable) receives its DataContext after template
//   adoption, so {Binding} on ScaleY silently stays at zero in DataTemplates.
//   FrameworkElement.Height is always reliable from a DataTemplate binding.
public sealed class BinToHeightConverter : IValueConverter
{
    // Must stay in sync with the container Height="140" in SoundModeView.xaml.
    private const double BarMaxHeight = 140.0;

    // dB window: [DbFloor, DbCeiling] maps linearly to [0, 1].
    private const double DbFloor   = -130.0;
    private const double DbCeiling =  -20.0;
    private const double DbRange   = DbCeiling - DbFloor; // 110.0

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d || d <= 0.0) return 0.0;

        double db         = 20.0 * Math.Log10(d);
        double normalized = (db - DbFloor) / DbRange; // (db + 130) / 110
        return Math.Clamp(normalized, 0.0, 1.0) * BarMaxHeight;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
