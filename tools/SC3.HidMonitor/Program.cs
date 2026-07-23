using System.Text.Json;
using SC3.HidMonitor;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        var options = Options.Parse(args);
        if (options.ShowHelp)
        {
            Options.WriteHelp();
            return 0;
        }

        var devices = HidNative.EnumerateSc3Devices();
        devices = FilterByInterface(devices, options.Interface);

        if (options.Command == "list")
        {
            WriteDevices(devices, options.Json);
            return devices.Count == 0 ? 2 : 0;
        }

        WriteDevices(devices, json: false);
        Console.Error.WriteLine(
            $"Monitoring input reports for {options.DurationSeconds} second(s). Press Ctrl+C to stop early.");
        Console.WriteLine("timestamp\tinterface\tbytes\thex");

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        await InputReportMonitor.MonitorAsync(
            devices,
            TimeSpan.FromSeconds(options.DurationSeconds),
            cancellation.Token);
        return devices.Count == 0 ? 2 : 0;
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        Console.Error.WriteLine("Use --help for usage.");
        return 1;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Fatal error: {exception.Message}");
        return 1;
    }
}

static IReadOnlyList<HidDeviceDescriptor> FilterByInterface(
    IReadOnlyList<HidDeviceDescriptor> devices,
    string? interfaceName)
{
    if (interfaceName is null)
    {
        return devices;
    }

    return devices
        .Where(device => device.Interface.Equals(interfaceName, StringComparison.OrdinalIgnoreCase))
        .ToArray();
}

static void WriteDevices(IReadOnlyList<HidDeviceDescriptor> devices, bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    Console.WriteLine($"SC3 HID interfaces: {devices.Count}");
    foreach (var device in devices)
    {
        Console.WriteLine();
        Console.WriteLine($"Interface: {device.Interface}");
        Console.WriteLine($"Path: {device.Path}");
        Console.WriteLine($"VID:PID: {device.VendorId:X4}:{device.ProductId:X4}");
        Console.WriteLine($"Version: 0x{device.VersionNumber:X4}");
        Console.WriteLine($"UsagePage/Usage: 0x{device.UsagePage:X4}/0x{device.Usage:X4}");
        Console.WriteLine(
            $"Report bytes (input/output/feature): {device.InputReportByteLength}/" +
            $"{device.OutputReportByteLength}/{device.FeatureReportByteLength}");
        Console.WriteLine($"Manufacturer: {device.Manufacturer ?? "(not exposed)"}");
        Console.WriteLine($"Product: {device.Product ?? "(not exposed)"}");
        Console.WriteLine($"Serial: {device.SerialNumber ?? "(not exposed)"}");
        if (device.Error is not null)
        {
            Console.WriteLine($"Error: {device.Error}");
        }
    }
}

internal sealed record Options(
    string Command,
    int DurationSeconds,
    string? Interface,
    bool Json,
    bool ShowHelp)
{
    internal static Options Parse(string[] args)
    {
        var command = "list";
        var duration = 30;
        string? interfaceName = null;
        var json = false;
        var showHelp = false;

        var index = 0;
        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            command = args[0].ToLowerInvariant();
            index++;
        }

        if (command is not ("list" or "monitor"))
        {
            throw new ArgumentException($"Unknown command '{command}'. Expected list or monitor.");
        }

        while (index < args.Length)
        {
            switch (args[index])
            {
                case "--duration":
                    duration = ParseDuration(ReadValue(args, ref index, "--duration"));
                    break;
                case "--interface":
                    interfaceName = NormalizeInterface(ReadValue(args, ref index, "--interface"));
                    break;
                case "--json":
                    json = true;
                    index++;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    index++;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[index]}'.");
            }
        }

        if (command == "monitor" && json)
        {
            throw new ArgumentException("--json is only valid with the list command.");
        }

        return new Options(command, duration, interfaceName, json, showHelp);
    }

    internal static void WriteHelp()
    {
        Console.WriteLine("Read-only FIFINE SC3 HID descriptor and input-report monitor");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  sc3-hid-monitor list [--json] [--interface MI_03|MI_04]");
        Console.WriteLine("  sc3-hid-monitor monitor [--duration SECONDS] [--interface MI_03|MI_04]");
        Console.WriteLine();
        Console.WriteLine("The executable is hard-coded to VID 0x3142 / PID 0x0C33.");
        Console.WriteLine("It opens matching interfaces for input only and never sends output or feature reports.");
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        var value = args[index + 1];
        index += 2;
        return value;
    }

    private static int ParseDuration(string value)
    {
        if (!int.TryParse(value, out var duration) || duration is < 1 or > 86400)
        {
            throw new ArgumentException("--duration must be an integer from 1 through 86400 seconds.");
        }

        return duration;
    }

    private static string NormalizeInterface(string value)
    {
        var normalized = value.ToUpperInvariant();
        if (normalized is not ("MI_03" or "MI_04"))
        {
            throw new ArgumentException("--interface must be MI_03 or MI_04.");
        }

        return normalized;
    }
}
