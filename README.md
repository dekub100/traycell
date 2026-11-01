# TrayCell

[![CI](https://github.com/dekub100/traycell/actions/workflows/ci.yml/badge.svg)](https://github.com/dekub100/traycell/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Battery levels for your wireless peripherals, in the Windows tray.

TrayCell is a small native C# app that reads the battery level of a **Razer Viper V3 Pro SE**
and an **8BitDo Ultimate 2C** and shows it in a single tray icon. No Razer Synapse, no 8BitDo
software, no background services, no telemetry.

## 🤖 AI-Generated

This project was written entirely by AI ([OpenCode](https://github.com/anomalyco/opencode)
with DeepSeek V4.1 Flash). I test it against my own hardware, but the approach isn't always
idiomatic — issues and PRs are welcome.

It's a personal project: built for my own setup and shared publicly as-is, in case someone
else finds it useful. No support commitment — if it works for you too, great.

## Features

- **One tray icon** — left-click for a flyout with a row and level bar per device.
- **Pin a device** — click a row (or use *Tray icon shows*) to keep either device on the
  icon; the choice is remembered between runs.
- **Honest states** — a number is never shown as current once the device stops answering.
  Stale values are marked, disconnected devices drop their number, and a transport with no
  percentage says so instead of guessing.
- **Charging indicator** — a bolt is drawn while the mouse reports charging.
- **No dependencies** — raw `hid.dll` / `setupapi` / `cfgmgr32` interop; no NuGet packages.

## Supported devices

| Device | Transport | Battery |
|---|---|---|
| Razer Viper V3 Pro SE | 2.4 GHz dongle or USB cable | Percentage via Razer HID feature reports |
| 8BitDo Ultimate 2C | Bluetooth | Percentage from the Windows Bluetooth device property |
| 8BitDo Ultimate 2C | USB cable or 2.4 GHz dongle | Connected, but **no percentage** — XInput only reports wired/full |

Other devices using the same transports may work; they have not been tested.

## Requirements

- Windows 10 or 11, x64.
- Nothing else for a release build — it is self-contained.
- To build from source: the .NET 10 SDK.

## Install

1. Download `TrayCell-v*-win-x64.zip` from
   [Releases](https://github.com/dekub100/traycell/releases).
2. Extract it anywhere and run `TrayCell.App.exe`.

The executable is unsigned, so Windows SmartScreen may ask for confirmation the first time.

## Usage

- **Left-click** the tray icon to open the flyout.
- **Click a device row** to pin that device to the icon; click it again to return to
  automatic (the lowest live level).
- **Right-click** for the menu: current levels, *Tray icon shows*, *Refresh now*, *Exit*.
- **Esc** or clicking elsewhere closes the flyout.

Icon states: green / amber / red fill is the level, a slash means the value is stale, a
centre dot means connected but unmetered, and a grey outline means not connected.

## How it works

The mouse speaks Razer's 90-byte HID feature-report protocol through its dongle; the
controller is read from the Bluetooth battery property that Windows itself exposes. Protocol
details, response codes and dead ends are in
[`docs/hardware-findings.md`](docs/hardware-findings.md).

## Known limitations

- **A sleeping mouse looks switched off.** The dongle answers "no response" for both, so an
  idle mouse shows as not connected until it is moved.
- **No percentage over XInput.** On USB or the 2.4 GHz dongle the controller reports only
  "wired / full"; the app shows "connected, no battery level" rather than inventing a number.
- **Device matching is by name and VID/PID.** A renamed controller or a different Razer model
  may not be picked up.
- **Windows only** — it uses WinForms and Windows device APIs.

## Build from source

```powershell
dotnet build TrayCell.slnx
dotnet run --project src/TrayCell.App
```

`TrayCell.Probe` is a console harness for the hardware side; see
[`CONTRIBUTING.md`](CONTRIBUTING.md) for its commands.

## Documentation

- [`docs/hardware-findings.md`](docs/hardware-findings.md) — verified protocol, device IDs,
  response codes.
- [`docs/battery-tray-handoff.md`](docs/battery-tray-handoff.md) — original research and
  reasoning.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — build, probe, CI and commit conventions.

## License

MIT — see [`LICENSE`](LICENSE).
