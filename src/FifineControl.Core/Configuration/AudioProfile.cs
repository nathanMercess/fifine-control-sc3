namespace FifineControl.Core.Configuration;

public sealed record AudioProfile
{
    public required string Name { get; init; }
    public string? CaptureDeviceId { get; init; }
    public string? MonitorDeviceId { get; init; }
    public string RecordingDirectory { get; init; } = "recordings";
    public float CaptureVolume { get; init; } = 1f;
    public float DigitalGainDb { get; init; }
    public IReadOnlyList<string> EnabledFilters { get; init; } = Array.Empty<string>();
}
