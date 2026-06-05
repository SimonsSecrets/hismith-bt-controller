using System.Globalization;
using System.IO;

namespace HismithController.BeatDetection;

// Writes an OSF capture to a UTF-8 text file: a header block, one `OSF` line per FFT hop, and
// one `CYCLE` line per 500 ms tempo-timer firing. The format is documented in IOsfCaptureSink
// and consumed by tools/OsfReplay.
//
// Threading: RecordHop (audio thread) only appends to an in-memory buffer under a short, almost
// always uncontended lock — no disk I/O on the audio thread. RecordCycle (tempo-timer thread)
// swaps the buffer out under that same lock, then writes the drained hops and the cycle line to
// disk outside the lock, so the audio thread is never blocked on file I/O.
public sealed class OsfFileCaptureSink : IOsfCaptureSink
{
    private readonly record struct HopRecord(long Index, double Flux, bool Running);

    private readonly StreamWriter _writer;
    private readonly object _bufferLock = new();

    // Active buffer (appended by the audio thread) and a spare swapped in on each drain so the
    // audio thread never waits on disk I/O — the lock is held only for the O(1) reference swap.
    private List<HopRecord> _buffer = new(256);
    private List<HopRecord> _spare  = new(256);

    private bool _disposed;

    public OsfFileCaptureSink(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // AutoFlush off: we flush explicitly once per cycle so a crash loses at most ~500 ms.
        _writer = new StreamWriter(path, append: false) { AutoFlush = false };
    }

    public void WriteHeader(OsfCaptureHeader h)
    {
        var c = CultureInfo.InvariantCulture;
        lock (_bufferLock)
        {
            _writer.WriteLine("# HismithController OSF capture v1");
            _writer.WriteLine($"hopMs={h.HopMs.ToString(c)}");
            _writer.WriteLine($"sampleRate={h.SampleRate.ToString(c)}");
            _writer.WriteLine($"osfLen={h.OsfLen.ToString(c)}");
            _writer.WriteLine($"osfWindowSeconds={h.OsfWindowSeconds.ToString(c)}");
            _writer.WriteLine($"minBpm={h.MinBpm.ToString(c)}");
            _writer.WriteLine($"maxBpm={h.MaxBpm.ToString(c)}");
            _writer.WriteLine($"preferredCenter={h.PreferredCenter.ToString(c)}");
            _writer.WriteLine($"preferredSigma={h.PreferredSigma.ToString(c)}");
            _writer.WriteLine($"recencyTauSeconds={h.RecencyTauSeconds.ToString(c)}");
            _writer.WriteLine($"sparsityMetronomeMin={h.SparsityMetronomeMin.ToString(c)}");
            _writer.WriteLine($"sparsityDenseMax={h.SparsityDenseMax.ToString(c)}");
            _writer.WriteLine($"tempoUpJumpFactor={h.TempoUpJumpFactor.ToString(c)}");
            _writer.WriteLine($"tempoUpJumpMinBpm={h.TempoUpJumpMinBpm.ToString(c)}");
            _writer.WriteLine($"tempoUpConfirmCycles={h.TempoUpConfirmCycles.ToString(c)}");
            _writer.WriteLine($"tempoConfirmToleranceBpm={h.TempoConfirmToleranceBpm.ToString(c)}");
            _writer.WriteLine($"startedUtc={DateTimeOffset.UtcNow.ToString("O", c)}");
            _writer.WriteLine("# hopIndex flux audioRunning");
            _writer.Flush();
        }
    }

    // Monotonic hop counter (never reset by a buffer swap), advanced only by the audio thread
    // under _bufferLock via RecordHop, so every OSF line carries a stable absolute index.
    private long _nextIndex;

    public void RecordHop(double flux, bool audioRunning)
    {
        lock (_bufferLock)
            _buffer.Add(new HopRecord(_nextIndex++, flux, audioRunning));
    }

    public void RecordCycle(long headIndex, int rawBpm, float confidence, double sparsity, int smoothedBpm)
    {
        List<HopRecord> drained;
        lock (_bufferLock)
        {
            drained = _buffer;
            _buffer = _spare;   // hand the audio thread an empty buffer; reuse the old one next time
            _spare  = drained;
        }

        var c = CultureInfo.InvariantCulture;
        foreach (var hop in drained)
            _writer.WriteLine($"OSF {hop.Index.ToString(c)} {hop.Flux.ToString(c)} {(hop.Running ? 1 : 0)}");
        drained.Clear();

        _writer.WriteLine(
            $"CYCLE {headIndex.ToString(c)} {rawBpm.ToString(c)} {confidence.ToString(c)} " +
            $"{sparsity.ToString(c)} {smoothedBpm.ToString(c)}");
        _writer.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Drain whatever the audio thread buffered after the last cycle so no hops are lost.
        List<HopRecord> drained;
        lock (_bufferLock)
        {
            drained = _buffer;
            _buffer = _spare;
            _spare  = drained;
        }
        var c = CultureInfo.InvariantCulture;
        foreach (var hop in drained)
            _writer.WriteLine($"OSF {hop.Index.ToString(c)} {hop.Flux.ToString(c)} {(hop.Running ? 1 : 0)}");

        _writer.Flush();
        _writer.Dispose();
    }
}
