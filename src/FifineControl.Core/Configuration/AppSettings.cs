namespace FifineControl.Core.Configuration;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string CurrentProfile { get; init; } = "Default";
    public IReadOnlyList<AudioProfile> Profiles { get; init; } =
    [
        new AudioProfile { Name = "Default" }
    ];
    public ObsWebSocketSettings Obs { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
}
