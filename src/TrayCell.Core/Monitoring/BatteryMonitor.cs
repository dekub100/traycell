using TrayCell.Devices;

namespace TrayCell.Monitoring;

public enum BatteryState
{
    /// <summary>Fresh value read just now.</summary>
    Ok,

    /// <summary>Read failed, but a recent value exists; it is shown as stale.</summary>
    Stale,

    /// <summary>No usable value at all (device off, unplugged, or never seen).</summary>
    Off,

    /// <summary>
    /// Connected, but the transport exposes no percentage (XInput over USB or the
    /// 2.4 GHz dongle reports only "wired / full").
    /// </summary>
    Wired,
}

public sealed record DeviceSnapshot(
    string Name,
    int? Level,
    bool? IsCharging,
    BatteryState State,
    DateTimeOffset? LastUpdated,
    string? Detail)
{
    public string ToolTipText() => State switch
    {
        BatteryState.Ok when IsCharging == true => $"{Name}: {Level}% (charging)",
        BatteryState.Ok => $"{Name}: {Level}%",
        BatteryState.Stale => $"{Name}: {Level}% (stale, {Age()})",
        BatteryState.Wired => $"{Name}: connected, no battery level",
        _ => $"{Name}: unavailable",
    };

    private string Age()
    {
        if (LastUpdated is not { } seen)
        {
            return "unknown age";
        }

        var age = DateTimeOffset.Now - seen;
        return age.TotalMinutes < 1
            ? $"{(int)age.TotalSeconds}s old"
            : $"{(int)age.TotalMinutes}m old";
    }
}

/// <summary>
/// Polls every source and applies the Ok / Stale / Wired / Off rule: a number is never
/// shown as current if the device stopped answering, and a device that is confirmed
/// gone drops its cached value immediately.
/// </summary>
public sealed class BatteryMonitor
{
    private readonly IBatterySource[] _sources;
    private readonly Dictionary<string, (int Level, bool? Charging)> _lastValue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastSuccess = new(StringComparer.Ordinal);
    private readonly TimeSpan _staleAfter;
    private readonly object _gate = new();

    public BatteryMonitor(IEnumerable<IBatterySource> sources, TimeSpan? staleAfter = null)
    {
        _sources = sources.ToArray();
        _staleAfter = staleAfter ?? TimeSpan.FromMinutes(3);
    }

    /// <summary>
    /// Polls every source, or only <paramref name="sourceName"/> when given, so a cheap
    /// source can be refreshed more often than an expensive one.
    /// </summary>
    public IReadOnlyList<DeviceSnapshot> Poll(string? sourceName = null)
    {
        var sources = sourceName is null
            ? _sources
            : _sources.Where(source => string.Equals(source.Name, sourceName, StringComparison.OrdinalIgnoreCase)).ToArray();

        var snapshots = new List<DeviceSnapshot>(sources.Length);

        foreach (var source in sources)
        {
            BatteryReading reading;
            try
            {
                reading = source.Read();
            }
            catch (Exception exception)
            {
                reading = BatteryReading.Unavailable(exception.Message);
            }

            snapshots.Add(ToSnapshot(source, reading));
        }

        return snapshots;
    }

    private DeviceSnapshot ToSnapshot(IBatterySource source, BatteryReading reading)
    {
        lock (_gate)
        {
            if (reading is { Success: true, Level: { } level })
            {
                _lastValue[source.Name] = (level, reading.IsCharging);
                _lastSuccess[source.Name] = reading.ObservedAt;
                return new DeviceSnapshot(source.Name, level, reading.IsCharging, BatteryState.Ok, reading.ObservedAt, reading.Detail);
            }

            // Connected but unmetered: say so instead of showing a stale number or "off".
            if (reading is { Presence: BatteryPresence.Present, Level: null })
            {
                return new DeviceSnapshot(source.Name, null, null, BatteryState.Wired, reading.ObservedAt, reading.Detail);
            }

            // Confirmed gone: forget the cached value so it cannot resurface as stale.
            if (reading.Presence == BatteryPresence.Absent)
            {
                _lastValue.Remove(source.Name);
                _lastSuccess.Remove(source.Name);
                return new DeviceSnapshot(source.Name, null, null, BatteryState.Off, null, reading.Detail);
            }

            if (_lastValue.TryGetValue(source.Name, out var cached)
                && _lastSuccess.TryGetValue(source.Name, out var seen)
                && DateTimeOffset.Now - seen <= _staleAfter)
            {
                return new DeviceSnapshot(source.Name, cached.Level, cached.Charging, BatteryState.Stale, seen, reading.Detail);
            }

            return new DeviceSnapshot(
                source.Name, null, null, BatteryState.Off,
                _lastSuccess.TryGetValue(source.Name, out var lastSeen) ? lastSeen : null,
                reading.Detail);
        }
    }
}
