using System.ComponentModel;

namespace SC3.HidMonitor;

internal static class InputReportMonitor
{
    internal static async Task MonitorAsync(
        IReadOnlyList<HidDeviceDescriptor> devices,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (devices.Count == 0)
        {
            Console.Error.WriteLine("No matching SC3 HID interfaces were found.");
            return;
        }

        using var durationCancellation = new CancellationTokenSource(duration);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            durationCancellation.Token);

        var tasks = devices.Select(device => ReadDeviceAsync(device, linkedCancellation.Token)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task ReadDeviceAsync(HidDeviceDescriptor device, CancellationToken cancellationToken)
    {
        if (device.Error is not null)
        {
            Console.Error.WriteLine($"[{device.Interface}] skipped: {device.Error}");
            return;
        }

        if (device.InputReportByteLength == 0)
        {
            Console.Error.WriteLine($"[{device.Interface}] skipped: descriptor reports no input reports.");
            return;
        }

        try
        {
            using var handle = HidNative.OpenForInput(device.Path);
            await using var stream = new FileStream(
                handle,
                FileAccess.Read,
                device.InputReportByteLength,
                isAsync: true);
            var buffer = new byte[device.InputReportByteLength];

            Console.Error.WriteLine(
                $"[{device.Interface}] listening, input report length {device.InputReportByteLength} bytes.");

            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (bytesRead == 0)
                {
                    break;
                }

                var hex = Convert.ToHexString(buffer.AsSpan(0, bytesRead));
                Console.WriteLine(
                    $"{DateTimeOffset.Now:O}\t{device.Interface}\t{bytesRead}\t{hex}");
                Console.Out.Flush();
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[{device.Interface}] input unavailable: {exception.Message}");
        }
    }
}
