namespace TrayCell.Devices;

/// <summary>Result of one read attempt against a device.</summary>
public readonly record struct BatteryReading(
    bool Success,
    int? Level,
    bool? IsCharging,
    string? Detail,
    DateTimeOffset ObservedAt)
{
    public static BatteryReading Ok(int level, bool? charging = null, string? detail = null) =>
        new(true, Math.Clamp(level, 0, 100), charging, detail, DateTimeOffset.Now);

    public static BatteryReading Unavailable(string detail) =>
        new(false, null, null, detail, DateTimeOffset.Now);
}

/// <summary>Anything that can report a battery level, synchronously, without throwing.</summary>
public interface IBatterySource
{
    string Name { get; }

    BatteryReading Read();
}
