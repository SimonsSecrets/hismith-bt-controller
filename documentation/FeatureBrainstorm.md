# HismithController — Feature & Use-Case Brainstorm

*Created 2026-06-01. A living idea backlog. Nothing here is committed work — it is a menu of
options grounded in what the app already does and how it is built.*

*Each item has a short, stable ID (theme-mnemonic prefix + number) so it can be referenced
easily. Prefixes: MODE (Theme 1), DSP (Theme 2), IN (Theme 3), RMT (Theme 4), DEV (Theme 5),
SAFE (Theme 6), UX (Theme 7), DATA (Theme 8).*

## Where the app is today

HismithController is a Windows WPF app that captures system audio via NAudio
`WasapiLoopbackCapture`, runs real-time spectral-flux onset detection plus autocorrelation
tempo (BPM) estimation, and drives a Hismith BLE device's speed over WinRT GATT writes.

Current capabilities, as built:

- **Sound mode** — system audio → beat/BPM → device speed, the headline feature.
- **Manual mode** — direct speed/BPM target with smooth ramping and Slow/Medium/Fast/Max presets.
- **Device discovery + connection flow** — BLE scan, model-aware (AK-01 / AK Series) connect.
- **Mock mode** — `--mock` runs audio + beat detection with no hardware, logging speed commands.
- **Config** — `AppConfig.json` exposes detector tuning (onset multiplier, autocorrelation window, etc.).
- **Architecture** — three subsystems (Audio, BeatDetection, Bluetooth) behind interfaces, wired
  via DI and C# events, with a strict UI-thread / audio-thread / serialized-BLE-write threading model.

The brainstorm below builds on these primitives. Most ideas reuse the existing audio pipeline,
BLE protocol layer, or mode/ViewModel pattern rather than introducing new subsystems.

Priority is rough (P1 = high value / well-aligned, P3 = speculative). Effort is S/M/L relative to
the existing codebase.

---

## Theme 1 — New control "modes" (reuse the mode + ViewModel pattern)

The app already has a Manual/Sound mode switcher. Each new mode is mostly a ViewModel plus a XAML
panel feeding the same `IBleDeviceService.SetSpeed` path, so this is the highest-leverage theme.

- **MODE-01 · P1 · M — Pattern / waveform mode.** Drive speed from a looping function (sine, triangle,
  sawtooth, square, random-walk) with adjustable period, min/max bounds, and amplitude. No audio
  needed — pure timer-driven output through the existing speed pipeline. A natural sibling to Manual.
- **MODE-02 · P1 · M — Programmable sequences / "routines".** A timeline of steps (e.g. 30 s @ 40 %, ramp to
  80 % over 60 s, hold, drop). Save/load named routines as JSON next to `AppConfig.json`. Reuses the
  ramping logic already in Manual mode.
- **MODE-03 · P2 · M — Interval / "tabata" mode.** Alternating high/low phases with configurable work/rest
  durations and an optional ramp between them; a constrained, friendlier sequence editor.
- **MODE-04 · P2 · M — Microphone / ambient-sound mode.** Same beat pipeline but sourced from a mic input
  device instead of loopback, so the device reacts to room audio rather than only system playback.
  NAudio already supports `WaveInEvent`; this is an alternate `IAudioCaptureService` implementation.
- **MODE-05 · P3 · L — Voice / breath mode.** Map mic loudness envelope (not beats) to speed — a continuous
  follower rather than a beat trigger.

## Theme 2 — Beat / Sound-mode intelligence (build on the existing DSP)

These deepen the feature the app is named for, extending `SpectralFluxBeatDetector` /
`AutocorrelationTempoEstimator`.

- **DSP-01 · P1 · S — Speed-mapping curve control.** Today BPM→speed mapping is roughly linear. Expose a
  configurable transfer curve: min/max BPM clamp, a gamma/exponent, and floor/ceiling speed. Cheap,
  and a big quality-of-experience lever for Sound mode.
- **DSP-02 · P1 · S — Energy / intensity mapping, not just tempo.** Map overall band energy (RMS or
  spectral-flux magnitude) to speed *alongside* BPM, so a loud 120-BPM section pushes harder than a
  quiet one. The spectrum data is already computed.
- **DSP-03 · P2 · M — Frequency-band targeting.** Let the user pick which band drives the device (kick/bass
  vs. full-range vs. a custom range). The FFT bins already exist; this is a UI + parameter exposure.
- **DSP-04 · P2 · M — Beat subdivision / multiplier.** Drive on every beat, every other beat, or double-time,
  with a half/double-tempo toggle — and finally surface the *octave-folding* logic that is built but
  currently disabled (see `SoundModeImplementation.md` §5.5).
- **DSP-05 · P2 · M — Smoothing / responsiveness slider.** A single user-facing "snappy ↔ smooth" control
  that maps onto ramp rate + `RecencyTauSeconds`, hiding the low-level tuning constants.
- **DSP-06 · P3 · M — Music vs. metronome UI toggle.** The sparsity classifier already distinguishes these
  regimes internally; expose it so users can force the right behavior for steady click tracks.

## Theme 3 — Input sources beyond local audio

- **IN-01 · P2 · L — Spotify / media "Now Playing" integration.** Pull tempo/energy from the Spotify Web
  API audio-features for the current track instead of (or to validate) the DSP estimate. Could search
  the connector registry for a Spotify connector. Adds song-aware presets.
- **IN-02 · P3 · L — MIDI clock / DAW sync.** NAudio includes MIDI support; lock the device to an external
  MIDI clock or note triggers for music-production / performance contexts.
- **IN-03 · P3 · M — OSC / WebSocket control input.** Let another app or a phone send speed/BPM commands
  over the network, turning HismithController into a controllable server.
- **IN-04 · P3 · L — Video / game reactivity.** React to on-screen events (loudness spikes already cover much
  of this via loopback; an explicit "game audio" profile with attack-biased detection is the lighter
  version).

## Theme 4 — Remote control & multi-surface

- **RMT-01 · P2 · L — Mobile / web remote.** A small embedded HTTP/WebSocket server exposing the current mode
  and speed, with a phone-friendly page to adjust them — useful because the device user may not be at
  the keyboard. Pairs naturally with IN-03's OSC idea (shared transport).
- **RMT-02 · P3 · L — Companion remote for a second person.** Same server, with a shareable session
  link/PIN so a partner can control speed/mode in real time. Needs consent + safety gating (Theme 6).
- **RMT-03 · P3 · M — Global hotkeys / hardware knob.** System-wide keyboard shortcuts or a MIDI/HID dial for
  hands-free up/down/stop without focusing the window.

## Theme 5 — Devices, protocol & hardware breadth

- **DEV-01 · P1 · S — Vibration / mode-command support.** The protocol already documents
  `Set mode: AA 05 mode(01-09)`. Surface device modes/patterns in the UI — low effort, the frame
  encoding exists in `HismithProtocol.cs`.
- **DEV-02 · P2 · M — Multi-device control.** Connect and drive more than one device, either mirrored or with
  independent per-device mappings. `ConnectedDeviceService` already abstracts a device; this extends
  it to a collection.
- **DEV-03 · P2 · M — Broader Hismith model support + capability detection.** Generalize beyond AK-01: detect
  model code (already parsed big-endian) and adapt speed range / available modes per model.
- **DEV-04 · P3 · L — Other-brand support via buttplug.io / Intiface.** The repo already vendors the
  buttplug.io protocol notes; an `IBleDeviceService` implementation targeting Intiface Central would
  open the app to the wider device ecosystem.
- **DEV-05 · P2 · S — Connection resilience.** Auto-reconnect on BLE drop, a heartbeat/keepalive, and a
  visible connection-quality indicator. Important because a mid-session disconnect is disruptive.

## Theme 6 — Safety, comfort & wellbeing (high value, often low effort)

These matter for a device that applies physical motion and should be treated as first-class.

- **SAFE-01 · P1 · S — Master speed limit / ceiling.** A global max-speed cap that *all* modes respect,
  including Sound mode peaks. Single clamp in the speed pipeline.
- **SAFE-02 · P1 · S — Panic / instant-stop.** A always-available large Stop control + global hotkey that
  issues `AA 04 00 04` immediately, bypassing ramps.
- **SAFE-03 · P1 · S — Ramp-rate / acceleration limit.** Cap how fast speed can change so a sudden loud beat or
  routine step can't jolt to max. Partially exists as ramping; make it an enforced safety bound.
- **SAFE-04 · P2 · S — Session timer / auto-stop.** Optional max-duration and idle-audio auto-stop (when
  `HasAudio` goes false for N seconds, wind down). The liveness signal already exists.
- **SAFE-05 · P2 · S — Gradual warm-up / cool-down.** Modes start from a low floor and ease in rather than
  snapping to the mapped speed on activation.
- **SAFE-06 · P3 · S — Safeword integration.** A keyword (typed, hotkey, or mic) that triggers instant-stop —
  ties into Theme 4 remote sessions.

## Theme 7 — UX, feedback & polish

- **UX-01 · P1 · S — Settings menu.** Already on the open-tasks list: a settings panel with a "open
  logs/crashdump folder" button, theme persistence, and exposure of the `AppConfig.json` tuning
  values through UI instead of hand-editing JSON.
- **UX-02 · P1 · S — Persist preferences.** Save selected color theme, last mode, last device, and mapping
  settings between runs (open-tasks item). A small settings-store service.
- **UX-03 · P2 · M — Richer real-time visualization.** Spectrum/waveform display, live BPM confidence, and a
  speed-over-time graph. `SpectrumAnalyzer` output is already available to render.
- **UX-04 · P2 · S — Finish the connection-flow visual states.** The open tasks note step indicators should
  move rose → dark-rose → green and the selected-device icon is mis-rendered; closing these polishes
  the first-run experience.
- **UX-05 · P2 · S — Per-mode presets & quick-recall.** Save named configurations per mode and recall them
  from a dropdown (extends the existing `SpeedPreset` concept).
- **UX-06 · P3 · S — Minimal / overlay / always-on-top mini-mode.** A compact window showing just speed +
  stop for when the main UI isn't needed.
- **UX-07 · P3 · S — System tray + media-key control.** Run minimized to tray with play/pause/stop bindings.

## Theme 8 — Data, diagnostics & extensibility

- **DATA-01 · P2 · M — Session history & stats.** Optional, privacy-respecting local log of session duration,
  average/peak speed, and mode usage, viewable in-app. Builds on the existing `FileLoggerProvider`.
- **DATA-02 · P2 · S — In-app diagnostics view.** Live readout of detector internals (current flux, threshold,
  autocorrelation lag, regime classification) to make tuning and bug reports easier — surfaces what
  `SoundModeImplementation.md` describes.
- **DATA-03 · P3 · M — Detector tuning / calibration wizard.** Play a known track or click track and
  auto-suggest `OnsetMultiplier` and mapping bounds.
- **DATA-04 · P3 · L — Scripting / plugin hooks.** Expose mode and mapping logic to a small scripting surface
  (e.g. C# scripting or a config DSL) so advanced users can define custom behaviors without rebuilding.

---

## Suggested near-term shortlist

If the goal is the best ratio of value to effort, grounded in what's already half-built:

1. **MODE-01 — Pattern/waveform mode** (Theme 1) — biggest new-capability win, reuses the speed pipeline.
2. **DSP-01 + DSP-02 — Speed-mapping curve + energy mapping** (Theme 2) — makes the flagship Sound mode feel much better.
3. **SAFE-01 + SAFE-02 + SAFE-03 — Safety trio: master cap, panic-stop, ramp-rate limit** (Theme 6) — low effort, high importance.
4. **UX-01 + UX-02 — Settings menu + preference persistence** (Theme 7) — already on the open-tasks list.
5. **DEV-01 — Surface device mode commands** (Theme 5) — the protocol support already exists.

## Open questions to steer prioritization

- Who is the primary user — solo, or is partner/remote control (Theme 4) a real target?
- Is broad device support (Theme 5 / buttplug.io) a goal, or is AK-01 the only device that matters?
- How much should live on-screen visualization matter vs. a minimal/background UX?
