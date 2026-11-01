using TrayCell.Hid;

namespace TrayCell.Devices.Razer;

/// <summary>
/// Reads battery level and charging state from a Razer mouse or its dongle over
/// HID feature reports. Remembers which collection answered so polling stays cheap.
/// </summary>
public sealed class RazerBatterySource : IBatterySource, IDisposable
{
    private const ushort RazerVendorId = 0x1532;

    /// <summary>OpenRazer's RAZER_VIPER_MOUSE_RECEIVER_WAIT_US: wait after writing before reading.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(60);

    /// <summary>Viper V3 Pro SE wireless and wired, then standard V3 Pro as a fallback.</summary>
    private static readonly ushort[] DefaultProductIds = [0x00df, 0x00de, 0x00c1, 0x00c0];

    private readonly ushort[] _productIds;
    private HidCollection? _active;
    private bool _disposed;

    public RazerBatterySource(IEnumerable<ushort>? productIds = null) =>
        _productIds = productIds?.Distinct().ToArray() is { Length: > 0 } ids ? ids : DefaultProductIds;

    public string Name => "Razer Viper V3 Pro SE";

    public BatteryReading Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_active is not null)
        {
            var cached = Query(_active);
            if (cached.Success)
            {
                return cached;
            }

            // Dongle slept or was unplugged; fall through and rediscover.
            DisposeActive();
        }

        var failures = new List<string>();

        foreach (var candidate in HidCollection.Enumerate(RazerVendorId))
        {
            if (!_productIds.Contains(candidate.ProductId))
            {
                candidate.Dispose();
                continue;
            }

            if (!candidate.SupportsReport(RazerReport.Length))
            {
                candidate.Dispose();
                continue;
            }

            var reading = Query(candidate);
            if (reading.Success)
            {
                _active = candidate;
                return reading;
            }

            failures.Add($"{candidate.Describe()}: {reading.Detail}");
            candidate.Dispose();
        }

        // No dongle, or the dongle could not reach the mouse. The receiver has no "link
        // lost" event, and a sleeping mouse answers exactly like a switched-off one
        // (status 0x04), so the honest state is "not responding" rather than a stale
        // percentage. The next successful poll restores the number.
        return BatteryReading.Disconnected(failures.Count == 0
            ? "no Razer dongle or mouse found"
            : $"mouse not responding (asleep or switched off): {string.Join("; ", failures)}");
    }

    private static BatteryReading Query(HidCollection collection)
    {
        // Viper-class wireless receivers need ~60 ms between the write and the read
        // (OpenRazer RAZER_VIPER_MOUSE_RECEIVER_WAIT_US = 59900). A sleeping mouse answers
        // 0x04 (no response), a busy one 0x01, and a just-waking one can return garbage
        // such as level 255, so retry a few times before reporting a failure.
        BatteryReading last = BatteryReading.Unavailable("no attempt made");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(250);
            }

            last = QueryOnce(collection);
            if (last.Success)
            {
                return last;
            }
        }

        return last;
    }

    private static BatteryReading QueryOnce(HidCollection collection)
    {
        if (!collection.TryFeatureTransaction(
                RazerReport.CreateBatteryLevelRequest(), out var raw, out var error, SettleDelay))
        {
            return BatteryReading.Unavailable(error);
        }

        if (!RazerReport.TryParse(raw, out var response))
        {
            return BatteryReading.Unavailable($"short response ({raw.Length} bytes)");
        }

        if (!response.IsSuccess)
        {
            return BatteryReading.Unavailable(response.Describe());
        }

        var rawLevel = response.Argument1;

        // 255 is both a valid full-charge value and the garbage frame seen while the
        // mouse wakes, so confirm it with a second read before trusting it.
        if (rawLevel == 255)
        {
            Thread.Sleep(100);
            if (collection.TryFeatureTransaction(
                    RazerReport.CreateBatteryLevelRequest(), out var confirmRaw, out _, SettleDelay)
                && RazerReport.TryParse(confirmRaw, out var confirmed)
                && confirmed.IsSuccess)
            {
                rawLevel = confirmed.Argument1;
            }
        }

        if (rawLevel is < 0 or > 255)
        {
            return BatteryReading.Unavailable($"implausible level {rawLevel} in {response.Describe()}");
        }

        // The device reports 0-255; OpenRazer scales it the same way (mamba.py, get_battery.py).
        var level = (int)Math.Round(rawLevel * 100.0 / 255.0);

        bool? charging = null;
        if (collection.TryFeatureTransaction(
                RazerReport.CreateChargingStatusRequest(), out var rawCharging, out _, SettleDelay)
            && RazerReport.TryParse(rawCharging, out var chargingResponse)
            && chargingResponse.IsSuccess)
        {
            charging = chargingResponse.Argument1 != 0;
        }

        return BatteryReading.Ok(level, charging, $"usage {collection.UsagePage:X4}/{collection.Usage:X4}, raw {rawLevel}");
    }

    private void DisposeActive()
    {
        _active?.Dispose();
        _active = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeActive();
    }
}
