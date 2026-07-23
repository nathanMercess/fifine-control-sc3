using FifineControl.Core.Audio;
using FifineControl.Core.Configuration;
using FifineControl.Core.Dsp;
using FifineControl.Core.Logging;
using FifineControl.Core.Recording;
using FifineControl.Core.Routing;
using HidSharp;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var appDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FifineControl");
var logger = new JsonFileLogger(Path.Combine(appDirectory, "logs", $"fifine-{DateTime.Now:yyyyMMdd}.jsonl"));
var settingsStore = new JsonSettingsStore(Path.Combine(appDirectory, "settings.json"), logger);
IAudioEndpointService audio = new WindowsAudioEndpointService(logger);

try
{
    var arguments = args.ToList();
    var command = arguments.Count == 0 ? "help" : arguments[0].ToLowerInvariant();
    switch (command)
    {
        case "devices":
            PrintDevices(audio.GetActiveDevices());
            break;
        case "status":
            Require(arguments, 2, "status <device-id>");
            PrintDevice(audio.GetDevice(arguments[1]));
            break;
        case "mute":
        case "unmute":
            Require(arguments, 2, $"{command} <device-id>");
            Console.WriteLine(audio.SetMute(arguments[1], command == "mute") ? "Muted" : "Unmuted");
            break;
        case "toggle":
            Require(arguments, 2, "toggle <device-id>");
            Console.WriteLine(audio.ToggleMute(arguments[1]) ? "Muted" : "Unmuted");
            break;
        case "volume":
            Require(arguments, 3, "volume <device-id> <0-100>");
            if (!float.TryParse(arguments[2], out var percent) || percent is < 0 or > 100)
            {
                throw new ArgumentException("Volume must be a number between 0 and 100.");
            }
            Console.WriteLine($"Actual volume: {audio.SetVolume(arguments[1], percent / 100):P0}");
            break;
        case "monitor":
            Require(arguments, 2, "monitor <device-id> [seconds]");
            var monitorSeconds = ParsePositiveSeconds(arguments, 2, 30);
            await MonitorAsync(audio, arguments[1], monitorSeconds);
            break;
        case "record":
            Require(arguments, 3, "record <capture-device-id> <seconds> [directory] [label]");
            var recordingSeconds = ParsePositiveSeconds(arguments, 2, 0);
            var recordingDirectory = arguments.Count > 3
                ? arguments[3]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "FifineControl");
            var label = arguments.Count > 4 ? arguments[4] : null;
            await RecordAsync(arguments[1], recordingSeconds, recordingDirectory, label, logger);
            break;
        case "recover":
            Require(arguments, 2, "recover <directory>");
            foreach (var recovered in WavRecordingService.RecoverInterruptedRecordings(arguments[1], logger))
            {
                Console.WriteLine(recovered);
            }
            break;
        case "route":
            Require(arguments, 3, "route <capture-device-id> <render-device-id> [seconds] [gain-db]");
            var routeSeconds = ParsePositiveSeconds(arguments, 3, 30);
            var routeGainDb = arguments.Count > 4 && float.TryParse(arguments[4], out var parsedGain)
                ? parsedGain
                : 0;
            await RouteAsync(arguments[1], arguments[2], routeSeconds, routeGainDb, audio, logger);
            break;
        case "profiles":
            var settings = settingsStore.LoadOrCreate();
            foreach (var profile in settings.Profiles)
            {
                Console.WriteLine($"{(profile.Name == settings.CurrentProfile ? '*' : ' ')} {profile.Name} | capture={profile.CaptureDeviceId ?? "(not set)"} | record={profile.RecordingDirectory}");
            }
            break;
        case "apply-profile":
            Require(arguments, 2, "apply-profile <name>");
            var updated = new ProfileService(audio, settingsStore, logger).Apply(arguments[1]);
            Console.WriteLine($"Applied profile: {updated.CurrentProfile}");
            break;
        case "help":
        case "--help":
        case "sc3-handshake":
            var sendKnownHandshake = arguments
                .Skip(1)
                .Any(argument => string.Equals(
                    argument,
                    "--send-known",
                    StringComparison.OrdinalIgnoreCase));

            Sc3HandshakeProbe(sendKnownHandshake);
            break;
        case "-h":
            PrintHelp(appDirectory);
            break;
        default:
            throw new ArgumentException($"Unknown command '{command}'. Use 'help'.");
    }

    return 0;
}
catch (Exception ex)
{
    logger.Error("cli.command.failed", ex, new { args });
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine($"Log: {Path.Combine(appDirectory, "logs")}");
    return 1;
}

static void PrintDevices(IEnumerable<AudioDeviceInfo> devices)
{
    foreach (var device in devices)
    {
        PrintDevice(device);
        Console.WriteLine();
    }
}

static void PrintDevice(AudioDeviceInfo device)
{
    Console.WriteLine($"[{device.Direction}] {device.Name}{(device.IsDefault ? " (default)" : string.Empty)}");
    Console.WriteLine($"  Id: {device.Id}");
    Console.WriteLine($"  Mute: {device.IsMuted,-5} Volume: {device.Volume:P0} Peak: {device.Peak:P0}");
}

static async Task MonitorAsync(IAudioEndpointService audio, string deviceId, int seconds)
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    Console.WriteLine($"Monitoring for {seconds}s. Ctrl+C to stop.");
    ConsoleCancelEventHandler handler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += handler;
    try
    {
        while (!cancellation.IsCancellationRequested)
        {
            var peak = audio.GetPeak(deviceId);
            var bars = new string('█', (int)Math.Round(peak * 40)).PadRight(40);
            Console.Write($"\r[{bars}] {peak,6:P1}");
            await Task.Delay(50, cancellation.Token);
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }
    finally
    {
        Console.CancelKeyPress -= handler;
        Console.WriteLine();
    }
}

static async Task RecordAsync(string deviceId, int seconds, string directory, string? label, IAppLogger logger)
{
    await using var recorder = new WavRecordingService(logger);
    var session = await recorder.StartAsync(deviceId, directory, label);
    Console.WriteLine($"Recording to {session.FinalPath}");
    Console.WriteLine("Press Ctrl+C to stop early.");

    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    ConsoleCancelEventHandler handler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += handler;
    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        Console.CancelKeyPress -= handler;
    }

    await recorder.StopAsync();
    Console.WriteLine($"Saved: {session.FinalPath}");
}

static async Task RouteAsync(
    string captureId,
    string renderId,
    int seconds,
    float gainDb,
    IAudioEndpointService audio,
    IAppLogger logger)
{
    var captureDevice = audio.GetDevice(captureId);
    var renderDevice = audio.GetDevice(renderId);
    var feedbackWarning = RoutingSafety.GetFeedbackWarning(captureDevice.Name, renderDevice.Name);
    if (feedbackWarning is not null)
    {
        Console.WriteLine($"WARNING: {feedbackWarning}");
        Console.Write("Type YES to start this route: ");
        if (!string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal))
        {
            Console.WriteLine("Route cancelled.");
            return;
        }
    }

    await using var routing = new WasapiAudioRoutingService(logger);
    var settings = new DspSettings { DigitalGainDb = gainDb };
    var session = await routing.StartAsync(captureId, renderId, settings);
    Console.WriteLine($"Routing {session.CaptureDeviceName} -> {session.RenderDeviceName}");
    if (session.Warning is not null)
    {
        Console.WriteLine($"WARNING: {session.Warning}");
    }

    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    ConsoleCancelEventHandler handler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += handler;
    try
    {
        while (!cancellation.IsCancellationRequested)
        {
            Console.Write($"\rPre {routing.PreDspPeak,6:P1} | Post {routing.PostDspPeak,6:P1}");
            await Task.Delay(60, cancellation.Token);
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }
    finally
    {
        Console.CancelKeyPress -= handler;
        Console.WriteLine();
    }

    await routing.StopAsync();
}

static int ParsePositiveSeconds(IReadOnlyList<string> arguments, int index, int defaultValue)
{
    if (arguments.Count <= index)
    {
        return defaultValue;
    }

    if (!int.TryParse(arguments[index], out var seconds) || seconds <= 0)
    {
        throw new ArgumentException("Duration must be a positive whole number of seconds.");
    }

    return seconds;
}

static void Require(IReadOnlyCollection<string> arguments, int count, string usage)
{
    if (arguments.Count < count)
    {
        throw new ArgumentException($"Usage: {usage}");
    }
}
static void Sc3HandshakeProbe(bool sendKnownHandshake)
{
    const int vendorId = 0x3142;
    const int productId = 0x0C33;

    Console.WriteLine("Procurando o FIFINE SC3...");
    Console.WriteLine($"VID:PID = {vendorId:X4}:{productId:X4}");
    Console.WriteLine();

    var devices = DeviceList.Local
        .GetHidDevices(vendorId, productId)
        .ToList();

    if (devices.Count == 0)
    {
        throw new InvalidOperationException(
            "Nenhuma interface HID do FIFINE SC3 foi encontrada.");
    }

    Console.WriteLine($"Interfaces HID encontradas: {devices.Count}");
    Console.WriteLine();

    foreach (var device in devices)
    {
        Console.WriteLine(device.DevicePath);
        Console.WriteLine(
            $"  Input:   {device.GetMaxInputReportLength()} bytes");
        Console.WriteLine(
            $"  Output:  {device.GetMaxOutputReportLength()} bytes");
        Console.WriteLine(
            $"  Feature: {device.GetMaxFeatureReportLength()} bytes");
        Console.WriteLine();
    }

    var mi04 = devices.FirstOrDefault(device =>
        device.DevicePath.Contains(
            "&mi_04#",
            StringComparison.OrdinalIgnoreCase));

    if (mi04 is null)
    {
        throw new InvalidOperationException(
            "O SC3 foi encontrado, mas a interface MI_04 não apareceu.");
    }

    Console.WriteLine("Interface MI_04 encontrada:");
    Console.WriteLine(mi04.DevicePath);
    Console.WriteLine();

    if (!sendKnownHandshake)
    {
        if (!mi04.TryOpen(out var stream))
        {
            throw new IOException(
                "Não foi possível abrir a MI_04. " +
                "Feche o atualizador e o HidMonitor.");
        }

        using (stream)
        {
            Console.WriteLine("MI_04 aberta com sucesso.");
            Console.WriteLine("Nenhum report foi enviado ao mixer.");
        }

        Console.WriteLine("MI_04 fechada com segurança.");
        Console.WriteLine();
        Console.WriteLine(
            "Para enviar o handshake conhecido, execute:");
        Console.WriteLine(
            "dotnet run --project .\\src\\FifineControl.Cli -- " +
            "sc3-handshake --send-known");

        return;
    }

    Console.WriteLine("ATENÇÃO:");
    Console.WriteLine(
        "Este comando enviará o único report de detecção capturado");
    Console.WriteLine(
        "da ferramenta oficial. Nenhum comando de Flash será enviado.");
    Console.WriteLine();
    Console.Write("Digite SEND para continuar: ");

    if (!string.Equals(
            Console.ReadLine(),
            "SEND",
            StringComparison.Ordinal))
    {
        Console.WriteLine("Operação cancelada.");
        return;
    }

    using var handle = Sc3NativeMethods.CreateFile(
        mi04.DevicePath,
        Sc3NativeMethods.GenericRead | Sc3NativeMethods.GenericWrite,
        Sc3NativeMethods.FileShareRead | Sc3NativeMethods.FileShareWrite,
        IntPtr.Zero,
        Sc3NativeMethods.OpenExisting,
        0,
        IntPtr.Zero);

    if (handle.IsInvalid)
    {
        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            "Não foi possível abrir a interface MI_04 pelo Windows.");
    }

    /*
     * A interface possui reports de 257 bytes:
     *
     * byte 0: Report ID, zero porque o SC3 não usa IDs
     * bytes 1–256: payload observado no USBPcap
     */
    var outputReport = new byte[mi04.GetMaxOutputReportLength()];

    outputReport[0] = 0x00; // Report ID
    outputReport[1] = 0xA5;
    outputReport[2] = 0x5A;
    outputReport[3] = 0x11;
    outputReport[4] = 0x00;
    outputReport[5] = 0x16;
    outputReport[^1] = 0xAA;

    Console.WriteLine();
    Console.WriteLine(
        $"Enviando report conhecido ({outputReport.Length} bytes)...");

    if (!Sc3NativeMethods.HidD_SetOutputReport(
            handle,
            outputReport,
            outputReport.Length))
    {
        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            "O Windows não conseguiu enviar o Output Report.");
    }

    Console.WriteLine("Output Report enviado.");

    // A ferramenta oficial aguardou aproximadamente 100 ms
    // antes de solicitar a resposta.
    Thread.Sleep(150);

    var inputReport = new byte[mi04.GetMaxInputReportLength()];
    inputReport[0] = 0x00; // Report ID

    if (!Sc3NativeMethods.HidD_GetInputReport(
            handle,
            inputReport,
            inputReport.Length))
    {
        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            "O Windows não conseguiu obter o Input Report.");
    }

    Console.WriteLine(
        $"Input Report recebido ({inputReport.Length} bytes).");
    Console.WriteLine($"Report ID: 0x{inputReport[0]:X2}");

    var firstPayloadBytes = inputReport
        .Skip(1)
        .Take(32)
        .Select(value => value.ToString("X2"));

    Console.WriteLine(
        $"Primeiros 32 bytes: {string.Join(" ", firstPayloadBytes)}");

    var nonZeroBytes = inputReport
        .Skip(1)
        .Select((value, index) => new { Value = value, Index = index })
        .Where(item => item.Value != 0)
        .ToList();

    Console.WriteLine("Bytes não zerados do payload:");

    if (nonZeroBytes.Count == 0)
    {
        Console.WriteLine("  Nenhum.");
    }
    else
    {
        foreach (var item in nonZeroBytes)
        {
            Console.WriteLine(
                $"  Payload[{item.Index}] = 0x{item.Value:X2}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Handshake concluído. Nenhum comando de Flash foi enviado.");
}

static void PrintHelp(string appDirectory)
{
    Console.WriteLine("FifineControl Windows audio proof of concept");
    Console.WriteLine();
    Console.WriteLine("  devices");
    Console.WriteLine("  status <device-id>");
    Console.WriteLine("  mute|unmute|toggle <device-id>");
    Console.WriteLine("  volume <device-id> <0-100>");
    Console.WriteLine("  monitor <device-id> [seconds]");
    Console.WriteLine("  record <capture-device-id> <seconds> [directory] [label]");
    Console.WriteLine("  recover <directory>");
    Console.WriteLine("  route <capture-device-id> <render-device-id> [seconds] [gain-db]");
    Console.WriteLine("  profiles");
    Console.WriteLine("  apply-profile <name>");
    Console.WriteLine("  sc3-handshake [--send-known]");
    Console.WriteLine();
    Console.WriteLine($"Settings and logs: {appDirectory}");
}

static class Sc3NativeMethods
{
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;

    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;

    internal const uint OpenExisting = 3;

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_SetOutputReport(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetInputReport(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);
}