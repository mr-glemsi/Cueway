# Cueway

A lightweight media key router and per-tab media controller for Windows 10 and 11.

Cueway routes hardware media keys to the highest-priority active player on your system. It works with desktop applications (including players without native Windows media session support) and individual browser tabs through a local companion extension.

## Features

- Priority-based routing for hardware media keys and playback controls
- Per-tab playback, volume, mute, and focus control through the companion browser extension
- Automatic detection of active desktop audio sources, with or without Windows SMTC support
- Manual target lock (`Alt+M`) and solo mode (`Alt+S`)
- Fullscreen game detection and automatic pause/resume for lower-priority players
- Lightweight native Windows build with a system tray interface

## Browser Extensions

The companion extensions talk to the desktop app over a local loopback WebSocket (`127.0.0.1:47621`) and make no external network requests.

- **Chromium:** `BrowserExtension/Chromium` (Manifest V3)
- **Firefox:** `BrowserExtension/Firefox` (Manifest V2)

## Building from Source

Requires the .NET 10 SDK (with the C++ desktop workload for Native AOT). Inno Setup 7 is optional for packaging the installer.

```powershell
# Debug build
dotnet build Cueway.csproj -c Debug

# Release build (Native AOT)
dotnet publish Cueway.csproj -c Release -r win-x64

# Installer (optional)
& "C:\Program Files\Inno Setup 7\ISCC.exe" Cueway.iss
```

## Privacy

Cueway runs entirely locally and does not collect or send user data. See [PRIVACY.md](PRIVACY.md) for details.

## License

[MIT](LICENSE)
