# LOQ Fan Monitor

A sleek, read-only Windows desktop telemetry dashboard for Lenovo LOQ laptops. Built with C# and WPF on .NET 10, it displays real-time CPU & GPU fan speeds, live temperatures, session averages and peaks, and a rolling 180-second RPM history graph.

## Features

- **Real-time Fan Telemetry:**
  - **Fan 1 (GPU)** & **Fan 2 (CPU)** live RPM readings via validated Lenovo WMI feature IDs (`0x04030001` and `0x04030002`).
  - **Animated Fan Icons:** Vector fan icons spin dynamically at speeds proportional to live RPM readings.
  - **Load Gauges & Stats:** Horizontal progress bars (calibrated to `5,800 RPM` max), percentage indicators (`%`), and session **`Avg`** and **`Peak`** RPM badges.
- **CPU & GPU Temperatures:**
  - Retrieves live temperatures via Lenovo's `LENOVO_GAMEZONE_DATA` WMI methods (`GetCPUTemp`, `GetGPUTemp`).
  - Dynamic color-coded thermal level bars (cool green → warm amber → hot coral) alongside session **`Avg`** and **`Peak`** temperature badges.
- **Fan Speed History Chart:**
  - Smooth 180-second rolling dual-series area chart with Y-axis RPM scale labels (`5800` – `0` RPM) and gradient fills.
- **Smart System Tray Integration:**
  - Clicking **Minimize (`-`)** silently minimizes the app to the Windows system tray and pauses UI animations for zero background GPU/CPU overhead.
  - **Single left-click** on the tray icon immediately restores the window; right-click opens the tray menu (`Open` / `Exit`).
  - **Live Tray Tooltip:** Hovering over the system tray icon displays live CPU/GPU temperatures and fan RPMs.
  - Clicking **Close (`X`)** cleanly exits the application.

## Safety and Read-Only Design

This application is strictly **read-only**. It calls only the following WMI getter methods in `\\.\root\WMI`:

- `LENOVO_OTHER_METHOD.GetFeatureValue`
- `LENOVO_GAMEZONE_DATA.GetCPUTemp`
- `LENOVO_GAMEZONE_DATA.GetGPUTemp`

It **never** calls WMI setters, fan-curve/table methods, BIOS modifiers, or fan-control APIs.

## Requirements

- Windows 11 (x64)
- .NET 10 SDK (to build from source)
- Lenovo WMI provider available on the laptop
- Administrator privileges (enforced automatically via `app.manifest` so WMI telemetry queries succeed)

## Run from Source

1. Install the .NET 10 SDK.
2. Clone this repository:
   ```powershell
   git clone https://github.com/fahim-m-007/LOQ-Fan-Monitor.git
   cd LOQ-Fan-Monitor
   ```
3. Build and run (in an elevated PowerShell terminal):
   ```powershell
   dotnet build -c Release
   Start-Process ".\bin\Release\net10.0-windows\LOQFanMonitor.exe" -Verb RunAs
   ```

## Publish a Standalone Single-File Executable

To publish a self-contained single-file Windows x64 executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The standalone executable will be generated at:

```text
bin\Release\net10.0-windows\win-x64\publish\LOQFanMonitor.exe
```

## Disclaimer

This project is an independent hardware monitoring utility and is not an official Lenovo application. It does not control fan speeds or modify laptop firmware settings.
