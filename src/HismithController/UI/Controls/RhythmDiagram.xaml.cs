using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HismithController.Controls;

// Abstract rhythm diagram: one full sine period per stroke cycle (Ratio beats),
// a faint centre baseline, and beat dots accented on each stroke-cycle start.
// Direct port of the React RhythmDiagram (design/modes.jsx). Drawn over a fixed
// 96×32 surface and scaled by the XAML Viewbox. Sine/dots colour follows the
// control's Foreground (set/animated by the parent tile) via bindings.
public partial class RhythmDiagram : UserControl
{
    // Geometry constants mirror the JS source so the shapes match the design exactly.
    private const double W = 96, H = 32;
    private const double Left = 6, Right = W - 6;
    private const int Beats = 8;
    private const int Samples = 72;
    private const double Amp = 6.5;
    private const double MidY = H / 2;

    public static readonly DependencyProperty RatioProperty =
        DependencyProperty.Register(
            nameof(Ratio), typeof(int), typeof(RhythmDiagram),
            new PropertyMetadata(1, OnRatioChanged));

    public int Ratio
    {
        get => (int)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public RhythmDiagram()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
    }

    private static void OnRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RhythmDiagram)d).Rebuild();

    private void Rebuild()
    {
        if (Surface is null) return;
        Surface.Children.Clear();

        int ratio = Math.Max(1, Ratio);
        double step = (Right - Left) / Beats;   // distance between adjacent beats
        double period = ratio * step;            // one full sine period = one stroke cycle

        // Centre baseline.
        var baseline = new Line
        {
            X1 = Left, Y1 = MidY, X2 = Right, Y2 = MidY,
            StrokeThickness = 0.8,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.2
        };
        BindForeground(baseline, Shape.StrokeProperty);
        Surface.Children.Add(baseline);

        // The stroke-cycle sine.
        var sine = new Polyline
        {
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        for (int s = 0; s <= Samples; s++)
        {
            double x = Left + (Right - Left) * ((double)s / Samples);
            double y = MidY - Amp * Math.Sin(2 * Math.PI * (x - Left) / period);
            sine.Points.Add(new Point(x, y));
        }
        BindForeground(sine, Shape.StrokeProperty);
        Surface.Children.Add(sine);

        // Beat dots on the baseline; accent on each stroke-cycle start (every ratio-th).
        for (int i = 0; i <= Beats; i++)
        {
            double x = Left + i * step;
            bool isAccent = i % ratio == 0;
            double r = isAccent ? 1.7 : 1.0;
            var dot = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Opacity = isAccent ? 0.95 : 0.4
            };
            Canvas.SetLeft(dot, x - r);
            Canvas.SetTop(dot, MidY - r);
            BindForeground(dot, Shape.FillProperty);
            Surface.Children.Add(dot);
        }
    }

    // Shapes don't inherit Foreground, so bind Stroke/Fill to this control's
    // Foreground — which the parent tile sets (text-dim normally, rose when active).
    private void BindForeground(Shape shape, DependencyProperty target)
        => shape.SetBinding(target, new Binding(nameof(Foreground)) { Source = this });
}
