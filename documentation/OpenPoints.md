# Open Points

## 1. Emergency stop ✅ Resolved
Issue: The emergency stop using a keyboard button (space) does not trigger when app is not focused.
Goal: Implement a mechanism to trigger emergency stop regardless of app focus state.
Resolution: Added a system-wide low-level keyboard hook (`GlobalKeyboardHook`, WH_KEYBOARD_LL,
non-swallowing) that fires the emergency stop on Spacebar even when another window is focused.
The hook is gated on `IsConnected` and bails when the app is the active window (the existing
`OnPreviewKeyDown` path handles the focused case).

## 2. Sound mode sudden tempo changes
Issue: In sound mode, when changing the metronome input tempo, detected bpm often jumps really high for a short time.
This is kind of expected, since the metronome temp change results in two ticks being really close together.
Goal: Implement a mechanism to smooth out the tempo change, specifically handling these sudden metronome input changes.

## 3. Device calibration
Issue: It seems like the device response is not fully linear to the percentage input.
Goal: Set up a device calibration curve based on real world measurements (mapping input percentage to observed thrusting tempo).

