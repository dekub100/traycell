using TrayCell.Interop;

namespace TrayCell.Diagnostics;

/// <summary>Read-only views of the PnP device tree, used by the probe and for troubleshooting.</summary>
public static class PnpDiagnostics
{
    public readonly record struct BleDevice(string InstanceId, string? FriendlyName, uint? BatteryLevel);

    /// <summary>Every present BLE device node with the battery property Windows exposes.</summary>
    public static IEnumerable<BleDevice> BluetoothDevices()
    {
        foreach (var instanceId in CfgMgr32.EnumeratePresentDeviceIds("BTHLE"))
        {
            if (!CfgMgr32.TryOpenDevice(instanceId, out var deviceInstance))
            {
                continue;
            }

            yield return new BleDevice(
                instanceId,
                CfgMgr32.TryReadString(deviceInstance, DevPropKeys.FriendlyName),
                CfgMgr32.TryReadUInt32(deviceInstance, DevPropKeys.BluetoothBatteryLevel));
        }
    }
}
