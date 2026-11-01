# Project handoff: system tray battery monitor (Razer Viper V3 Pro + 8BitDo Ultimate 2C)

Date of research: 2026-10-31. Platform assumed: Windows (never explicitly confirmed by the user, but all tooling found and all tests run were Windows).

## Goal

A system tray app showing the current battery level of:

- Razer Viper V3 Pro **SE** (mouse). The user confirmed from first-hand experience that the SE has a different dongle and different hardware IDs than the standard V3 Pro, so projects written for the standard V3 Pro will likely not match it out of the box.
- 8BitDo Ultimate 2C Wireless (controller)

The user does not play competitive games on the controller, so latency is not a priority.

## Status summary

| Device | Feasible | Transport | Status |
|---|---|---|---|
| Razer Viper V3 Pro SE | Probably (unverified for the SE specifically) | Razer proprietary HID feature reports (OpenRazer-style class 0x07 commands) | Existing open-source tray projects target the standard V3 Pro, not the SE. Not yet run or inspected by us. SE-specific values (PIDs, and possibly command bytes/transaction ID) still need to be obtained. |
| 8BitDo Ultimate 2C | Yes, over Bluetooth only | Windows Bluetooth battery device property, device is BLE (`BTHLE\DEV_...`) | Confirmed readable in Device Manager (46%, earlier 48% in Settings). Not yet read from code. |

## Decisions already made

1. Controller will be used in **Bluetooth mode** to get battery readout. Reasons: battery data is not available on 2.4G (see tests below), Bluetooth has longer rated battery life, and the user does not need low latency.
2. Latency/compatibility trade-off accepted. A quick test showed Need for Speed: Underground 2 displays the **wrong input prompts** in its controls menu over Bluetooth but the controller **fully works**. Treated as a cosmetic issue (old game, likely sees a generic D-input device). Other games not yet checked.
3. Recommended implementation language: **C#** (PnP/WinRT built in, `NotifyIcon` for tray, HidSharp for Razer HID). Rust (`windows`, `hidapi`, `tray-icon` crates) is an alternative. Not final; confirm with user.
4. Start by polling the PnP battery property for the controller. Move to GATT only if stale values appear.

## Test results (what was actually run on the user's machine)

### 8BitDo Ultimate 2C on 2.4G dongle, XInput test

`XInputGetBatteryInformation` via ctypes returned:

```
Slot 0: type=WIRED, level=FULL
```

while on 2.4G. Conclusion: the dongle does not implement the XInput battery query. `WIRED`/`FULL` is a placeholder. XInput route is ruled out. (Assumed no charging cable was attached; not explicitly confirmed by the user.)

Script used:

```python
import ctypes
from ctypes import wintypes

try:
    xinput = ctypes.WinDLL("xinput1_4.dll")
except OSError:
    xinput = ctypes.WinDLL("xinput1_3.dll")

class XINPUT_BATTERY_INFORMATION(ctypes.Structure):
    _fields_ = [("BatteryType", ctypes.c_ubyte),
                ("BatteryLevel", ctypes.c_ubyte)]

TYPES = {0: "DISCONNECTED", 1: "WIRED", 2: "ALKALINE", 3: "NIMH", 255: "UNKNOWN"}
LEVELS = {0: "EMPTY", 1: "LOW", 2: "MEDIUM", 3: "FULL"}

for idx in range(4):
    info = XINPUT_BATTERY_INFORMATION()
    rc = xinput.XInputGetBatteryInformation(idx, 0, ctypes.byref(info))
    if rc == 0:
        print(f"Slot {idx}: type={TYPES.get(info.BatteryType, info.BatteryType)}, "
              f"level={LEVELS.get(info.BatteryLevel, info.BatteryLevel)}")
    else:
        print(f"Slot {idx}: error {rc} (1167 = no controller in this slot)")
```

### D-input on 2.4G dongle

User concluded D-input does not work over the dongle after trying. Evidence in sources was mixed (one Reddit user claimed it worked with an unspecified button combination). Dropped. No HID capture or USBPcap work was done; none is planned.

### Bluetooth mode, Windows Settings

Windows Settings shows a battery percentage for the controller in Bluetooth mode (48% at first check).

### Bluetooth mode, Device Manager

Device Manager > Bluetooth > "8BitDo Ultimate 2C Wireless" > Properties > Details shows property `{104ea319-6ee2-4701-bd47-8ddbf425bbe5}[2]` = **46%**. It had read 48% earlier, so the value changes. Device instance path prefix is **`BTHLE\DEV...`**, meaning Bluetooth Low Energy.

The PowerShell scan below was suggested but the user has not reported its output.

## Controller: how to read the battery

### Option A (start here): poll the PnP property

Property key `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` on the device node. Documented by several independent sources, including a project that reads a Razer Basilisk V3 Pro over Bluetooth with it. Device class holding it varies (BLE devices under `Bluetooth`, classic under `System`), so scan all present devices:

```powershell
$key = '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2'
Get-PnpDevice -PresentOnly | ForEach-Object {
    $p = $_ | Get-PnpDeviceProperty -KeyName $key -ErrorAction SilentlyContinue |
         Where-Object { $_.Type -ne 'Empty' }
    if ($p) { "{0}  |  {1}  |  {2}%" -f $_.FriendlyName, $_.InstanceId, $p.Data }
}
```

In the app, avoid shelling out to PowerShell. Use native CfgMgr/SetupAPI (`CM_Get_DevNode_PropertyW`) or `System.Management`. Match the device by friendly name ("8BitDo Ultimate 2C Wireless"). Windows may create multiple device nodes per Bluetooth device; the percentage should match across them. Treat an empty/missing value as "disconnected".

Other useful property keys seen in a reference script: `{83DA6326-97A6-4088-9453-A1923F573B29} 15` (IsConnected) and `{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A} 11` / `5` (last connected time). Not verified on this device.

### Option B: GATT Battery Service

Standard BLE Battery Service (0x180F), Battery Level characteristic (0x2A19). Likely present because Windows builds the property from it, but **not verified** for this controller. Untested sketch (target `net8.0-windows10.0.19041.0`):

```csharp
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

var devices = await Windows.Devices.Enumeration.DeviceInformation
    .FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromDeviceName("8BitDo Ultimate 2C Wireless"));
using var ble = await BluetoothLEDevice.FromIdAsync(devices[0].Id);
var svc = await ble.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
var chr = await svc.Services[0].GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
var res = await chr.Characteristics[0].ReadValueAsync();
byte level = DataReader.FromBuffer(res.Value).ReadByte();
```

Caveats: the controller must be connected at read time; handle the disconnected case explicitly. Windows restricts direct access to HID-over-GATT, but the Battery Service is separate and should be readable (expected, unverified).

### Controller caveats

- Readout only works while the controller is connected over **Bluetooth**. No evidence that Bluetooth and 2.4G can be active at once. Do not rely on a cached value after switching to 2.4G; it can go stale silently. Show a stale/off state instead.
- Windows may lag in updating the property. Polling every 30-60 s is enough.
- 8BitDo rates the 2C at **19 h on 2.4G vs 32 h on Bluetooth** (480 mAh). Manufacturer figures.
- No measured latency found for the 2C on either transport. 8BitDo claims 1000 Hz polling on 2.4G and wired only, and states the figure is from an ideal lab environment. Gamepadla data for other 8BitDo models (not the 2C) suggests Bluetooth is noticeably slower than the dongle (for example about 30 ms vs about 10.7 ms average button latency on the Ultimate 2.4G), but that is a different controller and the site marks its comparisons as experimental and user-submitted.
- Some software identifies the 2C in Bluetooth mode as a generic D-input device (reported in a RetroArch forum thread; also seen with NFS Underground 2 on the user's machine).

## Mouse: Razer Viper V3 Pro SE

### SE vs standard V3 Pro (important)

- The user states the SE has a different dongle and different hardware ID than the standard V3 Pro. Search results agree it is a separate product with separate IDs.
- A driver database (treexy.com, Driver Fusion) lists the SE's HID interfaces as `USB\VID_1532&PID_00DE&MI_01` and `USB\VID_1532&PID_00DF&MI_01`, and the standard V3 Pro as `USB\VID_1532&PID_00C0&MI_01`. That source does not say which SE PID is wired and which is the dongle. **Read the real values from the user's machine** (Device Manager > mouse/dongle > Details > Hardware Ids) instead of trusting this list.
- Dongle options: Razer's SE announcement shows both the HyperSpeed and HyperPolling dongles; one retailer lists the SE box as containing the HyperSpeed dongle plus a USB dongle adapter. Confirm which dongle the user actually uses, since it affects the PID. A Razer Insider reply also says the standard V3 Pro's dongle is not interchangeable with the generic HyperPolling dongle.
- No source found documents the SE's battery command bytes or transaction ID. They may match the standard V3 Pro or other recent Razer wireless mice (OpenRazer's driver), but this is unverified. Whether OpenRazer lists the SE at all was not checked.

### Existing projects (target the standard V3 Pro, not the SE)

Razer mice report battery through proprietary HID feature reports. The commands are documented in OpenRazer's driver (class 0x07 battery commands). Open-source Windows tray projects use this without Synapse.

Projects found (none run or code-reviewed by us; expect to add the SE PIDs to whichever one you use):

- Viper V3 Pro specific, C#, color-coded tray icon: `razer-viper-battery-tray` under the GitHub user `shirokuronoir` (only the profile page https://github.com/shirokuronoir was seen).
- Viper V3 Pro specific, Rust, "no Synapse, no kernel drivers, no admin", updated May 6, 2026: listed on https://github.com/topics/no-synapse (repository name not captured).
- https://github.com/xzeldon/razer-battery-report (Rust; supports other Razer mice; documents how to add devices: PID, HID interface, usage page, usage, transaction ID in `devices.rs`). The Viper V3 Pro is not in its list.
- https://github.com/rMX666/Razer-Battery-Tray (Pascal/libusb; verified on Viper Ultimate, DeathAdder V3 Pro).
- https://github.com/Fan4Metal/razer_tray (Python, wxPython, pyusb; based on https://github.com/hsutungyu/razer-mouse-battery-windows).
- https://github.com/HeyOkay/HaloBattery (multi-vendor tray app; Razer class 0x07 commands; also reads some 8BitDo controllers in D-input mode over Bluetooth; the 2C was not listed in the release notes seen).
- OpenRazer driver reference for transaction IDs: https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c

Values needed and **not yet obtained** (take PIDs from the user's machine and command details from a working Razer project or OpenRazer, do not guess):

- USB VID (Razer is 0x1532) and the SE's PIDs for wired and dongle (wireless) connections (candidates from a driver database: 0x00DE, 0x00DF; verify)
- HID interface, usage page and usage to open
- Transaction ID for the battery query on this model
- Command bytes for battery level and charging state (class 0x07; see OpenRazer)

Caveats:

- Synapse or other RGB software sending lighting frames can interfere with reads (HaloBattery release note, issue #108).
- The mouse reports only while the dongle/cable link is active. An asleep mouse may return nothing. Cache the last value and show a stale state.
- Razer's own hardware battery behavior is not part of this project. (An unrelated Razer Insider thread about a V3 Pro battery dying quickly was seen; not relevant.)

## Proposed architecture

Single C# app (.NET, Windows), one process, two pollers on a 30-60 s timer:

1. Mouse poller: HidSharp, send Razer battery query over the dongle's feature-report interface, parse level and charging state.
2. Controller poller: read the Bluetooth battery PnP property for the `8BitDo Ultimate 2C Wireless` BLE device.
3. State model per device: `Level (0-100 or null)`, `Charging (bool?)`, `LastUpdated`, `State (Ok | Stale | Off)`. Never display an old number without marking it stale.
4. Tray UI: either two icons with the percentage drawn on them, or one icon with a combined tooltip. Two icons are easier to read at a glance. Windows 11 hides extra tray icons unless pinned.

Alternative considered: fork an existing Viper V3 Pro tray project and add the controller poller. Faster for the mouse side; decide with the user.

## Suggested next steps

1. Run the PowerShell scan with the controller connected over Bluetooth; confirm it lists the 2C with a percentage. (Not yet reported by the user.)
2. Get the SE's real hardware IDs from Device Manager for the dongle in use (and wired). Then take one of the Viper V3 Pro projects, add the SE PIDs, and see whether the battery readout works. If it returns nothing or garbage, the transaction ID or command bytes differ; check OpenRazer's driver for the nearest recent wireless mouse, or capture Synapse's traffic. Compare results against Synapse or the mouse's own indicator.
3. Check that the controller's drain behavior in the property is real over time (it moved 48% to 46% so far; confirm after more use or a charge).
4. Scaffold the C# app: project setup, controller poller, Razer HID stub, tray rendering.
5. Fill in the Razer HID call using the values from step 2.
6. Add disconnect and stale handling for both devices.
7. Check the games the user plays for input prompt problems in Bluetooth mode (only NFS Underground 2 tested so far).

## Open questions for the user

- Confirm the OS is Windows and which version (Windows 10 or 11).
- Fork an existing Viper V3 Pro tray project or write everything in C#?
- Two tray icons or one combined icon/tooltip?
- Does the mouse also need to be monitored while connected by cable (charging)?
- Which dongle is used with the SE (HyperSpeed or HyperPolling), and what are the exact hardware IDs for the dongle and the wired connection?

## Dead ends (do not repeat)

- XInput battery query on the 2.4G dongle: returns WIRED/FULL, no real data.
- D-input over 2.4G: user concluded it does not work; no known button combination.
- Vendor software: it was unclear whether 8BitDo Ultimate Software supports the 2C beyond firmware updates, and no source says it shows battery. Not pursued.
- HID capture (USBPcap/Wireshark) of the dongle: not done and no longer needed now that Bluetooth works.

## Notes on information quality

- Verified on the user's machine: XInput result, Bluetooth battery value in Settings and Device Manager, `BTHLE` prefix, NFS Underground 2 behavior.
- From search results only (not independently tested): the Razer tray project capabilities, the PnP property key behavior on other devices, rated battery life, latency data.
- Unverified: the GATT Battery Service being present on the 2C, Viper V3 Pro SE PIDs and command values, whether Windows keeps updating the property while the controller is connected over a long session.
