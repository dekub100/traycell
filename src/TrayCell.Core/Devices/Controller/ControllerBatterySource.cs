using TrayCell.Interop;

namespace TrayCell.Devices.Controller;

/// <summary>
/// Reads the Bluetooth battery percentage that Windows exposes for a BLE device
/// (the same value shown in Settings), via the device property store.
///
/// A paired-but-offline controller keeps its BTHLE node, along with the battery value
/// cached from the last Bluetooth session. That node is marked
/// <c>DN_DEVICE_DISCONNECTED</c>, and its level must never be reported as current.
/// When the controller is instead on a USB cable or the 2.4 GHz dongle, Windows
/// presents it through XInput, which reports only "wired / full" - so the result is a
/// connection without a percentage.
/// </summary>
public sealed class ControllerBatterySource : IBatterySource
{
    public const string DefaultFriendlyName = "8BitDo Ultimate 2C Wireless";

    /// <summary>DN_DEVICE_DISCONNECTED from cfgmgr32.h: paired, but not currently reachable.</summary>
    private const uint DeviceDisconnected = 0x0200_0000;

    private const ushort EightBitDoVendorId = 0x2DC8;

    private readonly string _friendlyName;

    public ControllerBatterySource(string? friendlyName = null) =>
        _friendlyName = string.IsNullOrWhiteSpace(friendlyName) ? DefaultFriendlyName : friendlyName;

    public string Name => _friendlyName;

    public BatteryReading Read()
    {
        var sawDisconnectedNode = false;

        // BLE device nodes live under the BTHLE enumerator.
        foreach (var instanceId in CfgMgr32.EnumeratePresentDeviceIds("BTHLE"))
        {
            if (!instanceId.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!CfgMgr32.TryOpenDevice(instanceId, out var deviceInstance))
            {
                continue;
            }

            var friendlyName = CfgMgr32.TryReadString(deviceInstance, DevPropKeys.FriendlyName);
            if (!string.Equals(friendlyName, _friendlyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (CfgMgr32.TryReadUInt32(deviceInstance, DevPropKeys.DevNodeStatus) is { } status
                && (status & DeviceDisconnected) != 0)
            {
                // Present but offline: the battery property is a stale cached value.
                sawDisconnectedNode = true;
                continue;
            }

            if (CfgMgr32.TryReadUInt32(deviceInstance, DevPropKeys.BluetoothBatteryLevel) is { } level && level <= 100)
            {
                return BatteryReading.Ok((int)level, null, instanceId);
            }

            return BatteryReading.Unavailable("connected over Bluetooth but Windows reports no battery level");
        }

        if (IsConnectedOverUsb())
        {
            return BatteryReading.Connected(sawDisconnectedNode
                ? "Bluetooth node is offline; connected over USB/XInput, which exposes no percentage"
                : "connected over USB/XInput, which exposes no percentage");
        }

        return BatteryReading.Unavailable(sawDisconnectedNode
            ? "not connected (the paired Bluetooth node is offline)"
            : "not connected over Bluetooth");
    }

    /// <summary>
    /// True when an XInput controller is live and an 8BitDo USB device is present.
    /// Both checks together keep a different XInput pad from being mistaken for this one.
    /// </summary>
    private static bool IsConnectedOverUsb() => XInputDevice.IsAnyConnected() && IsEightBitDoUsbPresent();

    private static bool IsEightBitDoUsbPresent()
    {
        var vendorId = $"VID_{EightBitDoVendorId:X4}";

        foreach (var instanceId in CfgMgr32.EnumeratePresentDeviceIds("USB"))
        {
            if (instanceId.Contains(vendorId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
