# HismithController

Windows WinForms app: captures system audio → detects beats in real-time → controls Hismith BLE device speed.

## Architecture

Three subsystems wired by C# events in `MainForm`:
1. **Audio** (`src/HismithController/Audio/`) — NAudio `WasapiLoopbackCapture` loopback capture
2. **Beat Detection** (`src/HismithController/BeatDetection/`) — spectral flux onset detection
3. **Bluetooth** (`src/HismithController/Bluetooth/`) — Windows WinRT BLE GATT writes

All subsystems implement interfaces and are registered via `Microsoft.Extensions.DependencyInjection`.

## Build & Run

```bash
dotnet build HismithController.slnx
dotnet run --project src/HismithController/HismithController.csproj
dotnet run --project src/HismithController/HismithController.csproj -- --mock
dotnet test HismithController.slnx
```

## Key Libraries

- **NAudio 2.2.1** — `WasapiLoopbackCapture` for system audio loopback
- **MathNet.Numerics 5.0.0** — `Fourier.ForwardReal()` for FFT in beat detection
- **Windows WinRT BLE** — `Windows.Devices.Bluetooth.GenericAttributeProfile` (from `net8.0-windows10.0.19041.0` TFM, no extra NuGet needed)
- **Microsoft.Extensions.DependencyInjection 8.0.0** — DI container
- **Microsoft.Extensions.Configuration.Json 8.0.0** — `AppConfig.json` settings

Project targets `net8.0-windows10.0.19041.0`, x64 only (required for WinRT BLE APIs).

## Beat Detection

Spectral flux with adaptive threshold (`SpectralFluxAnalyzer.cs`):
- FFT size: 512 samples (~11.6ms at 44100 Hz), hop size: 256 samples (50% overlap)
- Positive spectral flux: `sum(max(0, |X[k]| - |X_prev[k]|))` over low-frequency bins
- Threshold: `mean(last 40 flux values) * OnsetMultiplier` (default 1.5)
- Min inter-onset interval: 200ms (caps detection at 300 BPM)
- Beat detection runs synchronously on the NAudio audio thread — must stay under 5ms per frame

## Hismith BLE Protocol

**Status: best-guess, requires reverse engineering with real hardware.**

Likely Nordic UART Service (NUS):
- Service UUID: `6E400001-B5A3-F393-E0A9-E50E24DCCA9E`
- TX characteristic: `6E400002-B5A3-F393-E0A9-E50E24DCCA9E` (Write Without Response)
- Command frame: `[0xAA, 0x01, speed(0–99), XOR_checksum, 0xFF]`

To confirm protocol: use nRF Connect on Android to sniff BLE traffic from the official HiApp during speed changes. Update `HismithProtocol.cs` with confirmed byte format and UUIDs.

## Threading Model

- **UI thread** — WinForms message pump; all `Control` updates must happen here
- **Audio thread** — NAudio fires `DataAvailable` here; beat detection runs synchronously in this callback
- **BLE writes** — `async`/`await` dispatched from the UI thread after marshaling beat events via `BeginInvoke`

## Mock Mode

Pass `--mock` on the command line or set `"UseMockBle": true` in `AppConfig.json`. `MockBleDeviceService` logs all speed commands to the UI log panel. Audio capture and beat detection run normally in mock mode — no BLE hardware required.
