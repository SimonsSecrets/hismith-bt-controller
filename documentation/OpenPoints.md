# Open Points

## 1. Emergency stop ✅ Resolved
Issue: The emergency stop using a keyboard button (space) does not trigger when app is not focused.
Goal: Implement a mechanism to trigger emergency stop regardless of app focus state.
Resolution: Added a system-wide low-level keyboard hook (`GlobalKeyboardHook`, WH_KEYBOARD_LL,
non-swallowing) that fires the emergency stop on Spacebar even when another window is focused.
The hook is gated on `IsConnected` and bails when the app is the active window (the existing
`OnPreviewKeyDown` path handles the focused case).

## 2. Sound mode sudden tempo changes ✅ Resolved
Issue: In sound mode, when changing the metronome input tempo, detected bpm often jumps really high for a short time.
This is kind of expected, since the metronome temp change results in two ticks being really close together.
Goal: Implement a mechanism to smooth out the tempo change, specifically handling these sudden metronome input changes (large jumps up should be followed more conservatively, eg need more than two rapid succession ticks to be applied).
Resolution: Added `TempoSmoother` (`Features/BeatDetection/TempoSmoother.cs`), an asymmetric
confirmation filter applied to the autocorrelation estimate before it is published as
`CurrentBpm`. Decreases and small increases pass straight through (slowing is never delayed),
but a *large* upward jump (> +25 % **and** > +14 BPM) is held as a pending candidate and only
adopted once it persists for 5 consecutive 500 ms tempo cycles (≈ 2.5 s) — so the transient
spike thrown off while the input tempo changes is rejected, while a genuine speed-up still
takes effect after a brief delay. Smoothing is applied at the source, so the Music BPM
readout, the beat pulse, and the device output all stay stable. See
`documentation/SoundModeImplementation.md` (tempo output smoothing).

Follow-up (capture `osf-20260605-125501.txt`): the original `+20 BPM` / 3-cycle tuning still
let a smaller residual overshoot through — bumping a metronome 20→30 BPM injects one ~1.6 s
transition gap that the autocorrelation briefly reads as **~37 BPM**. The `+20` floor missed
it (only +17 over 20) and 3 cycles was too short to wait it out. Re-tuned to `+14 BPM` /
5 cycles: the floor now sits between the genuine step (+10) and the overshoot (+17) so the
real 30 passes immediately while the 37 is gated, and 5 cycles outlasts the ~4-cycle overshoot
so the settle-down reading discards it. Cost: genuine large up-jumps adopt ~1 s later (the
overshoot and a real jump are indistinguishable until the overshoot falls back).

Follow-up 2 — response lag (capture `osf-20260605-184322.txt`). Two transition lags
surfaced once the overshoot was fixed:
- **Stop lag ✅ Resolved.** When a fast source stopped, the device took ~4 s to stop. Cause:
  the device is gated on `SoundModeViewModel.HasAudio`, which was held a **fixed 4000 ms**
  after the last beat (sized for one 15 BPM period to bridge a slow click's inter-tick
  silence). The hold is now **tempo-relative** (`HoldMsForBpm`): `DropoutBeats (3) × beat
  period`, clamped to `[1000, 4000] ms`. At 120 BPM that is ~1.5 s; at ≤ 40 BPM it stays at
  the 4000 ms ceiling, so slow sources are unchanged. Safe against missed onsets because the
  hold's expiry re-reads the capture state (`HasAudio = state == Running`), so a hold lapsing
  while audio still plays keeps `HasAudio` true — the device only stops once the hold lapses
  **and** the capture is RMS-silent (NoSignal, ~0.5–1.5 s). Floor 1000 ms keeps the hold above
  the service's 500 ms silence-RMS window. See `SoundModeHoldTests`.
- **Increase lag ✅ Resolved.** A genuine large up-jump (90→120, 120→180, 180→240) used to wait
  the full 5-cycle confirmation (~2.5 s ≈ 10 ticks at 240 BPM) even though the new tempo was
  already corroborated. The estimator now reports `HarmonicSupport` (`ac[2L]/ac[L]`, read from
  a small neighbourhood around `2L` to survive odd-lag tempos like 181 BPM), and `TempoSmoother`
  adopts a large up-jump immediately when support ≥ `TempoCorroborationMin` (0.25). Validated on
  captures `osf-20260605-184322` and `-191543`: every genuine jump adopts on the first cycle it
  appears (support ≥ 0.3), while the 37 BPM overshoot (support 0.00) still takes the slow path
  and is rejected. Residual lag is just the estimator lock (~1 new beat period — inherent).
  See `documentation/SoundModeImplementation.md` (corroborated large up-jumps) and
  `TempoSmootherTests` / `AutocorrelationTempoEstimatorTests`.
- **>240 BPM subharmonic ✅ Resolved.** A ~300 BPM click (0.20 s) read as 150 (its 2× lag)
  because `maxBpm = 240` put the true lag below `lagMin`. Raised the estimator's `MaxBpm` to
  **360** (well above the device's 240 cap) so the fundamental sits inside the range and ~300
  BPM reports its true tempo. The device output is still clamped to 240 by `BeatToDeviceMapper`,
  so this corrects the Music BPM readout and divided-rhythm math without driving the hardware
  faster (chosen over clamp-and-document). 360 — not 300 — is required: at a 300 ceiling the
  300 BPM fundamental lands on the boundary lag and still collapses to 150. Validated on
  `osf-20260605-184322` (the 300 region now reads ~301) with no regression on 20–240 BPM tempos
  or the stop. See `AutocorrelationTempoEstimatorTests.Analyze_FastTrainAboveCeiling_*`.

## 3. Device calibration
Issue: It seems like the device response is not fully linear to the percentage input.
Goal: Set up a device calibration curve based on real world measurements (mapping input percentage to observed thrusting tempo).

## 4. Security
Goal: Scan the whole application for any security concerns/issues and make sure it is trustworthy on other computers.
When running the application on other computers, i want to avoid that windows flags the app as suspicious or untrusted.

## 5. Sound mode visualizer beat display 
Make the sound mode visualizer beats align with the detected beats (instead of the calculated bpm).

## 6. Background image
The background image seems to jump slightly between the scanning screen and the device listing screen.
