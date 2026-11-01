# AGENTS.md

TrayCell shows the battery level of a **Razer Viper V3 Pro SE** (wireless dongle, HID feature
reports) and an **8BitDo Ultimate 2C** (Bluetooth PnP property, or XInput when wired / on the
2.4 GHz dongle) in a single Windows tray icon. No vendor software, no NuGet packages.

## Before changing anything

- Read [CONTRIBUTING.md](CONTRIBUTING.md) for build, probe and commit rules.
- Hardware facts live in [`docs/hardware-findings.md`](docs/hardware-findings.md). Trust that
  file over assumptions, and do not retry the dead ends listed there.
- The original research and reasoning is in
  [`docs/battery-tray-handoff.md`](docs/battery-tray-handoff.md).

## Commands

```powershell
dotnet build TrayCell.slnx
dotnet run --project src/TrayCell.App
dotnet run --project src/TrayCell.Probe -- all
```

Probe commands: `all`, `controller`, `xinput`, `razer`, `hid`, `notify`, `battery`, `watch`,
`reportids`. There is no test project yet.

## Layout

| Path | Contents |
|---|---|
| `src/TrayCell.Core` | pollers, cfgmgr32/HID interop, `Ok / Stale / Wired / Off` monitor |
| `src/TrayCell.Probe` | console harness that exercises each transport independently |
| `src/TrayCell.App` | WinForms tray app: single icon, flyout, context menu, pin preference |
| `docs/` | handoff research and verified hardware findings |

## Rules

- Conventional Commits; the history is the changelog source (`cliff.toml`).
- Add the `Generated-By:` trailer to commits.
- Run the app after a change and leave it running; the user closes it.
- Never `git push` or create a release unless asked.
- A sleeping mouse and a switched-off mouse are indistinguishable at the protocol level
  (status `0x04`). Do not add logic that pretends to tell them apart.
- Keep `TrayCell.Core` free of WinForms and external packages.
- A paired-but-offline Bluetooth node still carries a cached battery value
  (`DN_DEVICE_DISCONNECTED`); never report it as current.
