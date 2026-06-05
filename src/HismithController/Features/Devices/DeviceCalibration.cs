namespace HismithController.Devices;

// Maps between a target thrust tempo (BPM) and the device speed percentage that
// produces it. On real hardware this relationship is NOT linear (OpenPoints §3): the
// Pro 1 reaches 136 BPM at 50 % speed, not the 120 a linear scale assumes. The
// calibration stores empirically-measured (percent, bpm) points and interpolates
// piecewise-linearly in both directions, so the rest of the app can reason purely in
// BPM and still emit the speed byte that actually yields that tempo on this model.
public sealed class DeviceCalibration
{
    // Ascending in both fields; first point is (0, 0), last is (100, MaxBpm). Strict
    // monotonicity in BPM is required so the Bpm→Percent inverse is single-valued.
    private readonly IReadOnlyList<(int Percent, int Bpm)> _points;

    public DeviceCalibration(IReadOnlyList<(int Percent, int Bpm)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
            throw new ArgumentException("Calibration needs at least two points.", nameof(points));

        for (var i = 1; i < points.Count; i++)
        {
            // Percent must strictly increase (so forward lerp never divides by zero);
            // BPM must be non-decreasing (so the inverse stays single-valued).
            if (points[i].Percent <= points[i - 1].Percent || points[i].Bpm < points[i - 1].Bpm)
                throw new ArgumentException(
                    "Calibration points must strictly increase in percent and not decrease in BPM.",
                    nameof(points));
        }

        _points = points;
    }

    // Tempo the device reaches at 100 % speed — the top of the curve. Drives the
    // app-wide BPM scale (slider range, cap, presets), so it must equal the real ceiling.
    public int MaxBpm => _points[^1].Bpm;

    // Forward: speed percent → the tempo it produces on this device.
    public int PercentToBpm(int percent)
    {
        var p = Math.Clamp(percent, _points[0].Percent, _points[^1].Percent);
        for (var i = 1; i < _points.Count; i++)
        {
            var (p0, b0) = _points[i - 1];
            var (p1, b1) = _points[i];
            if (p <= p1)
                return (int)Math.Round(Lerp(p0, b0, p1, b1, p));
        }
        return _points[^1].Bpm; // unreachable: the clamp guarantees a segment match
    }

    // Inverse: target tempo → the speed percent that yields it. This is what the device
    // send path uses (a 120-BPM request becomes ~43 % on the Pro 1, not the linear 50 %).
    public int BpmToPercent(int bpm)
    {
        var b = Math.Clamp(bpm, _points[0].Bpm, _points[^1].Bpm);
        for (var i = 1; i < _points.Count; i++)
        {
            var (p0, b0) = _points[i - 1];
            var (p1, b1) = _points[i];
            if (b <= b1)
                // Flat BPM segment (b0 == b1): the lower percent already reaches this
                // tempo, so pick it rather than dividing by zero in the lerp.
                return b1 == b0 ? p0 : (int)Math.Round(Lerp(b0, p0, b1, p1, b));
        }
        return _points[^1].Percent; // unreachable: the clamp guarantees a segment match
    }

    // Linear interpolation of y at x across the segment (x0,y0)→(x1,y1).
    private static double Lerp(int x0, int y0, int x1, int y1, int x) =>
        y0 + (y1 - y0) * (double)(x - x0) / (x1 - x0);

    // Straight 0→maxBpm line for models without measured data. The inverse then matches
    // the old linear bpm*100/maxBpm mapping exactly, so unmeasured devices are unchanged.
    public static DeviceCalibration Linear(int maxBpm)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBpm, 1);
        return new([(0, 0), (100, maxBpm)]);
    }
}
