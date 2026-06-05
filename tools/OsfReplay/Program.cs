using System.Globalization;
using HismithController.BeatDetection;

// Offline replay of an OSF capture produced by --capture-osf (OpenPoints.md item 2).
// Reconstructs each tempo cycle's OSF window from the recorded flux stream, re-runs the REAL
// AutocorrelationTempoEstimator + TempoSmoother, and prints a per-cycle table comparing the
// recorded live result against the recomputed one. A matching raw BPM confirms a faithful,
// deterministic replay; the table then makes the tempo spike and the smoother's response
// obvious, and CLI overrides let you A/B parameter changes against the very same capture.
//
// Usage:
//   dotnet run --project tools/OsfReplay -- <capture.txt>
//                 [--tau <s>] [--osf-window <s>]
//                 [--jump-factor <f>] [--jump-min <bpm>]
//                 [--confirm-cycles <n>] [--confirm-tol <bpm>]

var ci = CultureInfo.InvariantCulture;
// Use invariant formatting for the printed table too (dots, not the OS locale's comma) so the
// output reads consistently regardless of machine locale.
CultureInfo.CurrentCulture = ci;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: OsfReplay <capture.txt> [--tau s] [--osf-window s] " +
                      "[--jump-factor f] [--jump-min bpm] [--confirm-cycles n] [--confirm-tol bpm]");
    return args.Length == 0 ? 1 : 0;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Capture file not found: {path}");
    return 1;
}

// ── Parse capture ────────────────────────────────────────────────────────────
var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var flux = new List<double>(8192);   // index == hopIndex (contiguous from 0)
var cycles = new List<Cycle>();

foreach (var raw in File.ReadLines(path))
{
    var line = raw.Trim();
    if (line.Length == 0 || line[0] == '#') continue;

    if (line.StartsWith("OSF ", StringComparison.Ordinal))
    {
        // OSF <hopIndex> <flux> <audioRunning>
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        long idx = long.Parse(p[1], ci);
        double f = double.Parse(p[2], ci);
        if (idx != flux.Count)
        {
            Console.Error.WriteLine(
                $"Non-contiguous OSF stream: expected hopIndex {flux.Count}, got {idx}. " +
                "Replay requires a complete stream.");
            return 1;
        }
        flux.Add(f);
    }
    else if (line.StartsWith("CYCLE ", StringComparison.Ordinal))
    {
        // CYCLE <head> <rawBpm> <confidence> <sparsity> <smoothedBpm>
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        cycles.Add(new Cycle(
            Head:     long.Parse(p[1], ci),
            RawBpm:   int.Parse(p[2], ci),
            Conf:     float.Parse(p[3], ci),
            Sparsity: double.Parse(p[4], ci),
            Smoothed: int.Parse(p[5], ci)));
    }
    else if (line.Contains('='))
    {
        int eq = line.IndexOf('=');
        header[line[..eq]] = line[(eq + 1)..];
    }
}

double H(string key, double fallback) =>
    header.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Any, ci, out var d) ? d : fallback;
int Hi(string key, int fallback) =>
    header.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Any, ci, out var i) ? i : fallback;

// ── CLI overrides (default to the captured header values) ──────────────────────
double Opt(string name, double fallback)
{
    int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Any, ci, out var d)
        ? d : fallback;
}

double hopMs  = H("hopMs", 256.0 * 1000.0 / 44100.0);
int    osfLen = Hi("osfLen", (int)Math.Round(H("osfWindowSeconds", 6.0) * 44100 / 256));

double tau            = Opt("--tau",            H("recencyTauSeconds", 2.5));
double jumpFactor     = Opt("--jump-factor",    H("tempoUpJumpFactor", 1.25));
int    jumpMin        = (int)Opt("--jump-min",  Hi("tempoUpJumpMinBpm", 20));
int    confirmCycles  = (int)Opt("--confirm-cycles", Hi("tempoUpConfirmCycles", 3));
int    confirmTol     = (int)Opt("--confirm-tol",    Hi("tempoConfirmToleranceBpm", 8));
// Corroboration threshold for immediate adoption of a large up-jump. Default 1.0 keeps the
// pure time-based behaviour for captures recorded before this field existed; pass
// --corroboration <v> (or recapture) to exercise the early-adopt path.
double corroborationMin = Opt("--corroboration", H("tempoCorroborationMin", 1.0));

// --osf-window override changes how much history each cycle's snapshot spans (in samples).
double osfWindowOverride = Opt("--osf-window", -1.0);
if (osfWindowOverride > 0.0)
    osfLen = (int)Math.Round(osfWindowOverride * 1000.0 / hopMs);

var estimator = new AutocorrelationTempoEstimator(
    minBpm:            H("minBpm", 15.0),
    maxBpm:            Opt("--max-bpm", H("maxBpm", 240.0)),
    preferredCenter:   H("preferredCenter", 120.0),
    preferredSigma:    H("preferredSigma", 0.5),
    recencyTauSeconds: tau);

var smoother = new TempoSmoother(
    jumpUpFactor:           jumpFactor,
    jumpUpMinBpm:           jumpMin,
    confirmCycles:          confirmCycles,
    confirmToleranceBpm:    confirmTol,
    corroborationThreshold: corroborationMin);

// ── Replay ─────────────────────────────────────────────────────────────────────
Console.WriteLine($"Capture : {path}");
Console.WriteLine($"hopMs={hopMs.ToString("0.####", ci)}  osfLen={osfLen}  hops={flux.Count}  cycles={cycles.Count}");
Console.WriteLine($"estimator: tau={tau.ToString(ci)}  fold=false");
Console.WriteLine($"smoother : jumpFactor={jumpFactor.ToString(ci)} jumpMin={jumpMin} " +
                  $"confirmCycles={confirmCycles} confirmTol={confirmTol} " +
                  $"corroboration={corroborationMin.ToString(ci)}");
Console.WriteLine();
Console.WriteLine("  #     head    t(s)  recRaw  rplRaw    d  recSm  rplSm  conf  hsup  spars  flag");
Console.WriteLine("  ----  ------  -----  ------  ------  ---  -----  -----  ----  ----  -----  ----");

var span = flux.ToArray();
int n = 0, mismatches = 0;
foreach (var cyc in cycles)
{
    int head  = (int)Math.Min(cyc.Head, span.Length);
    int start = Math.Max(0, head - osfLen);
    var window = span.AsSpan(start, head - start);

    var est = estimator.Analyze(window, hopMs, fold: false);
    int rplSmoothed = smoother.Update(est.Bpm, est.HarmonicSupport);

    int dRaw = est.Bpm - cyc.RawBpm;
    bool mismatch = dRaw != 0;
    if (mismatch) mismatches++;

    // Flag a raw spike: the live raw estimate jumped far above the published (smoothed) value.
    bool spike = cyc.RawBpm > cyc.Smoothed + 20 && cyc.RawBpm > cyc.Smoothed * 1.25;
    string flag = spike ? "SPIKE" : (mismatch ? "diff" : "");

    double t = cyc.Head * hopMs / 1000.0;
    Console.WriteLine(
        $"  {n,4}  {cyc.Head,6}  {t,5:0.0}  {cyc.RawBpm,6}  {est.Bpm,6}  {dRaw,3}  " +
        $"{cyc.Smoothed,5}  {rplSmoothed,5}  {est.Confidence,4:0.00}  {est.HarmonicSupport,4:0.00}  {cyc.Sparsity,5:0.00}  {flag}");
    n++;
}

Console.WriteLine();
Console.WriteLine(mismatches == 0
    ? $"Replay reproduced all {cycles.Count} cycles exactly (raw BPM matched)."
    : $"WARNING: {mismatches}/{cycles.Count} cycles differed from the recorded raw BPM " +
      "(parameter override active, or stream/version mismatch).");
return 0;

// One recorded tempo-timer firing: the head index into the flux stream plus the live result.
readonly record struct Cycle(long Head, int RawBpm, float Conf, double Sparsity, int Smoothed);
