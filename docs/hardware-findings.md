# Hardware findings (verified on the target machine)

Date: 2026-11-01. Machine: Windows 11 build 26200, .NET SDK 10.0.301. Both devices connected:
mouse wirelessly via its dongle, controller over Bluetooth.

This file supersedes the "not yet obtained" list in `battery-tray-handoff.md`.

## 8BitDo Ultimate 2C — confirmed working

PnP battery property read successfully, no admin rights, via `Get-PnpDeviceProperty`:

```
key:  {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2
dev:  8BitDo Ultimate 2C Wireless
id:   BTHLE\DEV_REDACTED\8&31B49F7A&0&REDACTED
value: 100
```

- Friendly name matches exactly: `8BitDo Ultimate 2C Wireless`.
- Instance ID prefix is `BTHLE\DEV_<MAC>`, so match on prefix **and** friendly name; the
  trailing `8&...&0&REDACTED` is the same MAC, do not hardcode it.
- Value was 46% in the earlier session, is 100% now after charging — the property is live.
- The scan must iterate all present devices, since the device node class can vary.

No roadblocks. GATT fallback is not needed.

## Razer Viper V3 Pro SE — IDs and protocol confirmed

### USB IDs (confirmed against the machine and OpenRazer)

| Thing | Value |
|---|---|
| Vendor ID | `0x1532` |
| SE wireless (dongle) | `0x00DF` — **present** on this machine: `USB\VID_1532&PID_00DF` |
| SE wired | `0x00DE` — not enumerated right now (mouse is wireless) |
| Standard V3 Pro (for contrast) | wired `0x00C0`, wireless `0x00C1` |

OpenRazer defines these as `USB_DEVICE_ID_RAZER_VIPER_V3_PRO_SE_WIRED 0x00DE` and
`USB_DEVICE_ID_RAZER_VIPER_V3_PRO_SE_WIRELESS 0x00DF`, so the SE **is** a supported device
in OpenRazer, with its own entries in the battery path. The handoff's worry that the SE might
need reverse engineering is resolved: use the same commands as the standard V3 Pro.

### Interfaces exposed by the dongle (composite device)

| Interface | Contains |
|---|---|
| `MI_00` | HID mouse (`UP:0001_U:0002`) |
| `MI_01` | keyboard, consumer control, system control, plus vendor collections `UP:0001_U:0000` (Col05–Col08) — the likely control interface |
| `MI_02` | keyboard + generic input device |

Still to confirm empirically: which top-level collection accepts the feature report. Probe all
of them rather than assuming.

### Report format (90 bytes, "new" protocol)

| Offset | Field | Value for us |
|---|---|---|
| 0 | `status` | `0x00` on request |
| 1 | `transaction_id` | **`0x1f`** |
| 2–3 | `remaining_packets` (big endian) | `0x0000` |
| 4 | `protocol_type` | `0x00` |
| 5 | `data_size` | `0x02` for both commands |
| 6 | `command_class` | `0x07` |
| 7 | `command_id` | `0x80` battery level, `0x84` charging status |
| 8–87 | `arguments[80]` | results returned here |
| 88 | `crc` | XOR of bytes 2..87 |
| 89 | `reserved` | `0x00` |

- **Battery level**: class `0x07`, id `0x80`, `data_size 0x02` → level in `arguments[1]`, **0–255**. Scale to a percentage with `raw * 100 / 255` (OpenRazer does the same in `mamba.py` and `get_battery.py`).
- **Charging status**: class `0x07`, id `0x84`, `data_size 0x02` → `arguments[1]` = `0`/`1`.
- **CRC**: `crc = bytes[2] ^ bytes[3] ^ ... ^ bytes[87]`, stored at index 88.
- **Response check**: `status == 0x02` means success (`0x03` failure, `0x04` timeout/no response).
- The SE wireless battery query uses transaction id `0x1f`, identical to the standard V3 Pro
  (OpenRazer `razer_attr_read_charge_level`). No SE-specific command variants found.

Sources: `openrazer/driver/razermouse_driver.c`, `razermouse_driver.h`,
`razerchromacommon.c`, `razercommon.h`, `razercommon.c` at master.

## Mouse HID channel (verified from code)

Implemented in `TrayCell.Core/Hid` and `TrayCell.Core/Devices/Razer`, exercised by
`dotnet run --project src/TrayCell.Probe -- razer`. This replaces the "remaining unknown" above.

- Only **one** collection on the dongle exposes feature reports:
  `HID\VID_1532&PID_00DF&MI_00` (usage page `0x0001`, usage `0x0002`, the mouse), with
  `FeatureReportByteLength = 91` — a report id byte plus the 90-byte Razer report.
- Every other collection is input-only (`feat=0`), including the four vendor collections on
  `MI_01` (`UP 0001 / U 0000`). They cannot carry a class `0x07` command.
- Windows denies read/write access to that mouse collection (error 5). **Write-only**
  (`GENERIC_WRITE`) does succeed, which is enough: `HidD_SetFeature` + `HidD_GetFeature` both work.
- The report goes out as 91 bytes with report id `0x00` at index 0 and the Razer report at
  index 1; the reply has the same shape, so parsing starts at index 1.
- `HidP_GetCaps` returns `HIDP_STATUS_SUCCESS = 0x00110000`, **not** `0`. Testing against `0`
  silently reports `feat=0` for every device on the system.
- OpenRazer sleeps `RAZER_VIPER_MOUSE_RECEIVER_WAIT_US` (59900 us) between write and read;
  `TryFeatureTransaction` takes an optional settle delay for this.

Status: **working**. A live read returned

```
02 1F 00 00 00 02 07 80 00 39 00 00 ...
```

`status = 0x02` (success), `arguments[1] = 57` → **22%** after scaling, and the charging query
returned `arguments[1] = 0` (not charging).

Response codes seen in practice, all of which the poller must survive:

| Status | Meaning | When |
|---|---|---|
| `0x02` | success | mouse awake; `arguments[1]` is the level |
| `0x04` | command no response | **mouse asleep** — the normal case after a minute of idle. Not an error. |
| `0x01` | command busy | previous request still in flight, or polled too quickly |
| `0x02` with level `255` | success frame, ambiguous | `255` is both the valid full-charge value and the garbage frame seen while waking; confirm with a second read |

So a sleeping mouse is indistinguishable from an unreachable one at the protocol level, and a
single 255 reading is ambiguous. `RazerBatterySource` retries 4 times at 250 ms, confirms a 255
with a second read, and scales the raw byte to 0–100. `BatteryMonitor` shows the last good value
as *stale* rather than wrong.

## Controller transport state

Verified on the target machine with the controller plugged in by USB cable and charging.

- The paired BLE node **stays present** after the controller leaves Bluetooth:
  `BTHLE\DEV_REDACTED`, still carrying `DEVPKEY_Bluetooth_BatteryLevel = 100`.
  That value is cached from the last Bluetooth session, not the current level.
- The node is marked disconnected in `DEVPKEY_Device_DevNodeStatus`
  (`{4340A6C5-93FA-4706-972C-7B648008A5A7}`, 2). Observed status `0x0380200A` contains
  `DN_DEVICE_DISCONNECTED = 0x02000000`. Windows Settings agrees: "not connected".
  `DEVPKEY_Device_ProblemCode` is `0`, so the problem code is not a usable signal.
- Over USB the controller appears as `USB\VID_2DC8&PID_310A` with an
  `XnaComposite` / "Xbox 360 Controller for Windows" child, i.e. XInput. A second,
  differently-named node (keyboard/mouse collections on `MI_01`) is the same device.
- XInput's own battery query returns `type = wired`, `level = full` — a placeholder.
  **XInput exposes no percentage**, on USB or on the 2.4 GHz dongle.
- Windows also exposes a stale `8BitDo Ultimate 2C Wireless` entry that looks "OK" to
  `Get-PnpDevice`; only the devnode status bit reveals that it is offline.

Rule used by `ControllerBatterySource`: read the BLE property only when
`DN_DEVICE_DISCONNECTED` is clear; otherwise, if an XInput controller is live and an 8BitDo
USB device is present, return a *connected* reading with no level (`BatteryState.Wired`);
otherwise return a *disconnected* reading, which the monitor turns into `Off` immediately.

### Disconnect detection

- `CM_Register_Notification` with `CM_NOTIFY_FILTER_TYPE_DEVICEINSTANCE` and
  `CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES` works from a normal user process and fires
  on plug, unplug, Bluetooth reconnect and power-off. Verified: 39 callbacks across one
  plug → unplug → BT reconnect → power-off sequence.
- The native `CM_NOTIFY_FILTER` is 416 bytes: the flags live at the **top level**, and the
  DEVICEINSTANCE union is `WCHAR InstanceId[MAX_DEVICE_ID_LEN]` (200 chars / 400 bytes).
  Getting this wrong returns `CR_INVALID_DATA` (31).
- Callbacks arrive on a system thread, so the app only sets a flag there and lets a 1 s UI
  timer debounce and refresh.
- Turning the controller off makes the BLE node disconnected or removes it, which the
  8 s controller poll also catches. A confirmed disconnect maps to `BatteryState.Off`
  immediately and clears the cached level, so the old 100% cannot reappear as stale.
- The mouse has no equivalent event: the dongle stays enumerated and simply answers
  `0x04` when the mouse is asleep **or** switched off. With no way to tell those apart,
  a failed poll is reported as disconnected (after the source's own retries) and the
  monitor goes straight to `Off`, rather than showing a stale percentage for minutes.
  The cost is that an idle mouse shows as not responding until it is moved and the next
  8 s poll succeeds.

## Dead ends — do not repeat

- **XInput battery query** over USB or the 2.4 GHz dongle: returns the placeholder
  `wired` / `full`, never a percentage.
- **D-input over the dongle**: exposes no battery data.
- **8BitDo vendor software**: not required, and its protocol is not documented here.
- **USBPcap HID capture of the dongle**: no useful battery traffic.
- **BLE node battery property while the node is disconnected**: a cached value from the
  last Bluetooth session, not the current level (see above).
