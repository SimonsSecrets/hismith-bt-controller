namespace HismithController.BeatDetection;

// Header metadata written once at the top of an OSF capture. Carries every parameter the
// estimator/smoother were constructed with so a replay can reconstruct them identically and
// reproduce the live result deterministically. See OsfFileCaptureSink and tools/OsfReplay.
public sealed record OsfCaptureHeader(
    double HopMs,
    int    SampleRate,
    int    OsfLen,
    double OsfWindowSeconds,
    double MinBpm,
    double MaxBpm,
    double PreferredCenter,
    double PreferredSigma,
    double RecencyTauSeconds,
    double SparsityMetronomeMin,
    double SparsityDenseMax,
    double TempoUpJumpFactor,
    int    TempoUpJumpMinBpm,
    int    TempoUpConfirmCycles,
    int    TempoConfirmToleranceBpm);

// Diagnostic sink for the sound-mode tempo pipeline (OpenPoints.md item 2). Records the
// onset-strength envelope (one flux value per FFT hop) plus the per-cycle tempo-estimator
// output to a replayable text file, so a real "sudden tempo jump" can be captured and
// reproduced offline. The NullOsfCaptureSink is used when capture is off so SpectralFluxBeatDetector
// needs no null-checks and the audio thread pays only a single virtual no-op call.
//
// Threading: RecordHop is called on the audio thread (inside the detector's _osfLock) and must
// not block or touch disk; RecordCycle is called on the tempo-timer thread and is where file
// I/O happens. WriteHeader is called once from the detector constructor.
public interface IOsfCaptureSink : IDisposable
{
    void WriteHeader(OsfCaptureHeader header);

    // Audio thread. Buffer only — no disk I/O (the audio callback has a <5 ms/hop budget).
    void RecordHop(double flux, bool audioRunning);

    // Tempo-timer thread. headIndex is the total hops appended so far (the head of the OSF
    // window); rawBpm is the estimate BEFORE smoothing, smoothedBpm the published value.
    void RecordCycle(long headIndex, int rawBpm, float confidence, double sparsity, int smoothedBpm);
}

// No-op sink used whenever capture is disabled (no --capture-osf flag).
public sealed class NullOsfCaptureSink : IOsfCaptureSink
{
    public void WriteHeader(OsfCaptureHeader header) { }
    public void RecordHop(double flux, bool audioRunning) { }
    public void RecordCycle(long headIndex, int rawBpm, float confidence, double sparsity, int smoothedBpm) { }
    public void Dispose() { }
}
