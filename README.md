# TrayCell

A Windows system tray app that shows the battery level of wireless peripherals — currently a **Razer Viper V3 Pro SE** mouse and an **8BitDo Ultimate 2C Wireless** controller.

The goal is a glanceable, always-on percentage per device, with no Razer Synapse and no vendor software.

## Status

Research complete and implemented. Two documents:

- [`docs/battery-tray-handoff.md`](docs/battery-tray-handoff.md) — the original research, including dead ends and reasoning.
- [`docs/hardware-findings.md`](docs/hardware-findings.md) — what has since been **verified on the target machine**: exact device IDs, protocol bytes, CRC rule.

| Device | Feasible | Transport | State |
|---|---|---|---|
| 8BitDo Ultimate 2C | Yes, verified | BLE PnP device property (`{104EA319-...} 2`) | Read from code: 100% over Bluetooth. Node `BTHLE\DEV_...`, matched by friendly name. Over USB or the 2.4 GHz dongle Windows presents XInput instead, which has **no percentage** — the app then shows "connected, no battery level". |
| Razer Viper V3 Pro SE | Yes, verified | Razer HID feature reports, class `0x07` | Read from code: 57% (level in `arguments[1]`). PID `0x00DF`, channel `MI_00`, 91-byte feature report. A sleeping mouse answers `0x04` and shows as stale. |

## Design in one paragraph

One process, two independent pollers on an 8 s timer, PnP device-instance notifications for immediate refreshes, and a per-device state model of `Level`, `Charging`, `LastUpdated`, `State = Ok | Stale | Wired | Off`. A number is never shown without its staleness marked, because both devices can silently stop reporting (mouse asleep, controller switched to the 2.4 GHz dongle). A device that stops answering drops its number at once rather than lingering as stale — the mouse dongle has no link event and a sleeping mouse answers exactly like a switched-off one, so "not responding" is the honest state. `Wired` means the device is connected over a transport with no percentage (XInput) — the app says so instead of showing a cached number. The UI is a single tray icon; left-clicking it opens a small flyout with a row and level bar per device, plus Refresh and Exit. The icon follows the lowest live level by default, or a device you pin by clicking its row (or via the "Tray icon shows" menu); the pin is saved to `%LOCALAPPDATA%\TrayCell\settings.json`.

Planned stack: C# / .NET 10 on Windows — `NotifyIcon` for the tray, cfgmgr32 (`CM_Get_DevNode_PropertyW`) for the controller, and raw `hid.dll` / `setupapi` feature reports for the Razer mouse. No NuGet packages.

## Project layout

```
src/TrayCell.Core/     pollers, native interop, Ok/Stale/Off state model
src/TrayCell.Probe/    console harness that exercises both devices independently
src/TrayCell.App/      WinForms tray app (single icon, flyout, context menu)
```

## Building and running

```powershell
dotnet build TrayCell.slnx

# both devices
dotnet run --project src/TrayCell.Probe -- all

# controller only (Bluetooth PnP property)
dotnet run --project src/TrayCell.Probe -- controller

# mouse only (enumerates Razer HID collections, tries the battery query on each)
dotnet run --project src/TrayCell.Probe -- razer

# watch the mouse for 30 s while you move it
dotnet run --project src/TrayCell.Probe -- watch

# single spaced read with every response argument printed
dotnet run --project src/TrayCell.Probe -- battery

# watch PnP device events for 60 s while you plug/unplug something
dotnet run --project src/TrayCell.Probe -- notify --seconds 60

# the tray app
dotnet run --project src/TrayCell.App
```

## Known open questions

- ~~Fork an existing Viper V3 Pro tray project, or write everything from scratch?~~ Answered: scratch, C#.
- ~~Two tray icons or one combined icon/tooltip?~~ Answered: one icon, left-click opens a flyout.
- Does the mouse also need to be monitored while charging over cable?
- Which SE dongle is in use (HyperSpeed vs HyperPolling), and what are its real hardware IDs?

## Dead ends — do not repeat

XInput battery query over the 2.4 GHz dongle (returns a placeholder `WIRED`/`FULL`), D-input over the dongle, 8BitDo vendor software, and USBPcap HID capture of the dongle.

The same applies to the controller on a **USB cable**: XInput reports `wired`/`full` and nothing else. The only percentage Windows ever exposes for this controller is the Bluetooth property, and only while the Bluetooth link is actually up — see [`docs/hardware-findings.md`](docs/hardware-findings.md#controller-transport-state).
