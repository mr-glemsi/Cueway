<div align="center">

<img src="Assets/logo.png" alt="Cueway Logo" width="88" height="88" />

# Cueway

Lightweight media key router and per-tab media controller for Windows 10 and 11.

<p>
  <a href="https://github.com/mr-glemsi/Cueway/releases/latest">
    <img src="https://img.shields.io/github/v/release/mr-glemsi/Cueway?style=flat-square&label=Download&color=2563EB" alt="Latest Release" />
  </a>
  <a href="https://microsoftedge.microsoft.com/addons/detail/cueway-companion/cebckhofejojabmcfeinkdclglibafmk">
    <img src="https://img.shields.io/badge/Edge%20Add--ons-Available-0078D7?style=flat-square" alt="Edge Add-ons" />
  </a>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-111827?style=flat-square" alt="Windows 10/11" />
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/License-MIT-374151?style=flat-square" alt="MIT License" />
  </a>
</p>

<br />

<img src="Assets/preview.png" alt="Cueway Dashboard" width="820" />

</div>

Cueway routes hardware media keys to the highest-priority active player on your system. It works with desktop applications (including players without native Windows media session support) and individual browser tabs through a local companion extension.

## Download

Download the latest Windows installer (`Cueway-Setup-x64.exe`) from the [Releases](https://github.com/mr-glemsi/Cueway/releases/latest) page.

## Features

- Priority-based routing for hardware media keys and playback controls
- Per-tab playback, volume, mute, and focus control through the companion browser extension
- Automatic detection of active desktop audio sources, with or without Windows SMTC support
- Manual target lock (`Alt+M`) and solo mode (`Alt+S`)
- Fullscreen game detection and automatic pause/resume for lower-priority players
- Lightweight native Windows build with a system tray interface

## Settings & Customization

<div align="center">
  <img src="Assets/preview-settings.png" alt="Cueway Settings and Themes" width="820" />
</div>

## Browser Extensions

The companion extensions talk to the desktop app over a local loopback WebSocket (`127.0.0.1:47621`) and make no external network requests.

- **Microsoft Edge:** [Cueway Companion on Edge Add-ons](https://microsoftedge.microsoft.com/addons/detail/cueway-companion/cebckhofejojabmcfeinkdclglibafmk)
- **Chromium (Chrome, Brave, Opera):** `BrowserExtension/Chromium` (Manifest V3)
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

## Code Signing Policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

See [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) for signing scope, build provenance, and maintainer roles.

## Privacy

Cueway runs entirely locally and does not collect or send user data. See [PRIVACY.md](PRIVACY.md) for details.

## License

[MIT](LICENSE)
