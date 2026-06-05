# Sound Mode — Implementation Reference

This document explains **how Sound Mode works and *why* it is built the way it is** —
the algorithms, the trade-offs, and the decisions (several of which were reversed
after testing against real input). It is the living reference for the Sound Mode
audio → beat → BPM pipeline. **Keep it in sync** when changing anything in
`Features/Audio/` or `Features/BeatDetection/`.

> `SoundModePlan.md` is the original build plan and is partly historical; for the
> beat-detection algorithm, **this document supersedes it**.

---

## Glossary

Plain-language definitions of the audio/DSP terms used throughout this document.

- **Sample / sample rate.** Digital audio is a long list of numbers ("samples"), each
  the loudness of the waveform at one instant. The **sample rate** is how many of those
  per second; we normalize everything to **44,100 samples/second (44.1 kHz)**. So
  "512 samples" ≈ 11.6 ms of sound, and "256 samples" ≈ 5.8 ms.

- **FFT (Fast Fourier Transform).** An algorithm that takes a short chunk of the waveform
  (here 512 samples) and tells you **how much energy it contains at each frequency** —
  i.e. it converts "loudness over time" into "loudness per pitch". We use it to see how
  much *low-frequency* (bass/kick) energy is present right now.
  - **Bin.** The FFT splits the frequency range into equal slices called bins. With a
    512-point FFT at 44.1 kHz each bin is ≈ 86 Hz wide, so **bins 1–3 cover ≈ 86–258 Hz**
    — the kick/bass band we care about. `|X[k]|` is the energy magnitude in bin *k*.

- **Hop.** Rather than analyzing one isolated 512-sample window, we slide the window
  forward in small steps and run a new FFT each step. Each step is a **hop** of 256
  samples (~5.8 ms). Using a hop smaller than the window (here 50 % overlap) means we get
  a fresh frequency snapshot roughly every 5.8 ms instead of every 11.6 ms — better time
  resolution for catching beats. "Per hop" = "each ~5.8 ms analysis step".

- **Onset / onset detection.** An **onset** is the start of a new sound — a drum hit, a
  kick, a metronome click. **Onset detection** is finding those moments. We detect them by
  looking for sudden *increases* in low-frequency energy from one hop to the next.

- **Spectral flux.** The measure we use to detect onsets: for each FFT bin, how much its
  energy **rose** since the previous hop, summed over the bins of interest
  (`Σ max(0, |X[k]| − |X_prev[k]|)`). "Spectral" = across the frequency spectrum; "flux" =
  amount of change. It spikes at an onset (energy jumps) and sits near zero during steady
  or decaying sound. The `max(0, …)` keeps only increases ("half-wave rectified").

- **OSF (onset-strength envelope) ring.** The **OSF** is the running stream of spectral-flux
  values, one per hop — essentially a "beat energy over time" curve. We keep the most
  recent ~6 seconds of it in a **ring buffer** (a fixed-size array that overwrites its
  oldest entry when full, so it always holds the latest N values without growing). The
  tempo estimator looks for repeating patterns in this curve. ("OSF ring" = "the recent
  history of beat-energy values".)

- **RMS / RMS window.** **RMS (root-mean-square)** is a standard way to measure the average
  loudness of a chunk of audio: square every sample, average them, take the square root.
  The **RMS window** here is the most recent 500 ms; if its RMS is very low, the system is
  effectively silent. Used only for silence detection, separate from beat detection.

- **dBFS (decibels relative to full scale).** A loudness scale for digital audio where
  **0 dBFS is the loudest possible signal** and quieter sounds are negative numbers. Our
  silence threshold is **≈ −80 dBFS**, i.e. extremely quiet — about a ten-thousandth of
  full volume — which we treat as "no signal".

- **Autocorrelation.** A way to find repetition in a signal: slide a copy of the OSF curve
  against itself by various time offsets ("lags") and see which offset makes it line up
  best. The lag that matches best is the beat period, which converts directly to BPM. See
  §5 for why this is more robust than timing the gaps between detected onsets.

- **BPM (beats per minute) / IBI (inter-beat interval).** **BPM** is the tempo. **IBI** is
  the time between two consecutive beats; `BPM = 60000 / IBI(ms)`. The retired approach
  computed BPM straight from measured IBIs; the current one derives it from autocorrelation.

---

## 1. Pipeline overview

```
WASAPI loopback ─► AudioFrame (mono, 44.1 kHz) ─► SpectralFluxBeatDetector ─► SoundModeViewModel ─► UI / device
                                                   │
                                                   ├─ per hop (audio thread):  spectral flux ─► onset peak-pick ─► BeatDetected (liveness)
                                                   │                                          └─► append to OSF ring
                                                   └─ every 500 ms (timer thread): autocorrelation over OSF ─► TempoSmoother ─► CurrentBpm / Confidence
```

Two outputs come out of the detector and they are deliberately **decoupled**:

| Output | Source | Used for |
|---|---|---|
| `BeatDetected` events | discrete spectral-flux onsets | the "audio is live" signal (`HasAudio`) |
| `CurrentBpm` / `Confidence` | autocorrelation of the onset-strength envelope | the displayed tempo + device speed |

The single most important design decision is that **the BPM number is not computed
from the beat events.** Section 5 explains why.

---

## 2. Threading model

Three threads touch this subsystem; the split exists to keep the realtime audio
callback cheap.

- **Audio thread** (`IAudioCaptureService.SamplesAvailable`) — runs the FFT, spectral
  flux, onset peak-picking, and appends to the OSF ring. Must stay well under
  ~5 ms/hop, so it does **no** O(N·lag) work.
- **Tempo-timer thread** (`System.Threading.Timer`, every `TempoIntervalMs = 500 ms`)
  — snapshots the OSF under a lock and runs the autocorrelation tempo estimate. This
  is the only place the heavy O(lag·N) loop runs.
- **UI thread** — reads `CurrentBpm` / `Confidence` (both `volatile`) and handles the
  marshalled `BeatDetected` callbacks.

Cross-thread state:
- `_autoBpm`, `_autoConf` — `volatile`, written by the timer thread, read by the UI.
- `_audioRunning` — `volatile`, written by the capture thread (`StateChanged`), read on
  the audio thread.
- The OSF ring is guarded by `_osfLock` (a tiny critical section: one `double` write
  per hop, one array copy per 500 ms).

---

## 3. Audio capture (`WasapiLoopbackAudioCaptureService`)

Decisions that matter downstream:

- **Format normalization.** The system mixer can be any format (16/24/32-bit PCM or
  float, 44.1–192 kHz, mono…7.1). The service decodes every variant to mono `float`
  and **resamples to a canonical 44.1 kHz**. This is what lets the beat detector
  hard-code FFT/hop sizes in *samples* and have them keep a fixed wall-clock meaning.
- **Silence detection + `NoSignal`.** A 500 ms sliding-window RMS below `SilenceThreshold`
  (≈ −80 dBFS) flips the state to `NoSignal`. WASAPI loopback **stops firing callbacks
  during true silence**, so a watchdog timer forces `NoSignal` after 1.5 s without a
  callback (otherwise the state would stick at `Running` forever after audio stops).
- **`NoSignal` frames are still published.** The spectrum visualizer must keep moving,
  so `SamplesAvailable` fires in both `Running` *and* `NoSignal`. This is a sharp edge
  for the beat detector — see §6 (the phantom-beats bug).

---

## 4. Onset detection (audio thread)

Per hop of `HopSize = 256` samples (~5.8 ms), a `FftSize = 512` FFT (50 % overlap,
Hann-windowed) is taken over the most recent window.

### 4.1 Spectral flux in the kick/bass band
Flux = `Σ max(0, |X[k]| − |X_prev[k]|)` over bins **1–3 only** (≈ 86–258 Hz).

- **Why low-frequency only:** the musical *beat* is carried by kicks/bass; mid/high
  content (hats, vocals, melody) produces onsets on every subdivision and would drown
  the beat. Restricting to the kick band is the standard cheap beat-tracking front end.
- **Why positive-only (half-wave rectified):** we want energy *increases* (note onsets),
  not decays.
- **Why a Hann window:** reduces spectral leakage so a transient doesn't smear energy
  across bins.
- **Ring-buffer ordering invariant:** `_ringWritePos` points at the oldest sample, so
  the extraction loop yields oldest→newest — the correct time order for the FFT. Getting
  this wrong silently biases the low bins.

### 4.2 Adaptive threshold + peak-picking
An onset fires when **all** of these hold:

1. The flux value is a **local maximum** (`cand > left && cand >= right`, evaluated on
   the *previous* hop so we have one hop of look-ahead).
2. It exceeds the adaptive threshold `mean(last 40 flux values) × OnsetMultiplier`
   (default 1.5).
3. At least `MinInterOnsetMs = 200 ms` has elapsed since the last onset.
4. The capture state is `Running` (see §6).

- **Why peak-picking (added later):** the original code fired on the *first* frame above
  threshold, which on dense audio re-triggered every time the 200 ms gate reopened —
  pinning detection at the gate floor. Requiring a local maximum means one fire per
  genuine transient. It can only *reduce* firing versus the old rule, never increase it.
- **Why an adaptive (not fixed) threshold:** loopback levels vary wildly; a relative
  threshold tracks the recent signal. Its weakness — collapsing toward zero in quiet
  passages — is mitigated by the `threshold <= 0` guard and the `Running` gate (§6).
- **Why 200 ms:** caps the *pulse* rate at 300 BPM and suppresses double-triggers on one
  transient across adjacent hops.

The onset's `Confidence` (how far above threshold) rides along on `BeatEventArgs` and is
distinct from the BPM-estimate confidence.

---

## 5. Tempo estimation — why autocorrelation, not onset intervals

### 5.1 The history (important context)
The first implementation computed `BPM = 60000 / median(inter-beat-interval)` from the
discrete onsets (the now-removed `BpmEstimator`). It worked for a clean metronome but
failed two ways on real input:

1. **Real songs clipped to 240 BPM.** Dense low-band energy crossed the threshold almost
   every frame, so onsets fired continuously at the 200 ms gate floor → IBI ≈ 200 ms →
   ~300 BPM, clamped to 240. The model's hidden assumption — *every onset is a beat* — is
   false for music, which has onsets on many subdivisions plus noise.
2. **Metronomes jittered (190–235 for a 100 BPM source).** A metronome *tick* often
   carries little 0–300 Hz energy, so the adaptive threshold rode noise and the onset
   intervals were erratic. The median of erratic IBIs is meaningless.

Both failures share a root cause: **deriving tempo from discrete detections is fragile.**

### 5.2 The fix
Tempo is now estimated from the **periodicity of the continuous onset-strength envelope
(OSF)** via autocorrelation (`AutocorrelationTempoEstimator`). The OSF is just the
per-hop flux appended to a ~6 s ring (`OsfWindowSeconds`). Autocorrelation keys off the
*dominant repeating period*, so it locks onto the true beat even when individual onset
detections are noisy or missing — exactly the metronome and dense-music cases above.

This is the answer to the recurring question "does autocorrelation work for metronomes?"
— **yes, better than the onset-interval method**, because a click train is the ideal
periodic input.

### 5.3 Autocorrelation details and why
- **Lag range** spans `MinBpm = 15` to `MaxBpm = 360` BPM; `lagMax` is also capped at
  `N/2` so every lag has enough overlap. Note this `N/2` cap makes the *real* detectable
  floor ~20 BPM (a 15 BPM period exceeds half a 6 s window), independent of any weighting.
  `MaxBpm = 360` deliberately exceeds the device's 240 BPM cap: a periodicity faster than
  the ceiling has its fundamental lag below `lagMin` and can only be reported at a
  subharmonic (a ~300 BPM click read as 150). Detecting to 360 reports ~300 BPM music at its
  true tempo; the device output is still clamped to 240 by `BeatToDeviceMapper`, so this
  fixes the readout and divided-rhythm math without driving the hardware faster. 360 (not
  300) gives the 300 BPM fundamental headroom *inside* the range — right at the boundary the
  peak still collapses to the 150 subharmonic. See OpenPoints.md item 2.
- **Recency weighting (`RecencyTauSeconds`, default 2.5 s).** The autocorrelation runs over
  an exponentially recency-weighted copy of the OSF: weight `w[i] = exp(-(N−1−i)/τ)` with
  the newest sample at weight 1, folded in as `√w[i]·(x[i] − weightedMean)` so the existing
  bounded/biased autocorrelation math is unchanged (with `τ ≤ 0` the weights collapse to 1,
  reproducing the original estimate exactly). **Why:** when the input tempo changes, the old
  tempo's periodicity otherwise keeps winning the global max until it ages out of the full
  window — *worst on high→low*, because the biased normalisation (below) favours the old
  fast tempo's shorter lag, so the readout clings to it even once the new slow tempo is the
  majority. Weighting decays that stale evidence so the new tempo surfaces while it occupies
  only ~60 % of the window (~3.5 s after the change) instead of ~80 % (~4.8 s) — and it puts
  the lingering fast peak in the low-weight region so subharmonic rejection no longer
  re-promotes it. The window *length* is unchanged, so slow-tempo robustness is preserved;
  only the *influence* of old samples tapers. Lowering τ reacts faster but shrinks the
  effective evidence depth (the noise-robustness this whole approach buys, §5.2), so 2.5 s is
  the conservative default — tune it down per `AppConfig.json` for snappier reaction.
- **Biased normalization** (`sum / Σdev²`, a constant divisor) rather than unbiased
  (`/(N−lag)`). Biased deliberately favors *shorter* lags, which biases the picked peak
  toward the fundamental. This bias is real but **only ~10 % per octave** (peaks decay
  like `(N−lag)/N`), so it is *not* strong enough on its own to guarantee the fundamental
  over a subharmonic — see §5.5. Unbiased normalization would inflate slow lags and lock
  onto a half-tempo subharmonic outright.
- **Subharmonic rejection** (see §5.5) corrects the residual octave error directly, so the
  reported tempo is the true click rate even for accented metronomes.
- **Parabolic interpolation** around the peak gives sub-lag precision, which matters at
  small lags where one sample is several BPM.
- **Confidence** = the normalized autocorrelation at the peak (a 0–1 correlation strength).

### 5.5 Subharmonic rejection (octave-down correction)
Autocorrelation peaks at the true period **and every integer multiple of it**, so the
global-max lag can be a *sub-tempo*. This actually happens with metronomes: in the
0–300 Hz kick/bass band, accented and unaccented clicks carry different energy, so every
other (or every fourth) click dominates the envelope and the **half/quarter-tempo peak
wins the global max**. The symptom was a readout flapping `100 → 50 → 25` even with
folding off — i.e. the bug was *not* the fold classifier (§6) but the raw peak pick.

The fix (`AutocorrelationTempoEstimator.Analyze`): after the global-max lag `L` is found,
test its divisor lags `L/2, L/3, L/4`. If a divisor lands on a clear **local-maximum**
peak whose strength is ≥ `SubharmonicPeakFraction` (0.30) of the global peak, promote to
the **shortest** such lag (largest factor ⇒ fastest tempo ⇒ true fundamental). Key details:

- The divisor is searched over a **±3-sample neighbourhood** (`SubharmonicSearchRadius`),
  because integer rounding of `L/factor` (e.g. `207/2 = 104` vs. the real peak at `103`)
  otherwise lands beside the peak and silently fails the local-max test.
- The acceptance bar is **low (0.30)** on purpose: a strong accent pulls the fundamental
  peak well below the sub-tempo peak. It stays safe because *only the exact divisor lags*
  are tested, not every short lag — and on a **clean** train the divisor lags fall on
  autocorrelation **troughs**, so the train is left untouched.
- Applies in **both** fold modes; with folding on it just sharpens which harmonic feeds
  the fold. Guarded by `Analyze_AccentedMetronome_ReportsBeatNotSubharmonic` (a `[Theory]`
  over half- and quarter-tempo accents) which fails if the step is removed.

### 5.4 Octave folding and the preferred-tempo weighting
Autocorrelation peaks at the true period *and its multiples*, so a 120 BPM song also
peaks at 60 and 240. When folding is enabled, the estimator scores the harmonic set
`{2×, 1×, ½×, ⅓×}` (those within 15–360) by `autocorrelation × preference`, where
preference is a Gaussian in `log2(BPM)` centered on `PreferredBpmCenter = 120`
(`PreferredBpmSigma = 0.5` octaves). This collapses half/double-tempo readings toward a
musical range (~60–180) **without a hard cap**.

Folding is **currently disabled** (and was always conditional) — see §6.

### 5.6 Output smoothing — `TempoSmoother` (the asymmetric up-jump gate)
The autocorrelation estimate is **not published directly**. Each 500 ms cycle its result
is passed through `TempoSmoother` (`Features/BeatDetection/TempoSmoother.cs`) before being
stored in `CurrentBpm`. This fixes the symptom in OpenPoints §2: when the input tempo
changes, the recency-weighted autocorrelation briefly latches a spurious *short* lag (the
decaying old period plus a couple of close transition ticks), so the raw estimate spikes
**high** for ~0.5–1.5 s. Smoothing at the source keeps that spike out of the readout, the
beat pulse, and the device alike.

The filter is deliberately **asymmetric**:

- **Decreases and small increases** are adopted immediately. Slowing the source must never
  be delayed (the device should not keep running fast), and small drift needs no gating.
- **A large upward jump** — one that clears **both** a relative factor (`> +25 %`) **and**
  an absolute floor (`> +14 BPM`) — becomes a *pending candidate* and is adopted only after
  it persists for **5 consecutive cycles** (`TempoUpConfirmCycles`, ≈ 2.5 s) **unless it is
  already corroborated** (see below). A spike that fades before then is discarded and the
  previous tempo is held. This is the literal reading of the requirement "large jumps up …
  need more than two rapid-succession ticks to be applied."
- **Corroborated large up-jumps are adopted immediately** (`TempoCorroborationMin = 0.25`).
  The 5-cycle wait above is the *uncorroborated* path; it exists only to outlast a one-off
  transition gap, which is indistinguishable from a real speed-up *by timing alone*. But the
  estimator also reports `HarmonicSupport` — `ac[2L]/ac[L]`, how strongly the chosen period
  repeats (a real tempo correlates at twice its lag; a single interval has ~0 there). When a
  large up-jump's support clears the threshold, the repetition **is** the confirmation, so it
  is adopted on the first cycle it appears instead of after ~2.5 s. Validated against
  `captures/osf-20260605-184322.txt` (90→120, 120→180) and `-191543.txt` (180→240): support
  is ≥ 0.3 on the first cycle of each genuine jump, vs `0.00` for the 37 BPM overshoot, which
  therefore still takes the slow path and is rejected. The support is read from a small
  neighbourhood around `2L` (not the exact doubled lag): for an odd-lag tempo like 181 BPM
  (`L = 57`) the true half-tempo peak sits at ≈ 113.5, so `ac[114]` lands on the flank and
  understates support — the same integer-rounding fix the subharmonic search uses. **Floor**
  still inevitable: a faster tempo cannot be corroborated until its period has occurred twice
  (~1 s at 120 BPM), so the lag shrinks from ~2.5 s to about one new beat period, not zero.
- **Floor and confirm count were re-tuned** (was `+20 BPM` / 3 cycles) against a captured
  20→30 BPM metronome step (`captures/osf-20260605-125501.txt`). Bumping the input tempo
  injects one anomalously short transition interval — a single 1.6 s gap → a spurious **~37
  BPM** read that the recency-weighted autocorrelation latches onto for ~one new beat period
  (4 cycles at 30 BPM) before the true tempo fills the window. The `+20` floor let that 37
  through (it is only +17 over the prior 20), and 3 cycles was short enough that it confirmed.
  Lowering the floor to `+14` puts it *between* the genuine step (+10) and the overshoot
  (+17), so the real 30 passes through immediately while the 37 is gated; raising confirm to
  5 outlasts the 4-cycle overshoot, so the settle-down reading (30) arrives and discards the
  pending 37 first. **Trade-off:** a *genuine* large up-jump is now adopted ~1 s later. That
  latency is inherent — the only signal that an overshoot is spurious is that it falls back,
  which takes ≈ one new beat period to observe (confidence does *not* separate them: at the
  decision cycle the genuine 30→60 jump and the 37 artifact both read confidence ≈ 0.38).
- **Requiring both** a factor and a floor for "large" avoids gating tiny absolute drift at
  low BPM (the factor alone would) and small relative wobble at high BPM (the floor alone
  would). Successive candidates within `TempoConfirmToleranceBpm` (8 BPM) count as confirming
  the same pending tempo; a reading outside that restarts the count.
- A reading of **0** (lock lost / silence) passes through and clears any pending candidate;
  a full **stop/error** calls `Reset()` (wired in `OnAudioStateChanged`) so the next session
  re-locks from scratch rather than gating against a stale baseline.

The four thresholds are `private const` in `SpectralFluxBeatDetector` (passed into the
smoother's ctor) — fixed in code, **not** user-configurable, matching the `OnsetMultiplier`
convention. Guarded by `TempoSmootherTests`.

### 5.7 Capturing a real case for replay (diagnostics)

OpenPoints §2 can still recur on real input that the synthetic tests don't cover, so the
detector can record its actual OSF + per-cycle tempo output to a **replayable text file**.
This is a diagnostic seam, **off by default and zero-cost** when unused.

- **Turn on**: pass `--capture-osf` (writes a timestamped file under the app data
  `captures/` folder) or `--capture-osf=<path>` (resolved in `App.xaml.cs`,
  `ResolveOsfCapturePath`). The flag sets `AppSettings.OsfCapturePath`, which selects
  `OsfFileCaptureSink` over the `NullOsfCaptureSink` in DI.
- **What it records** (`IOsfCaptureSink` / `OsfFileCaptureSink`): a header with every
  estimator/smoother parameter, one `OSF <hopIndex> <flux> <running>` line per FFT hop, and
  one `CYCLE <head> <rawBpm> <conf> <sparsity> <smoothedBpm>` line per 500 ms tempo cycle.
  `rawBpm` is the estimate **before** the smoother — exactly the value that spikes.
- **Threading**: `RecordHop` runs on the audio thread but only appends to an in-memory
  buffer inside the existing `_osfLock` (no disk I/O); `RecordCycle` drains that buffer to
  disk on the tempo-timer thread. The audio-thread budget (§4) is preserved.
- **Replay**: `dotnet run --project tools/OsfReplay -- <capture.txt>` reconstructs each
  cycle's window from the continuous flux stream (`flux[head-osfLen .. head]`), re-runs the
  **real** `AutocorrelationTempoEstimator` + `TempoSmoother` (the tool compiles those exact
  source files, no copy), and prints a per-cycle table; matching `rplRaw == recRaw` confirms
  a faithful, deterministic replay. CLI overrides (`--tau`, `--osf-window`, `--jump-factor`,
  …) let you A/B parameter changes against the same captured case.

---

## 6. The regime classifier — when to fold (and why it is currently disabled)

> **Status (current): octave folding is DISABLED.** `OnTempoTimer` calls the estimator
> with `fold: false` for *all* input. The autocorrelation now runs unfolded everywhere;
> subharmonic rejection (§5.5) handles the octave error that folding used to clean up,
> and unfolded reporting keeps the true tempo across the full 15–360 BPM range for music
> and metronomes alike. The sparsity classifier below **still runs** but its `_foldDense`
> result is **not consumed** — it is retained (not deleted) so folding can be re-enabled
> later (e.g. behind a music/metronome UI toggle) by passing `fold: _foldDense` again.
> This is a deliberate decision pending a possible future music mode, not a temporary hack.
>
> The rest of this section documents the classifier as built, for when/if it is re-enabled.

Folding is right for dense music but **wrong for a fast metronome**: folding a 200 BPM
click train would report 100, violating the requirement that metronome input keep its
true tempo across the full 15–360 range. So the timer decides per-snapshot whether to
fold, using a content classifier.

### 6.1 The sparsity metric (and two rejected alternatives)
`ComputeSparsity` = the fraction of OSF hops that are **near-silent** (below 15 % of the
99th-percentile peak).

- A **click train** is near-silent between clicks → sparsity **0.83–0.98 at any tempo**,
  even over a noise floor.
- **Continuous music** keeps the envelope elevated → sparsity **~0.0–0.15**.

Two earlier metrics were tried and rejected — this is why the current one looks the way
it does:

1. **"Fraction of hops above the mean"** — *fooled by a steady noise floor.* Low-level
   room tone between clicks sits roughly half-above/half-below the mean, reading ~0.33
   and mis-classifying a real metronome as dense → it got folded → wrong tempo. This was
   an actual reported regression.
2. **"Energy concentration in the peakiest 10 % of hops"** — *collapsed for fast click
   trains.* At 240 BPM the clicks exceed 10 % of frames, so the top-10 % couldn't hold
   them and the value dropped to ~0.6, too close to dense content. Not tempo-robust.

Sparsity (peak-relative, percentile-referenced) is robust to **both** noise floor and
tempo, with a wide margin (~0.83 vs ~0.13) between the classes.

### 6.2 Thresholds + hysteresis
- `sparsity ≤ SparsityDenseMax (0.40)` → **dense** (fold).
- `sparsity ≥ SparsityMetronomeMin (0.60)` → **sparse/metronome** (no fold).
- In between → keep the current decision. The gap is a **hysteresis band** so a borderline
  track cannot flap between folded and unfolded every 500 ms.

The autocorrelation runs **every cycle regardless of regime**; sparsity only flips the
`fold` argument. So a 100 BPM metronome reads 100 whether classified sparse or dense
(100 is already the in-range fundamental); only fast metronomes (>180) actually depend on
the classification to avoid being folded.

---

## 7. State handling (the silence / stop edges)

These guards exist because of concrete bugs:

- **Onset firing is gated on `_audioRunning`.** Because `NoSignal` frames are still
  published (§3), and the adaptive threshold collapses on near-silence, the detector
  would otherwise emit **phantom beats on silence** — which kept `HasAudio` true (idle
  overlay never appeared) and fed garbage to the estimate. The OSF is still appended on
  `NoSignal` so the envelope keeps an honest picture of the quiet gaps; only the *firing*
  is suppressed.
- **`NoSignal` does *not* reset state.** A slow metronome's gaps legitimately dip to
  `NoSignal`; resetting there would wipe the estimate between clicks. (The ViewModel's
  4 s `AudioHold` similarly bridges these gaps for the overlay.)
- **`Stopped` / `Error` fully resets** the tempo result, fold state, last-beat tick, and
  the OSF ring, so the readout doesn't linger on a stale tempo after the source goes away.

---

## 8. ViewModel wiring (`SoundModeViewModel`)

- `LiveBpm` is refreshed from `CurrentBpm` on **every** `BeatDetected` (even while paused),
  so the readout is current the instant the user presses Play. Because `CurrentBpm` is the
  stable autocorrelation value, frequent onset-driven reads just re-sample a steady number.
- `HasAudio` is held true for `AudioHoldMs = 4000 ms` after each beat to bridge sparse/slow
  sources, then falls back to the live capture state. This is why stopping audio takes a
  few seconds to surface the idle overlay.
- `BeatTick` / `BeatPulse` drive the live dot + ring animation. They are paced by a
  `DispatcherTimer` whose interval is the beat period (`60000/LiveBpm` ms), **not** by the
  discrete onsets — so the visual pulse matches the displayed BPM instead of the (denser,
  jittery) raw onset rate. The timer runs only while `IsActivelyDriving` (device driving
  enabled + audio) and `LiveBpm > 0`, and restarts only when `LiveBpm` actually changes to
  avoid resetting its phase. Note: `IsDrivingDevice` gates *driving the device*, not detection
  — the Music BPM readout updates whenever `HasAudio`, independent of the play/pause toggle.
- All audio-thread callbacks marshal to the dispatcher before touching observable state.

### 8.1 Thrust rhythm → device BPM (Phase 4.1)

- **`ThrustRhythm`** (`Features/SoundMode/ThrustRhythm.cs`) — `EveryBeat`/`EveryTwoBeats`/`EveryFourBeats`.
  The enum's integer value **is** the divider ratio, so it casts straight to `int`.
- **`BeatToDeviceMapper.Map(musicBpm, rhythm, maxBpm)`** (`Features/SoundMode/BeatToDeviceMapper.cs`) —
  pure, stateless: `deviceBpm = round(min(musicBpm / ratio, maxBpm))`. The cap operates on the
  **unrounded post-ratio** value (design §6.4), then rounds. No `IDevice` dependency, no ramp.
- `SoundModeViewModel` exposes the read-only `RhythmOptions` tiles (each a `ThrustRhythmOption`
  with an `IsSelected` flag tracked exactly like `PresetItem.IsActive`), the `SelectedRhythm`
  enum, the `MaxBpm` cap (default **240 = uncapped**), and the computed display stats
  `DeviceBpm` / `DeviceSpeedPercent` / `IsRatioBadgeVisible` / `RatioBadgeText` /
  `MaxBpmPercent` / `IsCapped` / `IsCapActive`. `DeviceSpeedPercent` and `MaxBpmPercent` use the
  fixed **240 BPM** full-scale, not `IDevice.BpmToPercent` — the device dependency belongs to Phase 3.
- **Cap UI (§4.2):** a 0–240 slider bound to `MaxBpm`, a `"{MaxBpm} BPM · {MaxBpmPercent}%"` readout,
  and a "Capped" pill shown whenever `IsCapped` (cap set below full scale). `IsCapActive`
  (driving + capped + post-ratio tempo has reached the ceiling) drives a second gold "Capped"
  badge next to the Device stat.
- **Persistence (§4.3):** `SelectedRhythm` and `MaxBpm` survive restarts via `UserPreferencesStore`
  (`Configuration/`), which reads/writes `user-settings.json` under `%LOCALAPPDATA%\HismithController\`
  — separate from the read-only bundled `AppConfig.json`. The view-model loads prefs in its ctor
  (guarded so applying them doesn't re-trigger a save) and persists on change through a ~400 ms
  debounce, flushed on tab deactivation (`DeactivateAsync`) and app exit (`App.OnExit`). Enums are
  stored by name so reordering `ThrustRhythm` can't reinterpret old files; a missing/corrupt file or
  out-of-range cap falls back to defaults.
- **The mapping is display-only right now.** Phase 3 (sending `deviceBpm` to the BLE device) and
  §4.2/§4.3 (max-speed cap + persistence) are **not** implemented. `DeviceBpm`/`DeviceSpeedPercent`
  return **0 unless `IsActivelyDriving`** (audio present **and** Play engaged), matching the design's
  paused-state behaviour. The `÷N` rhythm badge **deviates from the design**: it is shown centered
  on the Music↔Device boundary (not next to the Device number) and is visible whenever a
  non-`EveryBeat` ratio is selected, independent of the driving state.
- The `RhythmDiagram` control (`UI/Controls/RhythmDiagram.xaml`) ports the design's SVG sine
  diagram: one full sine period per `Ratio` beats over a 96×32 surface, scaled by a `Viewbox`.

---

## 9. Tunables (`AppSettings`)

| Setting | Default | Meaning |
|---|---|---|
| `OnsetMultiplier` | 1.5 | adaptive onset threshold = mean(flux) × this |
| `OsfWindowSeconds` | 6.0 | OSF history length fed to autocorrelation |
| `RecencyTauSeconds` | 2.5 | exponential recency-weight time constant for the autocorrelation (lower = snappier reaction to tempo changes, less noise-robust; 0 = uniform/disabled) |
| `SparsityMetronomeMin` | 0.60 | ≥ ⇒ sparse/metronome (no fold) |
| `SparsityDenseMax` | 0.40 | ≤ ⇒ dense/song (fold) |
| `PreferredBpmCenter` | 120 | octave-fold preference center (BPM) |
| `PreferredBpmSigma` | 0.5 | octave-fold preference width (octaves) |

> The `BpmWindowK`, `BpmDeviationThreshold`, `BpmConfirmationTolerance`, and `BpmEmaAlpha`
> settings belonged to the retired `BpmEstimator` and were **removed** along with it.

---

## 10. Testing strategy

- **`AutocorrelationTempoEstimatorTests`** — pure, fast: synthetic OSF impulse trains for
  known tempos; verifies clean locking, octave folding (eighth-note train → quarter
  tempo), unfolded fast tempos, and zero/flat → 0.
- **`OnsetSparsityTests`** — the classifier metric across tempos and noise floors; the
  guard against the metronome-misrouting regression (metronome ≥ 0.60, continuous ≤ 0.40).
- **`MockSignalBeatDetectionTests`** — real-time integration (paced in wall-clock because
  the onset gate uses `Environment.TickCount64`): a clean metronome locks to its true
  tempo; `NoSignal` frames fire no phantom beats; `Stopped` resets the readout.
- **`BeatToDeviceMapperTests`** — the pure ratio + rounding math (pass-through, half, quarter,
  zero, and the banker's-rounding midpoint case).

---

## 11. Known limitations & possible future work

- **Latency:** autocorrelation needs ~1.5–2 s of audio to lock and updates every 500 ms,
  so the BPM appears a beat or two after playback starts. Tempo *changes* react over a few
  seconds; recency weighting (§5.3, `RecencyTauSeconds`) cuts the worst case (high→low) from
  ~4.8 s to ~3.5 s at the default τ without shrinking the window. This residual lag is the
  cost of robustness over the (fragile) instantaneous IBI method — fundamentally the
  estimator must observe ~2 periods of the new tempo before it can lock onto it.
  - On top of this, a genuine **upward** tempo change lags an extra ~1.5 s **by design**:
    `TempoSmoother` (§5.6) holds a large up-jump until it is confirmed across 3 cycles, to
    reject the change-transient spike. Downward changes are not delayed.
- **Fast metronomes (>180 BPM)** depend on the sparsity classifier to avoid folding; a
  very reverberant/noisy click could in principle read as dense.
- **Possible manual mode switch:** letting the user force "Music" (fold) vs "Metronome"
  (no fold) would remove the dependence on auto-classification. This touches the UI, which
  is governed by the design files (`design/modes.jsx`) — any such control must be agreed
  with the design owner before implementation.
```
