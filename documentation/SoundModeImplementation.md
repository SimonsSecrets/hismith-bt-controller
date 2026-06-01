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
                                                   ├─ per hop (audio thread):  spectral flux ─► onset peak-pick ─► BeatDetected (pulse/liveness)
                                                   │                                          └─► append to OSF ring
                                                   └─ every 500 ms (timer thread): autocorrelation over OSF ─► CurrentBpm / Confidence
```

Two outputs come out of the detector and they are deliberately **decoupled**:

| Output | Source | Used for |
|---|---|---|
| `BeatDetected` events | discrete spectral-flux onsets | the visual beat pulse + "audio is live" signal |
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
- **Lag range** spans `MinBpm = 15` to `MaxBpm = 240` BPM; `lagMax` is also capped at
  `N/2` so every lag has enough overlap.
- **Biased normalization** (`sum / Σdev²`, a constant divisor) rather than unbiased
  (`/(N−lag)`). Biased deliberately favors *shorter* lags, so the picked peak is the
  fundamental or a **higher** harmonic of the true tempo — never a subharmonic. The fold
  stage (below) can divide a too-fast pick down into range, but it cannot recover a
  too-slow (halved) pick. Unbiased normalization would inflate slow lags and risk locking
  onto a half-tempo subharmonic.
- **Parabolic interpolation** around the peak gives sub-lag precision, which matters at
  small lags where one sample is several BPM.
- **Confidence** = the normalized autocorrelation at the peak (a 0–1 correlation strength).

### 5.4 Octave folding and the preferred-tempo weighting
Autocorrelation peaks at the true period *and its multiples*, so a 120 BPM song also
peaks at 60 and 240. When folding is enabled, the estimator scores the harmonic set
`{2×, 1×, ½×, ⅓×}` (those within 15–240) by `autocorrelation × preference`, where
preference is a Gaussian in `log2(BPM)` centered on `PreferredBpmCenter = 120`
(`PreferredBpmSigma = 0.5` octaves). This collapses half/double-tempo readings toward a
musical range (~60–180) **without a hard cap**.

Folding is **not always applied** — see §6.

---

## 6. The regime classifier — when to fold (and the bugs it fixes)

Folding is right for dense music but **wrong for a fast metronome**: folding a 200 BPM
click train would report 100, violating the requirement that metronome input keep its
true tempo across the full 15–240 range. So the timer decides per-snapshot whether to
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
- `BeatTick` / `BeatPulse` drive the ring animation off the discrete onsets (cosmetic).
- All audio-thread callbacks marshal to the dispatcher before touching observable state.

---

## 9. Tunables (`AppSettings`)

| Setting | Default | Meaning |
|---|---|---|
| `OnsetMultiplier` | 1.5 | adaptive onset threshold = mean(flux) × this |
| `OsfWindowSeconds` | 6.0 | OSF history length fed to autocorrelation |
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

---

## 11. Known limitations & possible future work

- **Latency:** autocorrelation needs ~1.5–2 s of audio to lock and updates every 500 ms,
  so the BPM appears a beat or two after playback starts and reacts to tempo changes over
  a few seconds. This is the cost of robustness over the (fragile) instantaneous IBI method.
- **Fast metronomes (>180 BPM)** depend on the sparsity classifier to avoid folding; a
  very reverberant/noisy click could in principle read as dense.
- **Possible manual mode switch:** letting the user force "Music" (fold) vs "Metronome"
  (no fold) would remove the dependence on auto-classification. This touches the UI, which
  is governed by the design files (`design/modes.jsx`) — any such control must be agreed
  with the design owner before implementation.
```
