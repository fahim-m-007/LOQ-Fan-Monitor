# LOQ Fan Monitor

A read-only Windows desktop monitor for Lenovo LOQ laptops.

## What it does

- Reads Fan 1 and Fan 2 RPM using the Lenovo WMI feature IDs already tested in PowerShell:
  - `0x04030001`
  - `0x04030002`
- Attempts CPU and GPU temperature reads through Lenovo `LENOVO_GAMEZONE_DATA` getter methods.
- Shows a rolling 180-second fan RPM graph.
- Minimizes to the Windows system tray.

## Important safety boundary

This project is deliberately read-only. It calls only:
- `LENOVO_OTHER_METHOD.GetFeatureValue`
- `LENOVO_GAMEZONE_DATA.GetCPUTemp`
- `LENOVO_GAMEZONE_DATA.GetGPUTemp`

It does not call WMI setters, fan-table methods, BIOS interfaces, or fan-control APIs.

Temperature methods are model/firmware dependent and have not yet been confirmed on this particular LOQ. If the WMI provider does not return a valid temperature, the UI displays `-- °C`. Fan RPM reading does not depend on temperature support.

## Requirements

- Windows 11
- .NET 10 SDK
- Lenovo WMI provider available on the laptop

## Run from source

1. Install the .NET 10 SDK from Microsoft's official .NET download page.
2. Extract this folder.
3. Open PowerShell in this folder.
4. Run:

   ```powershell
   dotnet restore
   dotnet run
   ```

If WMI access is denied, close the app and try running PowerShell/Terminal as Administrator, then run it again. The app manifest does not force Administrator privileges.

## Publish a standalone folder

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The published output will be under `bin\Release\net10.0-windows\win-x64\publish`.

## First test checklist

- Confirm both fan RPM values match the PowerShell monitor.
- Switch between Quiet, Balanced, Performance, and Custom modes in Lenovo Vantage and check that the app follows the changes.
- Check whether CPU/GPU temperature values appear. If they remain unavailable, we should inspect the exact WMI method signatures and outputs before adding another sensor source.
- Minimize to tray, reopen from the tray icon, then exit using the tray menu.

## Current limitations

- The temperature getter calls are best-effort and need validation on the target laptop.
- The graph uses a fixed broad RPM scale for now.
- The tray icon is a generic Windows information icon; a custom LOQ icon can be added later.
