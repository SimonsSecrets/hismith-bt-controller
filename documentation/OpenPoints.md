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

## 3. Device calibration
Issue: It seems like the device response is not fully linear to the percentage input.
Goal: Set up a device calibration curve based on real world measurements (mapping input percentage to observed thrusting tempo).

## 4. Security
Goal: Scan the whole application for any security concerns/issues and make sure it is trustworthy on other computers.
When running the application on other computers, i want to avoid that windows flags the app as suspicious or untrusted.

## 5. Sound mode visualizer beat display 
Make the sound mode visualizer beats align with the detected beats (instead of the calculated bpm).
