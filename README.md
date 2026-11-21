# LOQ Fan Monitor

A lightweight, read-only Windows desktop application for Lenovo LOQ laptops. Built with C# and WPF on .NET 10, it displays real-time fan speeds, CPU/GPU temperatures (when supported), and fan speed history through a modern desktop interface.

## Features

- **Real-time fan monitoring:** Reads Fan 1 and Fan 2 RPM using Lenovo WMI feature IDs validated on the developer's Lenovo LOQ:
  - `0x04030001`
  - `0x04030002`
- **CPU and GPU temperatures:** Attempts to retrieve temperatures through Lenovo's `LENOVO_GAMEZONE_DATA` getter methods.
- **Fan speed history:** Displays a rolling 180-second RPM graph.
- **System tray support:** Allows the application to minimize to the Windows system tray.

## Safety and Read-Only Design

This application is deliberately designed to be read-only.

It calls only the following WMI methods:

- `LENOVO_OTHER_METHOD.GetFeatureValue`
- `LENOVO_GAMEZONE_DATA.GetCPUTemp`
- `LENOVO_GAMEZONE_DATA.GetGPUTemp`

It does **not** call WMI setters, fan-table methods, BIOS interfaces, or fan-control APIs.

Temperature getter methods are model- and firmware-dependent and have not yet been confirmed across all Lenovo LOQ configurations. If the WMI provider does not return a valid temperature, the application displays `-- °C`.

Fan RPM monitoring does not depend on temperature support.

## Requirements

- Windows 11
- .NET 10 SDK
- Lenovo WMI provider available on the laptop

## Run from Source

1. Install the .NET 10 SDK from Microsoft's official .NET download page.
2. Clone or download this repository.
3. Open PowerShell in the project folder.
4. Run:

   ```powershell
   dotnet restore
   dotnet run
   ```

If WMI access is denied, close the application and try running PowerShell or Windows Terminal as Administrator. The application manifest does not force Administrator privileges.

## Build

To build the application in Release configuration:

```powershell
dotnet build -c Release
```

## Publish a Standalone Version

To publish a self-contained Windows x64 version:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The published application will be available under:

```text
bin\Release\net10.0-windows\win-x64\publish
```

## Testing Checklist

- Confirm that both fan RPM readings match the PowerShell monitor.
- Switch between Quiet, Balanced, Performance, and Custom modes in Lenovo Vantage and check whether the application reflects the changes.
- Check whether CPU and GPU temperature readings are available.
- If temperature readings remain unavailable, inspect the exact WMI method signatures and outputs before adding another sensor source.
- Test minimizing to the system tray, reopening the application from the tray icon, and exiting through the tray menu.

## Current Limitations

- Temperature getter methods are best-effort and still need validation across different Lenovo LOQ models and firmware versions.
- The graph currently uses a fixed, broad RPM scale.
- The tray icon currently uses a generic Windows information icon. A custom LOQ icon may be added in a future update.

## Disclaimer

This project is an independent monitoring utility and is not an official Lenovo application. It does not control fan speeds or modify laptop firmware settings.