namespace TrayCell.Devices;

/// <summary>What a read attempt learned about the device's presence.</summary>
public enum BatteryPresence
{
    /// <summary>The read failed and presence is unknown (for example, the mouse is asleep).</summary>
    Unknown,

    /// <summary>The device is there; a level may or may not be available.</summary>
    Present,

    /// <summary>The device is confirmed gone: unplugged, powered off, or not connected.</summary>
    Absent,
}

/// <summary>Result of one read attempt against a device.</summary>
public readonly record struct BatteryReading(
    bool Success,
    int? Level,
    bool? IsCharging,
    string? Detail,
    DateTimeOffset ObservedAt,
    BatteryPresence Presence)
{
    public static BatteryReading Ok(int level, bool? charging = null, string? detail = null) =>
        new(true, Math.Clamp(level, 0, 100), charging, detail, DateTimeOffset.Now, BatteryPresence.Present);

    /// <summary>
    /// The device is connected, but its transport exposes no percentage. XInput over a
    /// USB cable or the 2.4 GHz dongle only ever reports "wired / full", so the honest
    /// answer is a connection without a number, not a made-up level.
    /// </summary>
    public static BatteryReading Connected(string detail) =>
        new(false, null, null, detail, DateTimeOffset.Now, BatteryPresence.Present);

    /// <summary>
    /// The device is confirmed gone. Unlike <see cref="Unavailable"/>, this lets the
    /// monitor stop showing the last value immediately instead of calling it stale.
    /// </summary>
    public static BatteryReading Disconnected(string detail) =>
        new(false, null, null, detail, DateTimeOffset.Now, BatteryPresence.Absent);

    /// <summary>
    /// The read failed without establishing presence. The monitor keeps the last value
    /// as stale, because the device may simply be asleep (the Razer dongle cannot tell
    /// "asleep" from "off").
    /// </summary>
    public static BatteryReading Unavailable(string detail) =>
        new(false, null, null, detail, DateTimeOffset.Now, BatteryPresence.Unknown);
}

/// <summary>Anything that can report a battery level, synchronously, without throwing.</summary>
public interface IBatterySource
{
    string Name { get; }

    BatteryReading Read();
}
