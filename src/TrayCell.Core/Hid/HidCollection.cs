using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TrayCell.Interop;

namespace TrayCell.Hid;

/// <summary>
/// One open HID top-level collection, with the metadata needed to talk to it
/// via feature reports (usage page/usage, report lengths) and a disposal handle.
/// </summary>
public sealed class HidCollection : IDisposable
{
    private readonly SafeFileHandle _handle;
    private bool _disposed;

    private HidCollection(string path, SafeFileHandle handle, string accessMode, HiddAttributes attributes, HidpCaps caps)
    {
        Path = path;
        _handle = handle;
        AccessMode = accessMode;
        VendorId = attributes.VendorId;
        ProductId = attributes.ProductId;
        VersionNumber = attributes.VersionNumber;
        UsagePage = caps.UsagePage;
        Usage = caps.Usage;
        InputReportByteLength = caps.InputReportByteLength;
        OutputReportByteLength = caps.OutputReportByteLength;
        FeatureReportByteLength = caps.FeatureReportByteLength;
    }

    public string Path { get; }
    public string AccessMode { get; }
    public ushort VendorId { get; }
    public ushort ProductId { get; }
    public ushort VersionNumber { get; }
    public ushort UsagePage { get; }
    public ushort Usage { get; }
    public int InputReportByteLength { get; }
    public int OutputReportByteLength { get; }
    public int FeatureReportByteLength { get; }

    /// <summary>True when this collection can carry a report of at least <paramref name="length"/> bytes.</summary>
    public bool SupportsReport(int length) => FeatureReportByteLength >= length;

    public string Describe() =>
        $"{VendorId:X4}:{ProductId:X4} up={UsagePage:X4} u={Usage:X4} " +
        $"feat={FeatureReportByteLength} in={InputReportByteLength} out={OutputReportByteLength} access={AccessMode}";

    /// <summary>
    /// Enumerates present HID collections, optionally narrowed by vendor/product id.
    /// The vendor/product filter is applied by parsing the device interface path,
    /// so unrelated devices are never opened.
    /// </summary>
    public static List<HidCollection> Enumerate(ushort? vendorId = null, ushort? productId = null, List<string>? log = null)
    {
        var results = new List<HidCollection>();
        var interfaceClass = HidNative.HidInterfaceClass;

        var deviceInfoSet = HidNative.SetupDiGetClassDevsW(
            ref interfaceClass, IntPtr.Zero, IntPtr.Zero,
            HidNative.DigcfPresent | HidNative.DigcfDeviceInterface);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            log?.Add($"SetupDiGetClassDevsW failed: {Marshal.GetLastWin32Error()}");
            return results;
        }

        log?.Add($"device info set acquired: 0x{deviceInfoSet.ToInt64():X}");

        try
        {
            var interfaceData = new SpDeviceInterfaceData
            {
                Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>(),
            };

            for (uint index = 0;
                 HidNative.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref interfaceClass, index, ref interfaceData);
                 index++)
            {
                var path = GetDevicePath(deviceInfoSet, ref interfaceData, log);
                if (path is null)
                {
                    continue;
                }

                if (vendorId is { } vid && ParsePathId(path, "vid_") != vid)
                {
                    continue;
                }

                if (productId is { } pid && ParsePathId(path, "pid_") != pid)
                {
                    continue;
                }

                var opened = TryOpen(path, log);
                if (opened is not null)
                {
                    results.Add(opened);
                }
            }

            log?.Add($"enumeration stopped at index {results.Count} kept");
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    /// <summary>
    /// Sends a feature report and reads the reply.
    /// <paramref name="request"/> is the payload without a report id; if the collection's
    /// feature report is one byte longer it is treated as having a leading report id of 0.
    /// </summary>
    public bool TryFeatureTransaction(byte[] request, out byte[] rawResponse, out string error, TimeSpan? settle = null, byte reportId = 0x00)
    {
        rawResponse = [];
        var length = FeatureReportByteLength;

        if (length <= 0)
        {
            error = "collection has no feature reports";
            return false;
        }

        var offset = length - request.Length;
        if (offset is < 0 or > 1)
        {
            error = $"feature report length {length} does not fit a {request.Length}-byte report";
            return false;
        }

        var writeBuffer = new byte[length];
        request.CopyTo(writeBuffer, offset);
        if (offset == 1)
        {
            writeBuffer[0] = reportId;
        }

        if (!HidNative.HidD_SetFeature(_handle, writeBuffer, (uint)length))
        {
            error = $"HidD_SetFeature failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        // Razer wireless receivers answer the read before the mouse has replied to the write,
        // unless the caller waits (OpenRazer uses 59900 us for Viper-class receivers).
        if (settle is { } delay && delay > TimeSpan.Zero)
        {
            Thread.Sleep(delay);
        }

        var readBuffer = new byte[length];
        if (!HidNative.HidD_GetFeature(_handle, readBuffer, (uint)length))
        {
            error = $"HidD_GetFeature failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        rawResponse = offset == 0 ? readBuffer : readBuffer[offset..];
        error = string.Empty;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }

    private static HidCollection? TryOpen(string path, List<string>? log = null)
    {
        // Windows denies write access to collections the system owns (mouse/keyboard),
        // and some vendor collections only allow read. Feature reports need read+write.
        (uint Access, string Label)[] modes =
        [
            (HidNative.GenericRead | HidNative.GenericWrite, "rw"),
            (HidNative.GenericWrite, "w"),
            (HidNative.GenericRead, "r"),
            (0, "query"),
        ];

        SafeFileHandle? handle = null;
        string accessMode = string.Empty;
        var lastError = 0;

        foreach (var (access, label) in modes)
        {
            var candidate = HidNative.CreateFileW(
                path, access,
                HidNative.FileShareRead | HidNative.FileShareWrite,
                IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

            if (!candidate.IsInvalid)
            {
                handle = candidate;
                accessMode = label;
                break;
            }

            lastError = Marshal.GetLastWin32Error();
            candidate.Dispose();
        }

        if (handle is null)
        {
            log?.Add($"CreateFile failed ({lastError}): {path}");
            return null;
        }

        try
        {
            var attributes = new HiddAttributes { Size = (uint)Marshal.SizeOf<HiddAttributes>() };
            if (!HidNative.HidD_GetAttributes(handle, ref attributes))
            {
                log?.Add($"HidD_GetAttributes failed ({Marshal.GetLastWin32Error()}): {path}");
                handle.Dispose();
                return null;
            }

            if (!HidNative.HidD_GetPreparsedData(handle, out var preparsed))
            {
                log?.Add($"HidD_GetPreparsedData failed ({Marshal.GetLastWin32Error()}): {path}");
                handle.Dispose();
                return null;
            }

            HidpCaps caps;
            try
            {
                // HidP_GetCaps returns an NTSTATUS; HIDP_STATUS_SUCCESS is 0x00110000, not 0.
                var status = HidNative.HidP_GetCaps(preparsed, out caps);
                if (status != HidNative.HidpStatusSuccess)
                {
                    log?.Add($"HidP_GetCaps failed (0x{status:X8}): {path}");
                    handle.Dispose();
                    return null;
                }
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }

            return new HidCollection(path, handle, accessMode, attributes, caps);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static string? GetDevicePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData, List<string>? log)
    {
        // First call is expected to fail with ERROR_INSUFFICIENT_BUFFER and report the size.
        _ = HidNative.SetupDiGetDeviceInterfaceDetailW(
            deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);

        if (requiredSize == 0)
        {
            log?.Add($"SetupDiGetDeviceInterfaceDetailW size query failed: {Marshal.GetLastWin32Error()}");
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // cbSize is 8 for the Unicode struct on 64-bit, 6 on 32-bit.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);

            return HidNative.SetupDiGetDeviceInterfaceDetailW(
                deviceInfoSet, ref interfaceData, buffer, requiredSize, out _, IntPtr.Zero)
                ? Marshal.PtrToStringUni(buffer + 4)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Reads "vid_1532" / "pid_00df" style ids out of a HID interface path.</summary>
    internal static ushort? ParsePathId(string path, string marker)
    {
        var at = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0 || at + marker.Length + 4 > path.Length)
        {
            return null;
        }

        return ushort.TryParse(path.AsSpan(at + marker.Length, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
