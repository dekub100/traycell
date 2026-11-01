namespace TrayCell.Interop;

/// <summary>
/// Listens for PnP device-instance changes (arrival, start, removal) so the app can
/// refresh as soon as a device appears or disappears instead of waiting for the next
/// poll. Callbacks arrive on a system thread, so subscribers must marshal to their own
/// thread before touching UI.
/// </summary>
public sealed class DeviceChangeNotifier : IDisposable
{
    private readonly CfgMgr32.CmNotifyCallback _callback;
    private IntPtr _notification;
    private bool _disposed;

    public DeviceChangeNotifier()
    {
        // Held in a field so the GC cannot collect the delegate under the native callback.
        _callback = OnNotification;

        var result = CfgMgr32.RegisterDeviceNotifications(_callback, out var notification);
        if (result == 0)
        {
            _notification = notification;
        }
        else
        {
            LastError = result;
        }
    }

    /// <summary>True when the OS accepted the registration; false means polling alone.</summary>
    public bool IsActive => _notification != IntPtr.Zero;

    /// <summary>The CONFIGRET returned by a failed registration, or 0.</summary>
    public int LastError { get; }

    /// <summary>Raised on a system thread whenever a device instance arrives, starts or is removed.</summary>
    public event Action? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_notification != IntPtr.Zero)
        {
            CfgMgr32.UnregisterNotification(_notification);
            _notification = IntPtr.Zero;
        }

        GC.KeepAlive(_callback);
    }

    private uint OnNotification(IntPtr notify, IntPtr context, uint action, IntPtr eventData, uint eventDataSize)
    {
        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // Never let a subscriber's exception unwind into the PnP callback.
        }

        return 0; // CR_SUCCESS
    }
}
