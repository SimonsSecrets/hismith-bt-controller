# UI Deviations — WPF vs. Design

A definitive checklist of every place the current WPF app diverges from the design files in
`design/` (`index.html` CSS + `app.jsx` / `connection.jsx` / `modes.jsx`). The goal is to drive
the WPF UI to a pixel-faithful match of the design, except where the user has explicitly chosen to
deviate.

**How to read this:** each item notes the design intent, the current WPF state, and the concrete
fix. Severity is a rough triage aid:
- 🔴 **Major** — whole component/screen missing or structurally wrong.
- 🟡 **Moderate** — present but visibly off (wrong shape, color, gradient, layout).
- 🟢 **Minor** — small numeric/polish gap, easy to miss.

Source of truth for colors/sizes: `design/index.html` `:root` tokens and the per-component CSS.

Issues that the user does not want to fix are marked with [SKIP]

> **Revision note:** re-synced against the updated design files. The latest design adds a full
> **Settings screen** (§13), turns the mode-bar's left button into a **Settings gear** (§6.3), adds a
> **Settings gear on the connection screens** (§5.6), and makes the theme a **Light/Dark/System**
> choice that lives in Settings (§12.1). The Settings screen, the mode-bar gear (§6.3), and the theme
> relocation (§12.1) are **tracked as one consolidated item under §13.1**. `modes.jsx` and
> `connection.jsx` are otherwise unchanged.

---

## 1. Window chrome & shell

### 1.1 🔴 [SKIP] No custom title bar
- **Design** (`app.jsx` `.titlebar`, `index.html:180`): a 44 px custom title bar (`--titlebar` `#F7ECE7`)
  containing a gradient app-logo square (18 px, rose→`#E0A07F`→gold), the "HismithController" label
  (13 px, weight 600), and three custom window buttons (minimize / maximize / close) with the close
  button turning `#E81123` red on hover.
- **WPF** (`MainWindow.xaml`): a plain `<Window>` using the default OS title bar. No custom chrome,
  no gradient logo, no styled window buttons.
- **Fix:** add `WindowChrome` (or `WindowStyle="None"` + custom bar) reproducing the design title bar.
  The `TitlebarBrush` token already exists in the theme but is unused.

### 1.2 🟡 [SKIP] Window is resizable / wrong rounding
- **Design**: fixed 540×960 portrait "window" with 12 px rounded corners and a soft outer drop shadow
  (`--shadow-window`).
- **WPF** (`MainWindow.xaml:10-11`): `Height=960 Width=540` but `MinHeight=640 MinWidth=480`, freely
  resizable, square OS corners, no app-level shadow.
- **Fix:** decide whether the app stays fixed-size like the mock or remains resizable; if matching the
  design, add rounded corners + drop shadow and lock/limit resize.

---

## 2. First-run welcome overlay

### 2.1 ✅ Welcome overlay — IMPLEMENTED
- **Design** (`app.jsx` `Welcome`, `index.html:844` `.welcome`): a modal overlay with blurred backdrop,
  gradient logo tile, "HismithController" heading, tagline "Explore, experiment, enjoy", three numbered
  step cards (Connect / Choose a mode / Play), and a single "Get started" button.
  *(Updated design: tagline changed from "Move to the beat." and the "Skip" button was removed.)*
- **Done** (`UI/Views/WelcomeView.xaml`): full-window scrim (`OverlayBackBrush` token) + centred card
  (gradient logo, heading, tagline, three step cards, "Get started" with a soft rose glow), mounted
  last in `MainWindow.xaml` so it paints over everything. The content layers behind it are blurred
  via a `BlurEffect` (design's `backdrop-filter: blur`), keyed off `IsWelcomeOpen`. Gated on
  `MainViewModel.IsWelcomeOpen`, seeded from the persisted `UserPreferences.HasSeenWelcome` first-run
  flag; "Get started" runs `DismissWelcomeCommand`, which hides the overlay and saves the flag
  (`user-settings.json`) so it never reappears.

---

## 3. Lost-connection banner

### 3.1 ✅ Connected-state lost banner — IMPLEMENTED
- **Design** (`app.jsx` `LostBanner`, `index.html:500` `.banner`): a coral banner shown **above the
  content while connected** — warning icon, "Connection lost" / "Your device has stopped. Your settings
  are preserved.", with Dismiss + Reconnect buttons.
- **Done** (`ConnectedView.xaml`): a coral banner (`CoralTintBrush` bg, `#ECC1B5` border, warn-triangle
  in a white circle, the two design text lines) added as a new top row above the mode bar, shown via
  `MainViewModel.IsConnectionLost` (set in `OnBleStatusChanged` when the BLE link drops while
  connected; both modes are force-stopped there too). **Deviation:** the **Dismiss button is omitted**
  per the user — the only action is **Reconnect** (`ReconnectCommand`), which reuses the disconnect/reset
  path to return to the pre-connect scan screen (rather than the design's in-place re-scan).

---

## 4. Toast notifications

### 4.1 🟢 [SKIP] Toast not implemented
- **Design** (`app.jsx` `toast`, `index.html:870` `.toast`): a pill toast at the bottom center (e.g.
  "Would open Windows Settings") with a Dismiss action.
- **WPF**: not implemented. Low priority — only used by the `failAdapter` path in the mock.

---

## 5. Connection screens (`ConnectionView.xaml`)

### 5.1 ✅ "Bluetooth unavailable" (no-adapter) screen — IMPLEMENTED
- **Design** (`app.jsx` `failAdapter`): a FailCard titled "Bluetooth unavailable" with "Open Settings".
- **Done**: added `ConnectionPhase.BluetoothUnavailable` + a coral-warn FailCard in `ConnectionView.xaml`.
  `IDeviceDiscoveryService` gained an `AdapterUnavailable` event, raised by `BleDeviceDiscoveryService`
  when the watcher stops with `RadioNotAvailable`; the VM switches to the new phase. "Open Settings"
  runs `OpenBluetoothSettingsCommand` → `ms-settings:bluetooth` (the §4.1 toast is SKIP). The Settings
  gear FAB stays visible on this screen.

### 5.2 ✅ Selected device icon turns white — IMPLEMENTED
- **Design** (`connection.jsx` `.device.selected .icon`): on selection the icon box fills rose and the
  Bluetooth glyph turns **white**.
- **Done** (`ConnectionView.xaml`): named the device-row glyph `BtGlyph` and the selection `DataTrigger`
  now sets its `Stroke` to white, so it reads against the rose-filled icon box.

### 5.3 ✅ Connecting stepper done/green state — IMPLEMENTED
- **Design** (`connection.jsx` `ConnectingCard`, `.step.done`): each step goes inactive → **active (rose)**
  → **done (sage/green with a check icon)**.
- **Done**: new `StepStateConverter` maps `ConnectStep` to mutually-exclusive `Inactive`/`Active`/`Done`
  per step; the XAML adds a sage circle + white check `Path` and sage label for `Done`. The VM sets
  `ConnectStep = 3` after identification so both steps show green checks during the "Identified:" window.

### 5.4 ✅ Spinner arc style — IMPLEMENTED
- **Design** (`.spinner`): a smooth ring, `rose-soft` track with a solid rose top arc, spinning.
- **Done** (`ConnectionView.xaml`): replaced the dashed ellipse with a static `RoseSoftBrush` track ring
  plus a rotating rose 90° arc `Path` overlay (the 0.9s spin storyboard now drives only the arc).

### 5.5 🟢 [SKIP] Card width
- **Design**: `.connect-card` `max-width: 520px` (content cap 480).
- **WPF** (`ConnectionView.xaml:116`): hard `Width=480 MaxWidth=480`.
- **Fix:** confirm intended max width (likely fine, but the design card is up to 520).

### 5.6 ✅ Settings gear on connection screens — IMPLEMENTED
- **Design** (`app.jsx` `.settings-fab`, `index.html:944`): a 38 px round gear button pinned top-right
  (`top:18 right:18`) inside the card frame on the pre-connect, devices-found, empty, and fail screens
  (hidden during scanning/connecting). Opens the Settings screen (§13).
- **Done** (`ConnectionView.xaml`): top-right 38 px gear FAB added as the last child of the centred
  card frame, hidden via `Phase` DataTriggers during Scanning/Connecting, wired to the window-level
  `OpenSettingsCommand`. (Done together with §13.1.)

---

## 6. Mode bar (`ConnectedView.xaml`)

### 6.1 ✅ Mode tab icons — IMPLEMENTED
- **Design** (`app.jsx` `ModeStrip`): Manual tab has a gear/settings icon, Sound tab has a music-note
  icon (13 px, before the label).
- **Done** (`ConnectedView.xaml`): each `ModeTab` RadioButton now wraps a horizontal `StackPanel` with a
  14 px leading vector `Path` (gear for Manual, music-note for Sound) + label. The icon `Stroke` binds to
  the tab's `Foreground` via `AncestorType=RadioButton`, so it recolours to white alongside the label when
  the tab is active (same pattern as the Settings `SegButton`).

### 6.2 🟡 [SKIP] Missing disabled "+" (more modes) tab
- **Design** (`app.jsx` `ModeStrip`): a third disabled tab with a "+" icon, tooltip "More modes coming
  soon".
- **WPF**: only two tabs; no "+" placeholder.
- **Fix:** add a disabled third tab with a plus glyph.

### 6.3 ✅ Mode-bar left button is now a Settings gear — IMPLEMENTED
- The design's mode-bar left slot is now a **Settings gear** (not a theme toggle), and theme switching
  has moved into the Settings screen. **Done as part of §13.1 (part B):** `ConnectedView.xaml`'s left
  slot is a gear bound to `OpenSettingsCommand`; the old sun/moon `ToggleThemeCommand` button is gone.

---

## 7. Sound mode visualizer (`SoundModeView.xaml`)

### 7.1 🔴 [SKIP] Missing "Listening to system audio" pill
- **Design** (`modes.jsx` `.viz-listening`): a top-left pill with a blinking live-dot and the text
  "Listening to system audio" (or "No audio"), on a translucent blurred background.
- **WPF** (`SoundModeView.xaml`): no such pill. (The `SoundModeViewModel` even references a "live-dot
  DataTrigger in the view" at line ~98, but no element consumes it.)
- **Fix:** add the top-left listening pill + blinking/beat live-dot.

### 7.2 ✅ Visualizer height — IMPLEMENTED
- **Design** (`.visualizer.compact`): 140 px tall.
- **Done** (`SoundModeView.xaml`): visualizer `Height=140`. `BinToHeightConverter.BarMaxHeight` was
  coupled to the old 90 px and bumped to 140 too, so the bars fill the full height.

### 7.3 ✅ Visualizer background gradient — IMPLEMENTED
- **Design** (`.visualizer`): vertical gradient `viz-bg-top` (`#FBEFF3`) → `viz-bg-bot` (`#FFF7F0`).
- **Done**: added `VizBgTopColor`/`VizBgBotColor` + a `VizBackgroundBrush` `LinearGradientBrush` to
  both `LightTheme.xaml` and `DarkTheme.xaml` (dark: `#2A1B23`→`#1A1218`); the visualizer Border now
  uses `VizBackgroundBrush` so it follows the theme.

### 7.4 ✅ Visualizer corner radius — IMPLEMENTED
- **Design**: 14 px.
- **Done** (`SoundModeView.xaml`): visualizer `CornerRadius=14`; `ClipToBounds` already clips the
  inner overlays to the new radius.

### 7.5 ✅ Spectrum bars: gradient + corner radius — IMPLEMENTED
- **Design** (`.viz-bars i`): each bar is a vertical gradient rose→`#E0A07F`, `border-radius: 6px`.
- **Done** (`SoundModeView.xaml`): bars use a vertical `RoseColor`→`#E0A07F` `LinearGradientBrush`;
  `CornerRadius=3` (a practical match — 6 px renders as a full pill on a ~2 px-wide bar).

### 7.6 ✅ Beat indicator = full-bleed border flash — IMPLEMENTED
- **Design** (`.viz-beat-ring`): the beat pulse is a **rounded-rectangle border flashing around the
  entire visualizer** (`inset:0`, 2 px rose, 14 px radius), opacity 0.85→0 over 360 ms.
- **Done**: replaced the centered 68 px `Ellipse` with a full-bleed `Border x:Name="BeatRingElement"`
  (2 px rose, 14 px radius); the code-behind `OnBeatPulse` now animates only opacity 0.85→0 over
  360 ms (the scale animation was dropped).

### 7.7 ✅ Idle bobbing dots — IMPLEMENTED
- **Design** (`.viz-idle .idle-rings`): three bobbing dots above "Play some music to get started".
- **Done** (`SoundModeView.xaml`): three 12 px dots above the prompt, each looping translateY 0→-4 +
  opacity 0.5→1 over 1.4 s (0.7 s + AutoReverse), staggered 0/0.2/0.4 s with a SineEase, started on
  `Loaded`. **Deviation:** fill is the stronger `RoseBrush`, not the design's near-invisible
  `rose-tint`, for contrast against the solid soft-surface idle background (user-approved).

---

## 8. Sound mode stats bar (`SoundModeView.xaml`)

### 8.1 🟡 [SKIP] Missing dividers between stats
- **Design** (`.stats-bar .stat + .stat`): a 1 px left border divides Music | Device | Speed.
- **WPF** (`SoundModeView.xaml:291`): `UniformGrid` columns with no dividers.
- **Fix:** add vertical 1 px separators between the three stat columns.

### 8.2 ✅ Stats bar corner radius — IMPLEMENTED
- **Design**: `border-radius: 12px`.
- **Done** (`SoundModeView.xaml`): stats bar `CornerRadius=12`.

### 8.3 ✅ Stat value font size — IMPLEMENTED
- **Design** (`.stat .v`): 18 px.
- **Done** (`SoundModeView.xaml`): the four stat values (Music / dash / Device / Speed) are `FontSize=18`.
  Also matched the design's stat **keys** (`.stat .k` 10 px + `letter-spacing: 0.08em`): keys bumped
  9→10 px, and since WPF `TextBlock` has no letter-spacing, hair spaces (`&#x200A;`) are inserted
  between the MUSIC/DEVICE/SPEED letters to fake the tracking (user-approved).

---

## 9. Sliders

### 9.1 🟡 [SKIP] No "muted/capped" slider variant in Sound mode
- **Design** (`.slider.muted`): when capped, the filled portion uses a muted `#D8C0C8` instead of rose.
- **WPF** (`SoundModeView.xaml:520-524`): max-speed slider always uses `ManualSpeedSlider` (rose fill),
  even when `IsCapped`.
- **Fix:** add a muted slider style and swap to it when capped.

---

## 10. Rhythm tiles (`SoundModeView.xaml`)

### 10.1 🟢 [SKIP] Active tile missing the focus-ring glow
- **Design** (`.rhythm-tile.active`): active tile gets `box-shadow: 0 0 0 3px rose-soft, 0 2px 10px
  rgba(196,118,138,0.18)` (a soft outer ring + lift).
- **WPF** (`SoundModeView.xaml`): active state sets border + background + colors but **no shadow
  ring/glow**.
- **Note:** a sibling-border glow was implemented and then **reverted at the user's request** — the
  user did not like the glow ring on the active tile. Intentional deviation; leave as-is.

---

## 11. Footer (`ConnectedView.xaml`)

### 11.1 ✅ Stop button drop shadow — IMPLEMENTED
- **Design** (`.btn.stop`): `box-shadow: 0 4px 14px rgba(0,0,0,0.28)` (and a sage-tinted shadow when
  flashed).
- **Done** (`SharedStyles.xaml` `StopButton`): added two empty blurred sibling Borders carrying the
  shadow (WPF shadow rule, so the label/icon stay sharp) — a black halo (`ShadowLayer`, ShadowDepth 4 /
  Direction 270 / BlurRadius 14 / Opacity 0.28, matching the CSS `0 4px 14px rgba(0,0,0,0.28)`) and a
  sage halo (`SageShadowLayer`, `#7FA688` @ 0.4). The flash storyboards crossfade black→sage in/out
  alongside the existing `IsStopFlashing` `FlashBg` animation, and the disabled trigger hides the shadow
  (`box-shadow: none`).

---

## 12. Theming

### 12.1 ✅ Theme: Light/Dark/System selector + persistence — IMPLEMENTED
- The 3-way **Light / Dark / System** theme control now lives on the Settings screen, and the choice
  persists between sessions. **Done as part of §13.1 (part C):** `App.ApplyThemePreference` swaps the
  theme dictionary, `System` resolves via `UISettings` and follows the OS live through
  `ColorValuesChanged`, and `UserPreferences.Theme` is saved/loaded in `user-settings.json`.

### 12.2 🟢 [SKIP] Card shadow fidelity
- **Design** (`--shadow-card`): a layered shadow (`0 1px 2px …04`, `0 8px 28px …06`).
- **WPF**: single `DropShadowEffect` with `CardShadowOpacity=0.1`. Approximation only — revisit if cards
  look too heavy/light vs. the mock.

---

## 13. Settings screen & theme relocation (NEW in latest design)

### 13.1 ✅ Settings feature — IMPLEMENTED (consolidated)
> **Single tracked item.** This bundles the new Settings screen with the two changes that only make
> sense alongside it: the mode-bar gear button (was §6.3) and the relocation of theme switching into
> Settings (was §12.1). Ship them together. The connection-screen Settings gear (§5.6) is the same
> entry point and is best done in the same pass.
>
> **Done (all parts A/B/C + §5.6):** `UI/Views/SettingsView.xaml` (+ `SettingsViewModel`) is shown
> as an app-level overlay in `MainWindow.xaml` (visible on `MainViewModel.IsSettingsOpen`), covering
> both connection and connected content and the footer. The connected mode-bar's left slot is now a
> Settings gear (`ConnectedView.xaml`, `OpenSettingsCommand`); the connection screens have the
> top-right gear FAB (`ConnectionView.xaml`, hidden during Scanning/Connecting). Theme is a
> Light/Dark/System segmented control wired to `App.ApplyThemePreference` with live OS-follow for
> System (`UISettings.ColorValuesChanged`) and persisted in `user-settings.json` via
> `UserPreferences.Theme`. The data folder is user-editable (`Configuration/AppDataPaths.cs`, fixed
> `%LOCALAPPDATA%` pointer file + migrate-and-restart on change); "Open in Explorer" opens it.
> Version reads from the assembly (`<Version>0.1.0</Version>`).
>
> Minor design deviation: the segmented control's active-pill glow is omitted (a `DropShadowEffect`
> on a text-bearing element blurs the text — see the WPF shadow rule in CLAUDE.md).

**Part A — The Settings screen itself**
- **Design** (`settings.jsx` `SettingsScreen`, `index.html:956`): a full-window view that **replaces
  the mode content** while open (opened from the gear in the mode bar / connection FAB; the footer stop
  bar hides while it's shown). Structure:
  - **Header** (`.settings-head`) — a back-arrow `icon-btn` (34 px, 9 px radius) + "Settings" title
    (18 px, weight 600), with a 1 px bottom border.
  - **Appearance** (`.set-group` → `.set-card`) — a "Theme" row: title (`.rk`) + subtitle (`.rsub`
    "Follow the system setting or pick a fixed look.") on the left, the Light/Dark/System segmented
    control (`.seg`, rose active pill) on the right.
  - **Application data** — a stacked "Data folder" row: title + subtitle, a monospace path field
    (`.path-field` — folder icon + ellipsized path on `surface-soft`), then two actions: **"Change…"**
    (ghost `.btn.sm`) and **"Open in Explorer"** (subtle `.btn.sm`, external-link icon).
  - **About** — three rows: Version (`1.4.2`), Author (`SimonsSecrets`), Contact (mailto link in
    rose-dark).
  - **Support** (`.kofi-card`) — heading "Enjoying HismithController?", a blurb, and a full-width
    **"Support me on Ko-fi"** button (`#FF5E5B`, coffee icon, 46 px, 9 px radius).
- **WPF**: no Settings screen, `SettingsView`, or settings view-model exists.

**Part B — Entry points (was §6.3 + §5.6)**
- **Design**: the connected mode-bar's **left slot is a Settings gear** (`theme-toggle inline` pill,
  `settings` icon at 17 px); the connection screens get a top-right gear FAB (§5.6). Both open the
  Settings screen.
- **WPF** (`ConnectedView.xaml:79-130`): the left slot is still a sun/moon **theme toggle** that flips
  the theme in place; connection screens have no gear at all.

**Part C — Theme control moves into Settings (was §12.1)**
- **Design** (`ThemeSeg`, `index.html:84` dark palette + `.seg`): theme is chosen via the **Light /
  Dark / System** segmented control on the Settings screen; `System` follows the OS preference live.
- **WPF**: `DarkTheme.xaml` exists but `App.xaml` only merges `LightTheme.xaml`; no Light/Dark/System
  selector, no `System` (follow-OS) support, and the choice is not persisted between sessions.

**Fix (all parts):** build a `SettingsView` shown in place of the mode content; swap the mode-bar
theme toggle for a gear that opens it and add the connection-screen gear FAB; move theme selection into
the Settings segmented control (wire runtime `DarkTheme.xaml` swap, implement `System`, persist the
choice). Needs several **new vector icons** not yet in the WPF app: `arrowLeft`, `folder`, `external`,
`monitor`, `coffee` (the gear `settings` icon already exists in `connection.jsx` paths). The "Open in
Explorer" action and theme persistence both map to existing `OpenTasks.md` items.

---

## Quick index (checklist)

- [ ] 1.1 Custom title bar (logo + window buttons) — 🔴 [SKIP]
- [ ] 1.2 Fixed size + rounded corners + window shadow — 🟡 [SKIP]
- [x] 2.1 First-run welcome overlay (new tagline, no Skip button) — ✅
- [x] 3.1 Lost-connection banner (connected state) — ✅ (Reconnect-only; Dismiss omitted per user)
- [ ] 4.1 Toast notifications — 🟢 [SKIP]
- [x] 5.1 "Bluetooth unavailable" screen — ✅
- [x] 5.2 Selected device icon turns white — ✅
- [x] 5.3 Stepper done/green state — ✅
- [x] 5.4 Spinner arc style — ✅
- [ ] 5.5 Connect-card max width — 🟢 [SKIP]
- [x] 5.6 Settings gear on connection screens — ✅
- [x] 6.1 Mode tab icons — ✅
- [ ] 6.2 Disabled "+" mode tab — 🟡 [SKIP]
- (6.3 folded into 13.1 — mode-bar gear button)
- [ ] 7.1 "Listening to system audio" pill — 🔴 [SKIP]
- [x] 7.2 Visualizer height 140 — ✅
- [x] 7.3 Visualizer background gradient — ✅
- [x] 7.4 Visualizer corner radius 14 — ✅
- [x] 7.5 Spectrum bar gradient + radius — ✅
- [x] 7.6 Beat ring = full-bleed border flash — ✅
- [x] 7.7 Idle bobbing dots — ✅
- [ ] 8.1 Stat dividers — 🟡 [SKIP]
- [x] 8.2 Stats bar radius 12 — ✅
- [x] 8.3 Stat value 18 px (+ key 10 px / tracking) — ✅
- [ ] 9.1 Muted/capped slider variant — 🟡 [SKIP]
- [ ] 10.1 Active rhythm tile glow — 🟢 [SKIP] (implemented then reverted per user)
- [x] 11.1 Stop button shadow — ✅
- (12.1 folded into 13.1 — theme Light/Dark/System selector + persist)
- [ ] 12.2 Card shadow fidelity — 🟢 [SKIP]
- [x] 13.1 Settings feature — screen (Appearance / Data folder / About / Ko-fi) + mode-bar gear (6.3)
      + theme selector relocation & persistence (12.1); pairs with 5.6 — ✅
