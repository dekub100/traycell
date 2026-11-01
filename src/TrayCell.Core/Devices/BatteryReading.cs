namespace TrayCell.Devices;

/// <summary>Result of one read attempt against a device.</summary>
public readonly record struct BatteryReading(
    bool Success,
    int? Level,
    bool? IsCharging,
    string? Detail,
    DateTimeOffset ObservedAt,
    bool Present)
{
    public static BatteryReading Ok(int level, bool? charging = null, string? detail = null) =>
        new(true, Math.Clamp(level, 0, 100), charging, detail, DateTimeOffset.Now, true);

    /// <summary>
    /// The device is connected, but its transport exposes no percentage. XInput over a
    /// USB cable or the 2.4 GHz dongle only ever reports "wired / full", so the honest
    /// answer is a connection without a number, not a made-up level.
    /// </summary>
    public static BatteryReading Connected(string detail) =>
        new(false, null, null, detail, DateTimeOffset.Now, true);

    public static BatteryReading Unavailable(string detail) =>
        new(false, null, null, detail, DateTimeOffset.Now, false);
}

/// <summary>Anything that can report a battery level, synchronously, without throwing.</summary>
public interface IBatterySource
{
    string Name { get; }

    BatteryReading Read();
}
