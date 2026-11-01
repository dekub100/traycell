using TrayCell.Devices;
using TrayCell.Devices.Controller;
using TrayCell.Devices.Razer;
using TrayCell.Diagnostics;
using TrayCell.Hid;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
var includeAllPids = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
var settleIndex = Array.FindIndex(args, a => a.Equals("--settle", StringComparison.OrdinalIgnoreCase));
var settleMs = settleIndex >= 0 && settleIndex + 1 < args.Length && int.TryParse(args[settleIndex + 1], out var parsedSettle) ? parsedSettle : 60;
var secondsIndex = Array.FindIndex(args, a => a.Equals("--seconds", StringComparison.OrdinalIgnoreCase));
var watchSeconds = secondsIndex >= 0 && secondsIndex + 1 < args.Length && int.TryParse(args[secondsIndex + 1], out var parsedSeconds) ? parsedSeconds : 30;

if (command is "-h" or "help" or "--help")
{
    PrintUsage();
    return 0;
}

try
{
    switch (command)
    {
        case "controller":
            ProbeController();
            break;
        case "razer":
            ProbeRazer(includeAllPids, settleMs);
            break;
        case "battery":
            ReadBattery(settleMs);
            break;
        case "reportids":
            ProbeReportIds(settleMs);
            break;
        case "watch":
            WatchRazer(settleMs, watchSeconds);
            break;
        case "hid":
            DumpHid();
            break;
        case "all":
            ProbeController();
            Console.WriteLine();
            ProbeRazer(includeAllPids, settleMs);
            break;
        default:
            PrintUsage();
            return 2;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception}");
    return 1;
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("""
        TrayCell probe

          controller   Read the 8BitDo battery via the Windows Bluetooth device property
          razer        Enumerate Razer HID collections and try the battery query on each
                       (--all also tries PIDs other than the Viper V3 Pro / SE)
          hid          Dump every present HID collection with its usage page/usage
          all          Both device probes (default)
        """);
}

static void ProbeController()
{
    Console.WriteLine("== 8BitDo Ultimate 2C - Bluetooth PnP property ==");

    var devices = PnpDiagnostics.BluetoothDevices().ToList();
    if (devices.Count == 0)
    {
        Console.WriteLine("  no BLE device nodes present");
    }

    foreach (var device in devices)
    {
        var battery = device.BatteryLevel is { } level ? $"{level}%" : "no battery property";
        Console.WriteLine($"  {device.FriendlyName ?? "(no name)"} | {battery} | {device.InstanceId}");
    }

    var source = new ControllerBatterySource();
    var reading = source.Read();
    Console.WriteLine();
    Console.WriteLine(reading.Success
        ? $"  RESULT: {source.Name} = {reading.Level}%"
        : $"  RESULT: unavailable - {reading.Detail}");
}

static void ProbeRazer(bool includeAllPids, int settleMs)
{
    Console.WriteLine("== Razer Viper V3 Pro SE - HID feature report ==");

    var collections = HidCollection.Enumerate(0x1532)
        .OrderBy(c => c.ProductId)
        .ThenBy(c => c.UsagePage)
        .ThenBy(c => c.Usage)
        .ToList();

    if (collections.Count == 0)
    {
        Console.WriteLine("  no HID collections with VID_1532 found (dongle unplugged?)");
        return;
    }

    ushort[] known = includeAllPids ? [] : [0x00df, 0x00de, 0x00c1, 0x00c0];
    Console.WriteLine($"  {collections.Count} collection(s):");
    Console.WriteLine();

    var answers = 0;

    foreach (var collection in collections)
    {
        var isCandidate = known.Length == 0 || known.Contains(collection.ProductId);
        var marker = isCandidate ? "candidate" : "skipped  ";
        Console.WriteLine($"  [{marker}] {collection.Describe()}");
        Console.WriteLine($"             {collection.Path}");

        if (!isCandidate)
        {
            continue;
        }

        if (!collection.TryFeatureTransaction(RazerReport.CreateBatteryLevelRequest(), out var raw, out var error))
        {
            Console.WriteLine($"             query failed: {error}");
            continue;
        }

        // A wireless mouse that has gone idle often answers 0x04 (no response) on the first
        // few attempts and starts answering once the link wakes up.
        var rawAttempts = new List<byte[]> { raw };
        for (var attempt = 0; attempt < 5 && RazerReport.TryParse(raw, out var probe) && !probe.IsSuccess; attempt++)
        {
            Thread.Sleep(200);
            if (!collection.TryFeatureTransaction(RazerReport.CreateBatteryLevelRequest(), out raw, out error))
            {
                Console.WriteLine($"             retry {attempt + 1} failed: {error}");
                break;
            }

            rawAttempts.Add(raw);
        }

        var statuses = rawAttempts
            .Select(bytes => RazerReport.TryParse(bytes, out var parsed) ? $"0x{parsed.Status:X2}" : "?");
        Console.WriteLine($"             statuses: {string.Join(" -> ", statuses)}");
        Console.WriteLine($"             raw: {Hex(raw, 16)}");

        if (collection.SupportsReport(RazerReport.Length))
        {
            var firmware = RazerReport.Create(0x00, 0x81);
            var ok = collection.TryFeatureTransaction(firmware, out var fwRaw, out var fwError, TimeSpan.FromMilliseconds(settleMs));
            var fwStatus = ok && RazerReport.TryParse(fwRaw, out var fwParsed) ? $"0x{fwParsed.Status:X2} {fwParsed.Describe()}" : fwError;
            Console.WriteLine($"             firmware query: {fwStatus}");
        }

        if (!RazerReport.TryParse(raw, out var response))
        {
            Console.WriteLine("             response too short to parse");
            continue;
        }

        Console.WriteLine($"             {response.Describe()}");

        if (response.IsSuccess && response.Argument1 is >= 0 and <= 100)
        {
            answers++;
            var charging = collection.TryFeatureTransaction(RazerReport.CreateChargingStatusRequest(), out var rawCharging, out _, TimeSpan.FromMilliseconds(settleMs))
                && RazerReport.TryParse(rawCharging, out var chargingResponse)
                && chargingResponse.IsSuccess
                    ? $"charging={chargingResponse.Argument1}"
                    : "charging=unknown";

            Console.WriteLine($"             MATCH: level={response.Argument1}% {charging}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(answers > 0
        ? $"  RESULT: {answers} collection(s) answered the battery query"
        : "  RESULT: no collection answered the battery query");
}

static void ReadBattery(int settleMs)
{
    using var collection = HidCollection.Enumerate(0x1532)
        .FirstOrDefault(c => c.FeatureReportByteLength >= RazerReport.Length);

    if (collection is null)
    {
        Console.WriteLine("no Razer collection with a feature report found");
        return;
    }

    var settle = TimeSpan.FromMilliseconds(settleMs);
    Console.WriteLine($"reading battery (settle {settleMs} ms, 1 attempt/500 ms) - move the mouse if it is asleep");

    for (var attempt = 0; attempt < 40; attempt++)
    {
        if (attempt > 0)
        {
            Thread.Sleep(500);
        }

        if (!collection.TryFeatureTransaction(RazerReport.CreateBatteryLevelRequest(), out var raw, out var error, settle))
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {error}");
            continue;
        }

        if (!RazerReport.TryParse(raw, out var parsed))
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} short response");
            continue;
        }

        Console.WriteLine($"{DateTime.Now:HH:mm:ss} status=0x{parsed.Status:X2} crc={(parsed.CrcValid ? "ok" : "bad")} " +
                          $"args=[{string.Join(",", parsed.Arguments.Take(10))}] raw={Hex(raw, 12)}");

        if (!parsed.IsSuccess)
        {
            continue;
        }

        var chargeOk = collection.TryFeatureTransaction(
            RazerReport.CreateChargingStatusRequest(), out var rawCharge, out var chargeError, settle);

        var chargeText = chargeOk && RazerReport.TryParse(rawCharge, out var charge)
            ? $"status=0x{charge.Status:X2} args=[{string.Join(",", charge.Arguments.Take(6))}]"
            : chargeError;

        Console.WriteLine($"             charging query: {chargeText}");
        return;
    }
}

static void ProbeReportIds(int settleMs)
{
    using var collection = HidCollection.Enumerate(0x1532)
        .FirstOrDefault(c => c.FeatureReportByteLength >= RazerReport.Length);

    if (collection is null)
    {
        Console.WriteLine("no Razer collection with a feature report found");
        return;
    }

    Console.WriteLine($"probing report ids on {collection.Describe()}");

    for (byte reportId = 0; reportId <= 5; reportId++)
    {
        var request = RazerReport.CreateBatteryLevelRequest();
        var ok = collection.TryFeatureTransaction(
            request, out var raw, out var error, TimeSpan.FromMilliseconds(settleMs), reportId);

        var detail = ok && RazerReport.TryParse(raw, out var parsed)
            ? $"status=0x{parsed.Status:X2} args[0..3]={string.Join(",", parsed.Arguments.Take(4))}"
            : error;

        Console.WriteLine($"  report id 0x{reportId:X2}: {detail}");
    }
}

static void WatchRazer(int settleMs, int seconds)
{
    using var collection = HidCollection.Enumerate(0x1532)
        .FirstOrDefault(c => c.FeatureReportByteLength >= RazerReport.Length);

    if (collection is null)
    {
        Console.WriteLine("no Razer collection with a feature report found");
        return;
    }

    Console.WriteLine($"watching {collection.Describe()} for {seconds}s - MOVE THE MOUSE NOW");

    var deadline = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < deadline)
    {
        var ok = collection.TryFeatureTransaction(
            RazerReport.CreateBatteryLevelRequest(), out var raw, out var error, TimeSpan.FromMilliseconds(settleMs));

        if (ok && RazerReport.TryParse(raw, out var parsed))
        {
            if (parsed.IsSuccess)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} SUCCESS level={parsed.Argument1}%");
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} status=0x{parsed.Status:X2}");
        }
        else
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {error}");
        }

        Thread.Sleep(700);
    }
}

static void DumpHid()
{
    var log = new List<string>();
    var collections = HidCollection.Enumerate(log: log);
    Console.WriteLine($"== all HID collections ({collections.Count}) ==");

    foreach (var collection in collections)
    {
        Console.WriteLine($"  {collection.Describe()}");
        Console.WriteLine($"    {collection.Path}");
    }

    foreach (var line in log)
    {
        Console.WriteLine($"  diag: {line}");
    }
}

static string Hex(byte[] data, int count) =>
    string.Join(' ', data.Take(count).Select(b => b.ToString("X2")));
