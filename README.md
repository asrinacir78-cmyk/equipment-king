# Equipment King

Equipment King is a small Windows desktop app that shows the names, connection state, and battery levels of nearby accessories.

## Features

- Lists paired Bluetooth accessories and shows whether Windows reports them as connected.
- Lists enabled USB devices that Windows exposes, including wired audio, storage, mouse, keyboard, controller, and receiver interfaces.
- Labels the connection type alongside the device category.
- Reads battery percentages exposed through Windows device properties and Windows battery controllers.
- Reads the standard Bluetooth LE Battery Service when a connected accessory provides it.
- Refreshes automatically every 30 seconds and on demand.
- Runs device scans in a separate process so a device driver or Windows API crash does not close the app window.
- Records scan stage progress and errors in `%LOCALAPPDATA%\EquipmentKing\equipment-king.log`.

## Battery support

The app can show a percentage only when the device, its receiver, or its Windows driver exposes a battery reading. This includes receiver-based accessories when Windows reports their HID battery property or a matching battery controller.

Some wireless receivers use vendor-specific protocols and do not expose a standard Windows battery reading. Those devices will still appear when Windows identifies their HID collection, but their battery will show as unavailable until support for that vendor protocol is added. There is no universal way for a Windows app to read every proprietary receiver's battery data.

## Requirements

- Windows 10 version 2004 (build 19041) or later
- .NET 10 SDK to build from source

## Build

```powershell
dotnet restore .\EquipmentKing.csproj
dotnet build .\EquipmentKing.csproj -c Release
```

To publish a self-contained Windows x64 app:

```powershell
dotnet publish .\EquipmentKing.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

The GitHub Actions workflow also creates a downloadable `win-x64` package when it runs.

## Privacy

Equipment King reads device information locally through Windows APIs. It does not send device names, battery readings, or other telemetry to a server.

## License

MIT. See [LICENSE](LICENSE).
