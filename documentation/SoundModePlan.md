# Sound Mode — Implementation Plan

Step-by-step plan to build the **Sound Mode** feature: capture system audio → visualize → detect beats → drive the Hismith device → expose user controls.

The plan is split into four phases. Each phase ends in a runnable, demoable state so we can validate before moving on. The order intentionally defers the device wiring — we want the audio + visualizer rock-solid, then the beat detection trustworthy, before we let any of it touch the BLE device.

Design source of truth: [design/modes.jsx](design/modes.jsx) (SoundMode component) and [UI_Design_Requirements.md](UI_Design_Requirements.md) §6.

---

## Phase 1 — Audio capture + visualization

**Goal:** When the user switches to the Sound tab, the app captures system audio via loopback and renders a live spectrum visualizer. No beat detection, no device commands yet.

### 1.1 Scaffold the feature folder ✅
- Create `src/HismithController/Features/Audio/` with the following interfaces and types:
  - `IAudioCaptureService` — start/stop loopback capture, exposes a `SamplesAvailable` event carrying a `ReadOnlySpan<float>` of mono PCM at a known sample rate.
  - `AudioFrame` — record containing the latest mono float buffer + sample rate + timestamp.
  - `AudioCaptureState` enum (`Stopped`, `Starting`, `Running`, `NoSignal`, `Error`).
- Register `IAudioCaptureService` in the DI container in `App.xaml.cs`.

### 1.2 Implement `WasapiLoopbackAudioCaptureService`
- Wrap `NAudio.CoreAudioApi.WasapiLoopbackCapture`.
- **Handle any system audio format**, not just float32 at 44.1/48 kHz. `WasapiLoopbackCapture.WaveFormat` is whatever the system mixer is configured for and can be 16-bit PCM, 24-bit PCM, 32-bit int, or float, at any sample rate (44.1, 48, 96, 192 kHz…), with any channel count (mono, stereo, 5.1, 7.1).
  - On `DataAvailable`, branch on `WaveFormat.Encoding` + `BitsPerSample` to decode each frame into a `float[]` in the range `[-1, 1]`. Cover `IeeeFloat` (32-bit), `Pcm` (16/24/32-bit), and `Extensible` (inspect `SubFormat` GUID — both PCM and float subformats exist in the wild). Throw a clear `NotSupportedException` for anything else so we discover it instead of silently writing zeros.
  - **Downmix to mono** by averaging all channels (`sum / channelCount`), not just the first two — this keeps surround-sound setups working.
  - **Resample to a canonical 44.1 kHz** before publishing samples downstream. Use `NAudio.Wave.MediaFoundationResampler` or a simple linear/polyphase resampler. Fixing the downstream sample rate means the beat detector's FFT size, hop size, and threshold history length (all expressed in samples) keep their documented time-domain meaning regardless of the source rate.
  - The published `AudioFrame` always carries: `float[] monoSamples` at 44.1 kHz, the original source format (for diagnostics/logging), and a timestamp.
- Implement a simple silence detector: if RMS over the last ~500ms is below a small threshold, set state to `NoSignal` so the UI can show the "Play some music to get started" idle state.
- All NAudio callbacks fire on a worker thread — the service must marshal `SamplesAvailable` to subscribers without blocking the capture thread. Keep the callback under ~2ms of work (decode + downmix + resample combined).

### 1.2a Mock variants — two independent axes
Mocking BLE and mocking audio are independent concerns and should be controllable separately. Extend `AppSettings.cs` accordingly:
- `UseMockBle` (existing) — already gates `MockBleDeviceService` vs `HismithBleDeviceService`.
- Add `UseMockAudio` (new bool, default false). Add a matching `--mock-audio` command-line flag. The existing `--mock` flag should now imply **both** `UseMockBle=true` and `UseMockAudio=true` (so existing behaviour is preserved); add `--mock-ble` as an explicit single-axis alternative.
- Wire DI in `App.xaml.cs` to choose `IAudioCaptureService` based on `UseMockAudio`, independently from how `IBleDeviceService` is chosen.
- This gives four runnable combinations:
  - real BLE + real audio (production)
  - mock BLE + real audio (develop Sound Mode without a Hismith — the use case you called out)
  - real BLE + mock audio (test the device-driving path with a deterministic synthetic beat)
  - mock BLE + mock audio (fully offline development)

Implement `MockAudioCaptureService` to generate a 120 BPM kick-style pulse + pink noise mix at 44.1 kHz mono, so the visualizer and beat detector have a deterministic signal to lock onto.

### 1.3 Spectrum data pipeline
- Add a `SpectrumAnalyzer` class (in the same folder) that maintains a rolling FFT over the incoming mono samples:
  - FFT size 1024, hop 512 (≈11.6 ms hop at 44.1 kHz), Hann window.
  - Output ~56 logarithmically-grouped magnitude bins to match the 56-bar visualizer in `modes.jsx`.
  - Apply a smoothing/decay (`bin = max(newValue, bin * 0.85)`) so bars don't jitter visually.
- Expose a `SpectrumUpdated` event delivering the 56-bin array at ~30 fps (downsample from the FFT rate).

### 1.4 `SoundModeViewModel` (minimal)
- Create `src/HismithController/UI/ViewModels/SoundModeViewModel.cs` modeled after [ManualModeViewModel.cs](src/HismithController/UI/ViewModels/ManualModeViewModel.cs).
- Properties for this phase:
  - `HasAudio` (bool) — true when capture state is `Running` and not in `NoSignal`.
  - `IsPlaying` (bool) — user-toggled; **defaults to `false` (paused) every time the tab is activated.** The user must explicitly press Play to start beat-driven device commands. This avoids startling the user with sudden device activity the moment they switch to Sound Mode. (No device commands wired in this phase yet, but the bool already gates everything that will be wired in Phase 3.)
  - `SpectrumBins` (ObservableCollection<double> or float[]) — bound to the visualizer.
- `InitializeAsync()` starts the capture service and sets `IsPlaying = false`; `Dispose`/`Deactivate` stops it.
- Marshal incoming events onto the UI dispatcher.

### 1.5 `SoundModeView.xaml` — visualizer-only first cut
- New view under `src/HismithController/UI/Views/SoundModeView.xaml` matching the card layout from `modes.jsx`:
  - Header: "Sound mode" label + "Beats from your system audio drive the device." subline.
  - Visualizer area: an `ItemsControl` bound to `SpectrumBins`, rendering 56 thin vertical bars. Each bar's `Height` (or `ScaleY`) is bound to its magnitude.
    - Use the warm rose accent. Keep DOM-equivalent: a single `ItemsControl` with no per-frame element creation — only update bound values.
  - Idle overlay (the "Play some music to get started" rings) shown when `!HasAudio`. **Skip** the "Listening to system audio" / "No audio" sublabel from the design — the visualizer itself communicates the listening state, and the overlay covers the silent case.
- Apply the two-layer drop-shadow card pattern from [CLAUDE.md](CLAUDE.md) — do not put the `DropShadowEffect` on the same border as the text.

### 1.6 Wire the Sound tab into navigation
- In [MainViewModel.cs](src/HismithController/UI/ViewModels/MainViewModel.cs), extend `OnActiveModeChanged` to set `ActiveModeContent = SoundModeViewModel` when `ActiveMode == "Sound"`.
- Inject `SoundModeViewModel` into `MainViewModel` constructor and register it in DI.
- Add a `DataTemplate` mapping `SoundModeViewModel` → `SoundModeView` in `App.xaml`.

### 1.7 Verify Phase 1
- Run the app (`dotnet run --project src/HismithController/HismithController.csproj`), connect to a real or mock Hismith, switch to the Sound tab.
- Play music in a browser/Spotify — the 56-bar visualizer should respond smoothly with no UI hitching.
- Stop the music — the idle overlay should appear within ~1 second.
- Switch back to Manual mode — capture should stop (verify via task manager / no CPU drain).
- **Format coverage:** run with the system mixer set to a non-default format (Windows → Sound settings → device Properties → Advanced → e.g. 24-bit 48 kHz, then 16-bit 44.1 kHz, then a 5.1 layout if available). The visualizer should behave identically in each case. Log the source format on capture start to confirm the decode path was exercised.
- **Mock matrix:** run `--mock-ble` (real audio + mock BLE) and confirm the visualizer reacts to actual system audio while BLE writes appear in the log panel. Run `--mock-audio` and confirm the visualizer shows a steady synthetic pattern without any real audio playing.

---

## Phase 2 — Beat detection

**Goal:** Detect beats from the captured audio and surface them in the UI (beat-ring flash + a live BPM number). Still no device commands.

### 2.1 Scaffold the beat detection feature folder
- Create `src/HismithController/Features/BeatDetection/`:
  - `IBeatDetector` — consumes mono float frames, raises `BeatDetected` (timestamp + confidence) and exposes a `CurrentBpm` smoothed estimate.
  - `BeatEventArgs` — carries beat timestamp and confidence.

### 2.2 Implement `SpectralFluxBeatDetector`
- Use the parameters already documented in [CLAUDE.md](CLAUDE.md) §"Beat Detection":
  - FFT size 512, hop 256 (~5.8 ms hop at 44.1 kHz).
  - Positive spectral flux summed over **low-frequency bins** (kick/bass range, roughly 0–250 Hz).
  - Adaptive threshold: mean of the last 40 flux values × `OnsetMultiplier` (default 1.5, read from `AppSettings`).
  - Min inter-onset interval: 200 ms (caps detection at 300 BPM).
- The detector runs synchronously inside the audio service's `SamplesAvailable` handler — it must stay under **5 ms per frame** (this is non-negotiable; the NAudio capture thread will glitch otherwise). Measure with `Stopwatch` in debug builds.
- Use `MathNet.Numerics`' `Fourier.ForwardReal()` for the FFT.

### 2.3 BPM estimation
The estimator has to satisfy two competing requirements:
- **Wide range:** support 15–240 BPM. At 15 BPM, beats are 4 seconds apart — any time-based history window large enough to be statistically meaningful at 15 BPM would be far too laggy at higher tempos.
- **Fast response to changes:** a metronome that switches tempo every 5 seconds must be tracked. An 8-second median window cannot do this — by construction it averages across the change.

Approach: **beat-count window, not time window, plus explicit change detection.**

- Add a `BpmEstimator` that maintains a ring buffer of the **last K inter-beat intervals (IBIs)**, not a time-bounded buffer. Start with `K = 4`. This naturally adapts the latency to the tempo: at 120 BPM the window covers 2 seconds, at 60 BPM it covers 4 seconds, at 240 BPM it covers 1 second.
- Compute the **median** of the K IBIs (median, not mean — robust to a single mis-detected beat without needing a percentile trim, which doesn't behave well at K=4).
- **Change detection** layered on top:
  - When a new IBI arrives, compare it to the current running median. If the deviation exceeds ~25% (tunable), do **not** smooth — treat it as a candidate tempo change.
  - If the **next IBI** confirms the new tempo (within ~15% of the candidate), flush the ring buffer to just the two confirming IBIs and report the new BPM immediately. This caps tempo-change latency at **2 beats** at any tempo (≈1 sec at 120 BPM, ≈8 sec at 15 BPM — but 8 sec at 15 BPM is unavoidable; that's literally two beats of the source signal).
  - If the next IBI does not confirm, treat the deviant beat as noise and keep the existing estimate.
- **Output smoothing:** apply a light EMA (`α ≈ 0.5`) **only** in the stable regime — once `BeatsSinceLastChange >= 3`. During and immediately after a tempo change, emit the raw median so the UI/device follows the change without an artificial lag tail.
- Expose `CurrentBpm` (0 when fewer than 2 IBIs collected, otherwise a stable int **15–240**) and a `Confidence` (0–1) value that drops during a change candidate and recovers as beats confirm — useful later for the mapper's ramp aggressiveness.

**Why this works for the metronome-changes-every-5-seconds case:** at a typical metronome BPM (say 60–180), 5 seconds is 5–15 beats. The two-beat confirmation rule means the new tempo is locked in after ~2 beats (≈0.7–2 sec), leaving 3+ seconds at the correct BPM before the next change. The estimator never has to "average through" the transition.

**Tuning levers** (all live in `AppSettings`, not hardcoded): `K` (window size), deviation threshold for change detection (default 0.25), confirmation tolerance (default 0.15), EMA α (default 0.5).

### 2.4 Extend `SoundModeViewModel`
- Subscribe to `BeatDetected` and `CurrentBpm` changes.
- New properties:
  - `LiveBpm` (int) — the music BPM displayed in the "Music" stat.
  - `BeatTick` (bool) — flips briefly true on each beat so the UI can flash. Use a `DispatcherTimer` of ~120 ms to reset it.
- The `IsPlaying = false` (paused) state gates the `BeatTick` flashes but **does not stop** the capture or detector — the visualizer keeps moving (per design §6.6).

### 2.5 Extend `SoundModeView.xaml`
- Add the **beat ring overlay** (`viz-beat-ring` in the design): a centered ring that scales/opacity-pulses when `BeatTick` flips. Use a `Storyboard` triggered by a `DataTrigger` on `BeatTick`, or a behavior that animates on each event.
- Add the **Live stats bar** stub with just the "Music" stat populated (Device/Speed columns can show `—` for now).
- Add the Play/Pause button bound to `IsPlaying` (no device wiring yet — just the bool and the visual "Detection paused" badge).

### 2.6 Verify Phase 2
- Play tracks with known tempos (e.g. a 120 BPM electronic track, a 90 BPM hip-hop track). The displayed BPM should settle within ~3–5 seconds and stay within ±2 BPM of truth.
- The beat ring should flash visibly on kicks.
- Pause/resume should freeze the BPM and beat ring but keep bars moving.
- Hot-swap tracks (different BPM) — the estimate should re-converge within a few seconds.
- **Low-tempo coverage:** play a 20–30 BPM source (slow metronome or generated test signal). The estimator should report a value rather than clamping to 40 or stalling at 0.
- **Tempo-change response (the metronome use case):** use a metronome app or test fixture that switches BPM every 5 seconds across a sweep (e.g. 60 → 120 → 90 → 180). The reported BPM should re-lock within ~2 beats of each change; verify by logging `(timestamp, detectedBpm)` and confirming the transitions are sharp rather than gradual. This test should also live as a `BpmEstimator` unit test driven by synthetic beat timestamps.

---

## Phase 3 — Drive the device from detected beats

**Goal:** When playing, the detected beats translate to BPM commands sent to the connected Hismith.

### 3.1 Beat-to-BPM mapping
- Add a `BeatToDeviceMapper` class (in `Features/Audio/` or a new `Features/SoundMode/` folder) that:
  - Takes `musicBpm` (from `BpmEstimator`) and emits `deviceBpm` continuously.
  - For Phase 3, with no rhythm divider and no cap, `deviceBpm = clamp(musicBpm, 0, device.MaxBpm)` — **applied directly, no ramping.**
- **Do not** add the Manual-mode-style constant-rate ramp here. Sound Mode is supposed to track the music; the whole point of the work in §2.3 is to make `musicBpm` itself a clean, fast-tracking signal. A Manual-mode ramp at ~83 BPM/s would noticeably lag every tempo change (e.g. 80 → 160 BPM would take ~1 second to catch up), which defeats the feature. The `BpmEstimator`'s two-beat change confirmation already prevents spurious single-beat jumps from reaching the mapper.
- **If, and only if, testing reveals the device misbehaves on large legitimate jumps** (e.g. a real track transitions from 70 → 180 BPM and the motor strains audibly), add a coarse safety governor as a *separate* later step — something like "limit slew to 300 BPM/s," which is 4× faster than the Manual ramp and only kicks in on extreme jumps. Capture this as an open question for the Phase 3 verify step rather than building it pre-emptively.
- Apply the same BLE write throttling pattern as Manual mode: min 50 ms between BLE writes, send on change or every 50 ms.

### 3.2 Wire `SoundModeViewModel` to `IConnectedDeviceService`
- Inject `IConnectedDeviceService` into `SoundModeViewModel`.
- When `IsPlaying` is true and `HasAudio` is true, push the mapper's output to `device.SetTargetBpmAsync(...)`.
- When `IsPlaying` flips to false: immediately send `SetTargetBpmAsync(0)`. The visualizer + detector keep running.
- **Tab activation:** `IsPlaying` is forced to `false` every time the user switches **to** the Sound tab (see §1.4 — `InitializeAsync` already sets this). The user must explicitly press Play; no device commands fire until they do. This is a safety/comfort decision, not just an open-question resolution.
- **Tab deactivation:** when the user switches **away from** the Sound tab, Sound Mode must release the device:
  - Flip `IsPlaying = false` (this triggers the immediate `SetTargetBpmAsync(0)` above).
  - Stop the audio capture service entirely — no point burning CPU on FFTs the user can't see.
  - Hook this off `MainViewModel.ActiveMode` changing away from `"Sound"`. The cleanest seam is to give `SoundModeViewModel` a `Deactivate()` method and call it from `MainViewModel.OnActiveModeChanged` when transitioning out of Sound (mirroring how `InitializeAsync` is called when transitioning in).
- Listen to global stop (existing `EmergencyStopAsync` in MainViewModel): when invoked, the device is already stopped, but also flip `IsPlaying = false` so we don't immediately re-send a non-zero BPM on the next beat. The user has to press Play again to resume — same comfort principle as tab activation.

### 3.3 Populate the Device / Speed stats
- Bind the "Device" stat to `deviceBpm` and the "Speed" stat to `device.BpmToPercent(deviceBpm)`.
- Show `—` / `0` when `!IsPlaying || !HasAudio`.

### 3.4 Reconnect / connection-lost handling
- Reuse the same patterns as Manual mode (`OnBleStatusChanged` → `ChipState.Lost`). When the device disconnects mid-playback, flip `IsPlaying = false` and surface the existing "Connection lost" banner.

### 3.5 Verify Phase 3
- With a real Hismith connected (or `--mock` for log inspection), play a track and press Play. The device should follow the music BPM closely — track tempo changes within a couple of beats (driven by §2.3's estimator dynamics, not by any mapper-side ramp).
- **Tab activation behaviour:** switch to the Sound tab while audio is already playing. The visualizer should react immediately, but the device must stay at 0 — no commands fire until Play is pressed.
- Press Pause — the device should stop within ~50 ms.
- Press the global Stop button while playing — the device stops, `IsPlaying` becomes false, the music keeps playing but the device stays at 0. Pressing Play again resumes.
- **Tab deactivation behaviour:** switch to Manual mid-playback. The device should stop immediately, and the Sound capture service should stop (no continued CPU use on FFTs). Switching back to Sound returns to the paused state.
- **Stress test with large legitimate jumps** (metronome 60 → 180 BPM, or a track with a hard tempo cut). Observe and listen to the device: does the motor handle the immediate jump cleanly, or does it strain / make ugly noises? If the latter, add the safety governor mentioned in §3.1 (coarse slew limit) — otherwise leave the direct-apply path as-is.

---

## Phase 4 — User controls (max speed cap + thrust rhythm)

**Goal:** Expose the two user-tunable parameters from the design — Max device speed and Thrust rhythm — and apply them in the mapper.

### 4.1 Thrust rhythm selector
- Add a `ThrustRhythm` enum and `ThrustRhythmOption` view-model record:
  - `EveryBeat` (ratio 1), `EveryTwoBeats` (ratio 2), `EveryFourBeats` (ratio 4).
- `SoundModeViewModel` properties:
  - `RhythmOptions` (read-only collection) — populated once with the three options including label + description text from design §6.3.
  - `SelectedRhythm` (bound to a `RadioButton`/segmented control).
- Build the segmented control in `SoundModeView.xaml` matching the `ThrustRhythmSelector` from `modes.jsx`:
  - Three tiles, each with a small SVG-style rhythm diagram (use WPF `Path` geometries — the arches + ticks from `RhythmDiagram` translate directly).
  - The active tile gets the accent background; others are subtle outlines.
- Apply the ratio in `BeatToDeviceMapper`: `deviceBpm = musicBpm / ratio` **before** the cap is applied.

### 4.2 Max device speed cap
- `SoundModeViewModel` properties:
  - `MaxBpm` (int, default 240 — uncapped).
  - `MaxBpmPercent` (computed, for display).
  - `IsCapped` (computed: `MaxBpm < 240`).
  - `IsCapActive` (computed: true when `postRatioBpm >= MaxBpm - 0.5` — drives the "Capped" badge on the Device stat).
- UI elements in the view:
  - Slider 0–240 BPM bound to `MaxBpm`.
  - Side-by-side `"{MaxBpm} BPM · {MaxBpmPercent}%"` readout + "Capped" badge when `IsCapped`.
  - Footer labels `0 · 120 · 240 (uncapped)` exactly as in the design.
- Apply in `BeatToDeviceMapper`: `deviceBpm = min(musicBpm / ratio, MaxBpm)`. The cap operates on the **post-ratio** value (design §6.4).

### 4.3 Persistence
- Persist `SelectedRhythm` and `MaxBpm` between sessions via `AppSettings.json` (mirror the existing pattern in `Configuration/AppSettings.cs`).

### 4.4 Verify Phase 4
- 128 BPM track + Every 2 beats → Device stat shows 64 BPM (no "÷2" badge if cap not hit, but the divider should be visible in the stat — match design exactly).
- 160 BPM track + Every 2 beats + cap 60 → Device shows 60 with "Capped" badge.
- 100 BPM track + Every beat + uncapped → Music = Device = 100, no badge.
- Settings persist across a relaunch.
- Switch rhythm mid-playback — the device transitions smoothly (the ramping from §3.1 already handles this).

---

## Cross-cutting concerns

These apply across all phases — don't defer them.

- **Threading.** Audio capture runs on the NAudio worker thread; beat detection runs there too (under the 5 ms budget). All UI mutations must go through `Application.Current.Dispatcher.InvokeAsync`. BLE writes are async from the UI thread, serialized by the existing `SemaphoreSlim` in `HismithBleDeviceService`.
- **Mock support.** Every phase should remain runnable under `--mock`. `MockAudioCaptureService` (synthetic input) + `MockBleDeviceService` (log-only writes) together let us validate the end-to-end loop without any hardware.
- **No premature abstractions.** Don't add a generic "audio pipeline" framework. Concrete `WasapiLoopbackAudioCaptureService` → `SpectralFluxBeatDetector` → `BeatToDeviceMapper` is fine.
- **Design fidelity.** Match [design/modes.jsx](design/modes.jsx) layout, spacing, and component structure (visualizer → beat ring → rhythm tiles → max-speed slider → stats bar → play button). Ask before deviating.
- **Tests.** Unit-test the pure parts: `BpmEstimator` (feed synthetic timestamps), `BeatToDeviceMapper` (ratio + cap math), `SpectralFluxBeatDetector` (feed pre-recorded PCM, assert beat counts within tolerance). Skip UI testing.

---

## Open questions

Resolved decisions (kept here for reference):

- **Tab switch behaviour — resolved:** Switching away from Sound Mode stops device updates and stops the capture service (§3.2). Switching to Sound Mode always lands in the **paused** state — no device commands until the user explicitly presses Play (§1.4, §3.2). This applies whether the tab is entered for the first time, re-entered later, or re-entered after a global Stop.
- **Sensitivity control:** §6.7 of the requirements reserves space but explicitly defers it. This plan honours that — no UI surface in Phase 1–4.

Still open:

- **Beat-ring animation curve:** the design uses a CSS pulse. WPF equivalent is a `Storyboard` with `EasingFunction`. Pick `QuadraticEase` `EaseOut` at ~250 ms unless the user prefers another curve.
- **Safety governor on large BPM jumps:** to be decided empirically during §3.5 stress testing — only added if the motor strains on a 60 → 180 BPM hard cut.
