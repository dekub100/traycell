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
/// Polls every source and applies the Ok / Stale / Off rule: a number is never
/// shown as current if the device stopped answering.
/// </summary>
public sealed class BatteryMonitor
{
    private readonly IBatterySource[] _sources;
    private readonly Dictionary<string, (int Level, bool? Charging)> _lastValue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastSuccess = new(StringComparer.Ordinal);
    private readonly TimeSpan _staleAfter;

    public BatteryMonitor(IEnumerable<IBatterySource> sources, TimeSpan? staleAfter = null)
    {
        _sources = sources.ToArray();
        _staleAfter = staleAfter ?? TimeSpan.FromMinutes(3);
    }

    public IReadOnlyList<DeviceSnapshot> Poll()
    {
        var snapshots = new List<DeviceSnapshot>(_sources.Length);

        foreach (var source in _sources)
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

            if (reading is { Success: true, Level: { } level })
            {
                _lastValue[source.Name] = (level, reading.IsCharging);
                _lastSuccess[source.Name] = reading.ObservedAt;
                snapshots.Add(new DeviceSnapshot(source.Name, level, reading.IsCharging, BatteryState.Ok, reading.ObservedAt, reading.Detail));
                continue;
            }

            // Connected but unmetered: report that instead of a stale number or "off".
            if (reading is { Present: true, Level: null })
            {
                snapshots.Add(new DeviceSnapshot(source.Name, null, null, BatteryState.Wired, reading.ObservedAt, reading.Detail));
                continue;
            }

            if (_lastValue.TryGetValue(source.Name, out var cached)
                && _lastSuccess.TryGetValue(source.Name, out var seen)
                && DateTimeOffset.Now - seen <= _staleAfter)
            {
                snapshots.Add(new DeviceSnapshot(source.Name, cached.Level, cached.Charging, BatteryState.Stale, seen, reading.Detail));
                continue;
            }

            snapshots.Add(new DeviceSnapshot(
                source.Name, null, null, BatteryState.Off,
                _lastSuccess.TryGetValue(source.Name, out var lastSeen) ? lastSeen : null,
                reading.Detail));
        }

        return snapshots;
    }
}
