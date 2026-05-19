# HismithController — UI Design Requirements
**Platform:** Windows WPF desktop application  
**Target user:** General consumer — expects a polished, guided experience with clear affordances and minimal jargon  
**Visual direction:** Clean and elegant with a warm feminine aesthetic — soft blush/rose tones, rounded forms, refined typography, smooth animations. Both a light and a dark theme are required; the dark theme preserves the same warmth using deep plum-toned darks rather than cold greys.

---

## 1. Visual Design Language

### Light Theme Palette
- **Background:** Warm white or very light blush (e.g. `#FDF8F6` or similar off-white with a warm tint)
- **Surface/card:** Pure white with a subtle drop shadow — used for panels and content cards
- **Primary accent:** Dusty rose / mauve (e.g. `#C4768A` range) — used for primary buttons, active states, highlights
- **Secondary accent:** Warm champagne/gold (e.g. `#D4A96A` range) — used sparingly for premium details
- **Text primary:** Warm near-black (`#2A1F1F` or similar)
- **Text secondary:** Medium warm grey
- **Success:** Soft sage green
- **Error/warning:** Muted coral (avoid harsh red — keep it on-palette)
- **Disabled:** Light warm grey, never harsh

### Dark Theme Palette
The dark theme should feel intimate and refined — not the cold blue-grey of a typical "developer dark mode." The key is to use deep plum-brown tones as the neutral base, which keeps the rose accent colours harmonious rather than clashing.

- **Background:** Deep plum-charcoal (e.g. `#1A1218`) — dark with a clear warm/purple undertone; never pure black or cold grey
- **Surface/card:** One step lighter than the background (e.g. `#251C23`) — enough contrast to distinguish cards without a harsh border; drop shadows become subtle inner glows or very soft elevation
- **Surface raised** *(for popovers, dropdowns, tooltips)*: A further step lighter (e.g. `#312430`) — creates a clear three-level depth hierarchy
- **Primary accent:** Slightly brightened dusty rose (e.g. `#D98FA0`) — a touch lighter and more saturated than the light-mode value so it reads clearly against dark backgrounds while remaining in the same rose family
- **Secondary accent:** Warm champagne/gold carries forward well on dark (e.g. `#E0BB7A`) — slightly warmer and brighter than the light-mode value
- **Text primary:** Warm off-white (e.g. `#F5EDE8`) — never pure white; the warm tint keeps it in the palette
- **Text secondary:** Muted rose-grey (e.g. `#A08890`) — readable but clearly subordinate
- **Success:** Muted sage, slightly lighter than the light-mode value so it reads on dark surfaces
- **Error/warning:** Soft coral, same family as light mode — slightly more saturated to be legible on dark backgrounds
- **Disabled:** Deep muted plum-grey — present but clearly inactive

### Theme Switching
- The app should default to the user's Windows system theme (light or dark) on first launch
- A manual **theme toggle** (sun / moon icon) is placed in the app header, alongside the device status chip
- Switching themes animates with a gentle crossfade (200–300ms) — no hard flash
- The selected theme preference is persisted between sessions
- All components, illustrations, and icons must be designed for both themes; no element should be designed only once and assumed to work on both backgrounds

### Typography
- **Font family:** Segoe UI (system font, WPF native) — clean and readable at all sizes
- **Hierarchy:**
  - Section headers: 14–16px, semi-bold
  - Labels: 12–13px, regular
  - Values/numbers: 20–28px, light weight — large numbers should feel airy, not heavy
  - Body/log text: 11–12px, regular

### Shape & Depth
- Rounded corners throughout — cards at 10–12px radius, buttons at 6–8px, sliders and inputs pill-shaped where appropriate
- Subtle card shadows (soft, spread, low opacity) — no harsh drop shadows
- No sharp edges anywhere in the UI

### Motion
- All state transitions should animate smoothly (150–250ms, ease-out)
- Slider thumb movement: fluid, no snapping
- Mode tab switching: fade or gentle slide transition
- Scanning state: soft pulsing/breathing animation (not a harsh spinner)
- Beat indicator in Sound mode: smooth flash, not a hard blink

### Icons
- Thin-line icon style (e.g. Segoe Fluent Icons or a matching thin-line set)
- Consistent stroke weight throughout
- No filled/solid icons except for the primary Stop action

---

## 2. Application Layout

The app is a **single resizable window** — no multi-window MDI pattern. The window should have a reasonable minimum size (e.g. 480×640px) and scale gracefully up to 2560×1440px (1440p).

### Layout Zones

```
┌─────────────────────────────────────────┐
│  Header: Logo · App name · Device status │
├─────────────────────────────────────────┤
│                                         │
│         Main content area               │
│   (Connection setup OR Mode content)    │
│                                         │
├─────────────────────────────────────────┤
│         Global stop button              │
└─────────────────────────────────────────┘
```

**Header** — always visible. Contains app identity and a compact device connection status chip.

**Main content area** — context-sensitive. Shows the Connection Setup flow when not connected; shows the Mode selector and active mode panel when connected.

**Footer bar** — always visible when a device is connected. Contains the emergency Stop button, centred or left-anchored.

---

## 3. Connection Setup

### 3.1 Pre-Connection State (Entry Point)
When the app launches and no device is paired, the main content area shows a **centered connection card**. This is the only thing the user can interact with — modes are not accessible until a device is connected.

**Card contents:**
- A small illustrative icon or graphic representing a device/Bluetooth
- Heading: *"Connect your Hismith"*
- Short descriptor line: *"Scan for nearby devices to get started"*
- Primary button: **"Scan for devices"**

### 3.2 Scanning State
Triggered by pressing Scan.

- The button transforms into a **scanning indicator** — a subtle pulsing animation with label *"Scanning…"* and a Cancel option
- A list area below the button begins to populate with discovered devices as they are found (real-time, not waiting for scan to complete)
- Each list item shows:
  - Device name (e.g. `HISMITH`)
  - A small Bluetooth signal strength indicator (1–3 bars)
  - A subtle "tap to select" affordance

**Empty state:** If no devices are found after the scan completes, show: *"No devices found. Make sure your Hismith is powered on and nearby."* with a **"Try again"** button.

### 3.3 Device Selected
When the user taps a device in the list:
- It becomes highlighted (accent background, checkmark)
- A **"Connect"** primary button appears below the list (or activates if already shown)
- Other items in the list remain visible but dimmed

### 3.4 Connecting & Validating
Triggered by pressing Connect.

- The card enters a **connecting state**: spinner/animation + label *"Connecting to [device name]…"*
- Sub-step indicator: two sequential steps shown in small text beneath:
  1. *"Establishing connection"* → *"Verifying device compatibility"*
- This step involves sending an identifier command to confirm the correct Hismith model

**Validation failure:** If the device responds but is not a recognised Hismith model:
> *"This device doesn't appear to be a compatible Hismith. Please choose a different device."*  
> Button: **"Go back"** (returns to device list)

**Connection failure (BLE error):**
> *"Couldn't connect to [device name]. Make sure the device is charged and within range."*  
> Button: **"Try again"**

### 3.5 Connected State
On successful connection and validation:
- The connection card animates out and the **Mode selector** animates in
- The header's device status chip updates to: a soft green dot + *"[Device name] connected"*
- The footer Stop button becomes active

### 3.6 Header Device Status Chip
A small persistent pill in the top-right of the header:

| State | Appearance |
|---|---|
| Disconnected | Grey dot + *"No device"* |
| Scanning | Pulsing blue dot + *"Scanning…"* |
| Connecting | Amber dot + *"Connecting…"* |
| Connected | Green dot + *"HISMITH"* |
| Disconnected mid-session | Coral dot + *"Lost connection"* |

Clicking the chip when connected opens a small **connection popover** with: device name, signal strength, and a **"Disconnect"** option.

### 3.7 Connection Lost Mid-Session
If the BLE connection drops while in a mode:
- A **non-blocking banner** slides down at the top of the main area: *"Connection lost. The device has stopped."*
- Action buttons on the banner: **"Reconnect"** | **"Dismiss"**
- The active mode controls are dimmed/disabled but remain visible (the user's settings are preserved)
- The device is automatically stopped (safety: no lingering commands)

---

## 4. Mode Navigation

Once connected, the main content area shows the **Mode selector** at the top, followed by the active mode's content panel.

### Mode Selector
A **segmented control / pill-style tab strip**:

```
  [ Manual ]  [ Sound ]  [ + ]
```

- Active mode has a filled accent-colour pill background; inactive modes are text-only
- The `+` placeholder is greyed out, non-interactive, with a tooltip: *"More modes coming soon"* — this signals extensibility without being confusing
- Switching modes transitions the content panel with a gentle fade

**Locked state (not connected):** The entire mode selector is shown but dimmed, with a message below: *"Connect a device to get started"*

---

## 5. Manual Mode

Manual mode allows the user to directly set the device's speed.

### Layout
A single content card occupying the main area, with:

1. **Speed control** (hero element) — slider + dual unit display
2. **Presets row**
3. **Power toggle**

### 5.1 Speed Control
The speed control is the hero element of this mode. Speed can be expressed in two interchangeable units — **percentage (0–100%)** and **BPM (0–240 BPM)** — and both representations are always visible and editable simultaneously.

**Slider**
- A large horizontal (or vertical) slider spanning the full width of the card
- The slider thumb is a rounded pill or circle, slightly oversized for easy dragging
- The underlying value is always a percentage (0–100), with BPM derived as `BPM = speed% / 100 × 240`

**Dual readout**
Two numeric input fields sit prominently above (or beside) the slider, side by side:

```
  ┌──────────┐     ┌──────────┐
  │   64%    │     │  154 BPM │
  └──────────┘     └──────────┘
        Speed             Tempo
```

- Either field can be edited directly by the user (clicking and typing a number)
- Changing one field immediately updates the other field and the slider position
- Both values are rounded to the nearest integer — no decimal places displayed or accepted
- Both fields share the same large, airy font weight

**Speed transitions (smooth ramping)**
Speed changes must never be applied instantly. When the target speed changes — whether by dragging the slider, typing a value, or tapping a preset — the device speed ramps smoothly to the new target over a short transition period (suggested default: ~0.5–1.5 seconds, proportional to the size of the change).

To communicate this to the user, the UI must visually distinguish between **target speed** (where the user set the slider) and **current speed** (what the device is actually running at):
- The slider and both numeric input fields reflect the **target** at all times
- A secondary smaller indicator beneath the readouts shows the **live device speed** during the ramp: e.g. a small label *"Device: 42%"* or a thin progress arc that fills toward the target
- Once the ramp completes, the secondary indicator disappears or matches the target value
- A subtle warm pulse on the readout confirms when the target has been reached

The slider sends updated target values with debounce while dragging (e.g. every 80–100ms), and on release. The ramp logic runs on the device command side — the UI simply reflects the ramp state.

### 5.2 Preset Buttons
A row of 4 quick-preset chips below the slider, each showing both units:

```
  [ Slow · 25% · 60 BPM ]  [ Medium · 50% · 120 BPM ]  [ Fast · 75% · 180 BPM ]  [ Max · 100% · 240 BPM ]
```

- Compact, pill-shaped chips
- Tapping a preset sets the target speed to that value and triggers the smooth ramp to the new speed

### 5.3 Power Toggle
- A **toggle switch** labelled *"Device power"*
- Off state: sends Power Off command (`AA 02 00 02`)
- On state: sends Power On command (`AA 01 00 01`)
- Default to On when mode is entered (device was just connected)
- When toggled off, the speed slider, readouts, and presets dim to indicate the device is inactive

---

## 6. Sound Mode

Sound mode detects beats from system audio and translates them to velocity commands.

Speed in Sound mode maps from 0 up to a user-configurable maximum. The minimum is always 0 — there is no adjustable floor.

### Layout
A content card with:

1. **Audio visualizer** (top section)
2. **Beat indicator**
3. **Thrust rhythm selector**
4. **Max speed control**
5. **Live stats bar**
6. **Play / Pause**

### 6.1 Audio Visualizer
- A **waveform or spectrum bar visualizer** running in real time — the most visually engaging element of this mode
- Warm accent color bars or a single continuous waveform line
- Should feel alive and reactive, not clinical
- The app uses system audio loopback (WasapiLoopbackCapture) — no audio source picker is needed; display a small label: *"Listening to system audio"*
- If no audio is detected, show a gentle idle animation with text: *"Play some music to get started"*

### 6.2 Beat Indicator
- A **circular pulse ring** or large dot that flashes with the accent color on each detected beat
- Positioned prominently, near or overlaid on the visualizer
- Doubles as a visual confirmation that beat detection is working

### 6.3 Thrust Rhythm Selector
Controls how detected beats are translated into device strokes — specifically, how many beats make up one full thrust cycle (forward + back).

This is presented as a **3-option segmented control or tile selector**, using plain-language labels with a short description under each. The internal ratios are never shown to the user.

| Option | Label | Description shown in UI |
|---|---|---|
| 1:1 | **Every beat** | *"The device thrusts with every beat"* |
| 1:2 | **Every 2 beats** | *"Forward on beat 1, back on beat 2 — a slower, fuller stroke"* |
| 1:4 | **Every 4 beats** | *"Forward for two beats, back for two beats — the most drawn-out rhythm"* |

- Default: **Every beat** (1:1)
- Each option may include a small abstract rhythm diagram — e.g. a row of dots or tick marks with an arrow showing the stroke pattern — to make the options scannable without reading the description
- Changing the selection takes effect immediately on the next detected beat; no confirmation needed
- The device BPM sent is: `detected BPM ÷ ratio` (e.g. detected 120 BPM on 1:2 = 60 BPM sent to device)

### 6.4 Max Speed Control
An optional ceiling that caps the BPM value **sent to the device** — i.e. it applies after the thrust rhythm ratio has been applied, not to the raw detected BPM.

For example: if the music is at 160 BPM, the rhythm is set to *Every 2 beats* (÷2 = 80 BPM to device), and the max speed cap is 60 BPM — then 60 BPM is sent (the cap is honoured on the device value, not the 160 BPM source).

- A single slider labelled *"Max device speed"*, spanning 0–240 BPM (displayed as an integer)
- Default: 240 BPM (uncapped — full range)
- The current value is shown as both BPM and its percentage equivalent, mirroring the dual-unit display convention from Manual mode: e.g. *"180 BPM · 75%"*
- When set below 240, a subtle indicator communicates that a cap is active (e.g. the slider track beyond the thumb is visually muted, or a small badge reads *"Capped"*)
- The control is entirely optional — if the user has never touched it, it defaults to the maximum and no cap is applied

### 6.5 Live Stats Bar
A small strip at the bottom of the card showing three values:

- **Music** — the detected tempo from the audio, e.g. *"128 BPM"*
- **Device** — the BPM actually being sent to the device after the thrust rhythm ratio and speed cap are applied, e.g. *"64 BPM"*
- **Speed** — the same device value expressed as a percentage, e.g. *"27%"*

When the thrust rhythm is *Every beat* and no cap is active, Music and Device will match. When they diverge (ratio applied or cap hit), showing both values helps the user understand why the device feels slower than the music tempo. All values update smoothly using a moving average — not jittery frame-by-frame changes.

### 6.6 Play / Pause
- A **Play/Pause button** to start and stop beat detection without leaving the mode
- When paused, the visualizer continues (so the user can see audio) but no commands are sent — display a pill badge: *"Detection paused"*

### 6.7 Future: Sensitivity Control *(not in current scope)*
A beat sensitivity control (mapping to the internal `OnsetMultiplier`) is a planned future addition. Reserve space in the layout for a control labelled *"Beat sensitivity"* with Low / Medium / High anchors, but it should not be built or exposed in the initial release. A placeholder/greyed-out treatment is acceptable if helpful for layout continuity.

---

## 7. Global Stop Button

**This is a safety-critical element and must be always accessible when connected.**

- Positioned in the **bottom-left of the footer bar**
- Appearance: a large, confidence-inspiring rounded button — not alarming red, but clearly a strong action. Consider a deep mauve or warm charcoal with white text: **"Stop"**
- Sends an immediate Stop command to the device (`AA 04 00 04`) and resets the speed display to 0 in all modes
- After pressing, a brief visual confirmation: button flashes to a muted success state + label changes momentarily to *"Stopped"*
- **Keyboard shortcut:** `Spacebar` — displayed as a tooltip on the button
- When disconnected: button is dimmed and non-interactive

---

## 8. Error States Reference

| Situation | Message | Action(s) |
|---|---|---|
| BLE adapter not available on this PC | *"Bluetooth isn't available on this device. Please check your Bluetooth settings."* | "Open Settings" |
| No devices found after scan | *"No devices found. Make sure your Hismith is on and nearby."* | "Try again" |
| Wrong device model validated | *"This doesn't appear to be a compatible Hismith device."* | "Go back" |
| BLE connection failed | *"Couldn't connect to [name]. Try moving closer to the device."* | "Retry" |
| Connection lost mid-session | *"Connection lost. Your device has stopped."* | "Reconnect" / "Dismiss" |
| Command send failure | Silent retry (1×); if still fails, brief toast notification: *"Couldn't reach device"* | — |
| No system audio detected | *"No audio detected. Play some music to get started."* | — |

---

## 9. First-Run Onboarding

On first launch only, display a brief **welcome overlay** (a modal card over the app):

- App logo / wordmark
- One-line tagline (e.g. *"Move to the beat."*)
- Three-step graphic: **Connect · Choose a mode · Play**
- Primary CTA: **"Get started"** → dismisses overlay, app is ready to use
- Secondary link: *"Skip"* (same behaviour)

This overlay should not appear on subsequent launches. It does not need to be a full tutorial — keep it to a single screen.

---

## 10. Responsive Sizing Behaviour

The window should resize gracefully across the full range from compact laptop screens to large 1440p displays:

- **Minimum size:** ~480 × 640px — all critical controls remain accessible; content cards may compress vertically but must never clip or overflow
- **Comfortable target:** ~600 × 720px — the baseline design size at which all proportions are defined
- **Medium:** Up to ~1024 × 768px — content cards expand; the speed slider and audio visualizer grow to fill available space; preset chips and stats bars remain anchored at their natural sizes
- **Large / 1440p:** Up to 2560 × 1440px — the layout should not simply stretch to fill all available space. Instead, the content area caps at a comfortable max-width (e.g. ~900px) and centres horizontally, with the remaining side space handled gracefully (background fill or deliberate breathing room). The footer Stop button must remain easily reachable and not drift to an awkward position at extreme widths.

**General rules:**
- No horizontal scrolling under any window size
- No content that overflows or clips outside the window
- Typography scales with the window (use relative units where possible) — large displays should not show tiny text
- The audio visualizer in Sound mode benefits most from extra vertical height — prioritise expanding it when space allows

---

## 11. Accessibility Considerations

- All interactive controls must have a minimum touch/click target of 32×32px (preferably 44×44px for primary actions)
- All text must meet WCAG AA contrast against its background
- All controls must be keyboard-navigable (Tab order follows visual layout)
- The Stop button must be reachable by keyboard at all times (`Spacebar` shortcut)
- Slider values should be readable by screen readers (ARIA-equivalent WPF automation peers)
- Do not convey state through colour alone — pair colour indicators with text labels or icons

---

## 12. Extensibility Notes for Future Modes

The mode tab strip is designed to accommodate new modes. When adding a mode:

- It slots into the tab strip in place of the `+` placeholder
- Each mode gets its own content card following the same card/layout conventions
- The Global Stop button and header status chip are shared infrastructure — mode cards should not replicate these

---

*Document prepared for handoff to UI designer. Implementation platform: WPF (.NET 8, Windows 10+). Target TFM: `net8.0-windows10.0.19041.0`.*
