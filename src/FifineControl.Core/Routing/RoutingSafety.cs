namespace FifineControl.Core.Routing;

public static class RoutingSafety
{
    public static void EnsurePossibleRoute(
        string captureDeviceId,
        string captureDeviceName,
        string renderDeviceId,
        string renderDeviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(renderDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureDeviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(renderDeviceName);

        if (string.Equals(captureDeviceId, renderDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Capture and render endpoint IDs are identical; routing would feed an endpoint into itself.");
        }

        if (string.Equals(Normalize(captureDeviceName), Normalize(renderDeviceName), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Capture and render endpoint names are identical; select a distinct output endpoint.");
        }
    }

    public static string? GetFeedbackWarning(string captureDeviceName, string renderDeviceName)
    {
        var captureHardware = ParenthesizedSuffix(captureDeviceName);
        var renderHardware = ParenthesizedSuffix(renderDeviceName);
        if (captureHardware is not null &&
            string.Equals(captureHardware, renderHardware, StringComparison.OrdinalIgnoreCase))
        {
            return "Capture and output appear to belong to the same physical device. Use headphones and keep speaker monitoring low to prevent acoustic feedback.";
        }

        return null;
    }

    private static string Normalize(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? ParenthesizedSuffix(string value)
    {
        var open = value.LastIndexOf('(');
        return open >= 0 && value.EndsWith(')')
            ? value[(open + 1)..^1].Trim()
            : null;
    }
}
