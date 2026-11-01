using TrayCell.Interop;

namespace TrayCell.Devices.Controller;

/// <summary>
/// Reads the Bluetooth battery percentage that Windows itself exposes for a BLE
/// device (the same value shown in Settings), via the device property store.
/// Only works while the device is connected over Bluetooth.
/// </summary>
public sealed class ControllerBatterySource : IBatterySource
{
    public const string DefaultFriendlyName = "8BitDo Ultimate 2C Wireless";

    private readonly string _friendlyName;

    public ControllerBatterySource(string? friendlyName = null) =>
        _friendlyName = string.IsNullOrWhiteSpace(friendlyName) ? DefaultFriendlyName : friendlyName;

    public string Name => _friendlyName;

    public BatteryReading Read()
    {
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

            if (CfgMgr32.TryReadUInt32(deviceInstance, DevPropKeys.BluetoothBatteryLevel) is { } level && level <= 100)
            {
                return BatteryReading.Ok((int)level, null, instanceId);
            }

            return BatteryReading.Unavailable("connected but Windows reports no battery level");
        }

        return BatteryReading.Unavailable("not connected over Bluetooth");
    }
}
