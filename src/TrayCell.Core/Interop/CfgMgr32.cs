using System.Runtime.InteropServices;

namespace TrayCell.Interop;

/// <summary>A DEVPROPKEY: property set GUID plus property id.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DevPropKey(Guid formatId, uint propertyId)
{
    public Guid FormatId = formatId;
    public uint PropertyId = propertyId;
}

/// <summary>Well-known device property keys used by TrayCell.</summary>
internal static class DevPropKeys
{
    /// <summary>DEVPKEY_Device_FriendlyName</summary>
    public static readonly DevPropKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    /// <summary>DEVPKEY_Bluetooth_BatteryLevel - the value Windows Settings shows.</summary>
    public static readonly DevPropKey BluetoothBatteryLevel = new(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);

    /// <summary>DEVPKEY_Device_DevNodeStatus - DEVICE_STATUS bitmask, not the same as "connected".</summary>
    public static readonly DevPropKey DevNodeStatus = new(new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"), 2);

    /// <summary>DEVPKEY_Device_ProblemCode - CM_PROB_* when the node has a problem.</summary>
    public static readonly DevPropKey ProblemCode = new(new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"), 3);
}

/// <summary>Minimal cfgmgr32 interop: device enumeration, property reads, PnP notifications.</summary>
internal static class CfgMgr32
{
    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1a;

    // CM_NOTIFY_FILTER_TYPE_DEVICEINSTANCE
    private const uint NotifyFilterTypeDeviceInstance = 2;

    // CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES
    private const uint NotifyFilterAllDeviceInstances = 0x0000_0002;

    /// <summary>Callback invoked by cfgmgr32 on a system thread for each device event.</summary>
    internal delegate uint CmNotifyCallback(
        IntPtr notify, IntPtr context, uint action, IntPtr eventData, uint eventDataSize);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CmNotifyFilter
    {
        public uint Size;
        public uint Flags;
        public uint FilterType;
        public uint Reserved;
        public CmNotifyFilterUnion Union;
    }

    /// <summary>
    /// Native union: GUID ClassGuid / HANDLE hTarget / WCHAR InstanceId[MAX_DEVICE_ID_LEN].
    /// MAX_DEVICE_ID_LEN is 200, so the union is 400 bytes. Zeroed for "all instances".
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 400)]
    internal struct CmNotifyFilterUnion
    {
        [FieldOffset(0)] public Guid DeviceInterfaceClass;
        [FieldOffset(0)] public IntPtr DeviceHandle;
    }

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Register_Notification(
        ref CmNotifyFilter filter, IntPtr context, CmNotifyCallback callback, out IntPtr notification);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Unregister_Notification(IntPtr notification);

    /// <summary>Registers for device-instance events (arrival, start, removal).</summary>
    internal static int RegisterDeviceNotifications(CmNotifyCallback callback, out IntPtr notification)
    {
        var filter = new CmNotifyFilter
        {
            Size = (uint)Marshal.SizeOf<CmNotifyFilter>(),
            Flags = NotifyFilterAllDeviceInstances,
            FilterType = NotifyFilterTypeDeviceInstance,
        };

        return CM_Register_Notification(ref filter, IntPtr.Zero, callback, out notification);
    }

    internal static void UnregisterNotification(IntPtr notification) => CM_Unregister_Notification(notification);

    // CM_GETIDLIST_FILTER_PRESENT | CM_GETIDLIST_FILTER_ENUMERATOR
    private const uint FilterPresent = 0x0000_0100;
    private const uint FilterEnumerator = 0x0000_0001;

    private const uint PropTypeByte = 0x03;
    private const uint PropTypeUInt16 = 0x05;
    private const uint PropTypeUInt32 = 0x07;
    private const uint PropTypeUInt64 = 0x08;
    private const uint PropTypeString = 0x12;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out uint bufferLength, string? filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint deviceInstance, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint deviceInstance, ref DevPropKey key, out uint propertyType,
        [Out] byte[]? buffer, ref uint bufferSize, uint flags);

    /// <summary>Enumerates present device instance IDs, optionally limited to one enumerator (e.g. "BTHLE").</summary>
    public static IEnumerable<string> EnumeratePresentDeviceIds(string? enumerator = null)
    {
        // The enumerator name is only honoured together with CM_GETIDLIST_FILTER_ENUMERATOR;
        // without it the argument would be treated as an instance-id prefix.
        var flags = enumerator is null ? FilterPresent : FilterPresent | FilterEnumerator;

        if (CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags) != CrSuccess || length == 0)
        {
            yield break;
        }

        var buffer = new char[length];
        if (CM_Get_Device_ID_ListW(enumerator, buffer, length, flags) != CrSuccess)
        {
            yield break;
        }

        var start = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0')
            {
                continue;
            }

            if (i == start)
            {
                yield break; // double null terminator: end of list
            }

            yield return new string(buffer, start, i - start);
            start = i + 1;
        }
    }

    public static bool TryOpenDevice(string instanceId, out uint deviceInstance) =>
        CM_Locate_DevNodeW(out deviceInstance, instanceId, 0) == CrSuccess;

    /// <summary>Reads a numeric property. Returns null when the property is absent or another type.</summary>
    public static uint? TryReadUInt32(uint deviceInstance, DevPropKey key)
    {
        var raw = TryReadRaw(deviceInstance, key, 64);
        if (raw is not { } value)
        {
            return null;
        }

        return value.Type switch
        {
            PropTypeByte when value.Data.Length >= 1 => value.Data[0],
            PropTypeUInt16 when value.Data.Length >= 2 => BitConverter.ToUInt16(value.Data, 0),
            PropTypeUInt32 when value.Data.Length >= 4 => BitConverter.ToUInt32(value.Data, 0),
            PropTypeUInt64 when value.Data.Length >= 8 => (uint)BitConverter.ToUInt64(value.Data, 0),
            _ => null,
        };
    }

    /// <summary>Reads a string property. Returns null when absent or of another type.</summary>
    public static string? TryReadString(uint deviceInstance, DevPropKey key)
    {
        var raw = TryReadRaw(deviceInstance, key, 1024);
        if (raw is not { } value || value.Type != PropTypeString || value.Data.Length < 2)
        {
            return null;
        }

        var decoded = System.Text.Encoding.Unicode.GetString(value.Data);
        var terminator = decoded.IndexOf('\0');
        return (terminator >= 0 ? decoded[..terminator] : decoded).TrimEnd();
    }

    private static (uint Type, byte[] Data)? TryReadRaw(uint deviceInstance, DevPropKey key, int capacity)
    {
        var buffer = new byte[capacity];
        var size = (uint)buffer.Length;

        var result = CM_Get_DevNode_PropertyW(deviceInstance, ref key, out var type, buffer, ref size, 0);
        if (result == CrBufferSmall)
        {
            buffer = new byte[size];
            result = CM_Get_DevNode_PropertyW(deviceInstance, ref key, out type, buffer, ref size, 0);
        }

        if (result != CrSuccess || size == 0 || size > buffer.Length)
        {
            return null;
        }

        return (type, buffer[..(int)size]);
    }
}
