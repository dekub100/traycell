# TrayCell

A Windows system tray app that shows the battery level of wireless peripherals — currently a **Razer Viper V3 Pro SE** mouse and an **8BitDo Ultimate 2C Wireless** controller.

The goal is a glanceable, always-on percentage per device, with no Razer Synapse and no vendor software.

## Status

Research complete, implementation not started. Two documents:

- [`docs/battery-tray-handoff.md`](docs/battery-tray-handoff.md) — the original research, including dead ends and reasoning.
- [`docs/hardware-findings.md`](docs/hardware-findings.md) — what has since been **verified on the target machine**: exact device IDs, protocol bytes, CRC rule.

| Device | Feasible | Transport | State |
|---|---|---|---|
| 8BitDo Ultimate 2C | Yes, verified | BLE PnP device property (`{104EA319-...} 2`) | Read live from the shell: 100%, node `BTHLE\DEV_...` |
| Razer Viper V3 Pro SE | Yes, verified IDs | Razer HID feature reports, class `0x07` | PID `0x00DF` (dongle) confirmed on machine; tx id `0x1f`, commands `0x07/0x80` + `0x07/0x84` confirmed in OpenRazer. Only the HID collection to open is unverified. |

## Design in one paragraph

One process, two independent pollers on a 30–60 s timer, and a per-device state model of `Level`, `Charging`, `LastUpdated`, `State = Ok | Stale | Off`. A number is never shown without its staleness marked, because both devices can silently stop reporting (mouse asleep, controller switched to the 2.4 GHz dongle). Tray UI is either two icons with the percentage drawn on them or one icon with a combined tooltip.

Planned stack: C# / .NET on Windows — `NotifyIcon` for the tray, SetupAPI (`CM_Get_DevNode_PropertyW`) for the controller, HidSharp for the Razer feature reports. Rust is a viable alternative but was not chosen.

## Known open questions

- Fork an existing Viper V3 Pro tray project for the mouse side, or write everything from scratch?
- Two tray icons or one combined icon/tooltip?
- Does the mouse also need to be monitored while charging over cable?
- Which SE dongle is in use (HyperSpeed vs HyperPolling), and what are its real hardware IDs?

## Dead ends — do not repeat

XInput battery query over the 2.4 GHz dongle (returns a placeholder `WIRED`/`FULL`), D-input over the dongle, 8BitDo vendor software, and USBPcap HID capture of the dongle.
