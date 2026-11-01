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

- **Battery level**: class `0x07`, id `0x80`, `data_size 0x02` → level in `arguments[1]` (0–100).
- **Charging status**: class `0x07`, id `0x84`, `data_size 0x02` → `arguments[1]` = `0`/`1`.
- **CRC**: `crc = bytes[2] ^ bytes[3] ^ ... ^ bytes[87]`, stored at index 88.
- **Response check**: `status == 0x02` means success (`0x03` failure, `0x04` timeout/no response).
- The SE wireless battery query uses transaction id `0x1f`, identical to the standard V3 Pro
  (OpenRazer `razer_attr_read_charge_level`). No SE-specific command variants found.

Sources: `openrazer/driver/razermouse_driver.c`, `razermouse_driver.h`,
`razerchromacommon.c`, `razercommon.h`, `razercommon.c` at master.

## Remaining unknown

Which HID top-level collection on PID `0x00DF` accepts the feature report, and whether
Windows needs the report-ID byte prefixed (91-byte write, ID `0x00`) or a bare 90-byte write.
That is a five-minute C# probe: enumerate `VID_1532&PID_00DF`, try each parent collection with
a 90/91-byte feature write, and look for `status == 0x02` plus a plausible percentage.
