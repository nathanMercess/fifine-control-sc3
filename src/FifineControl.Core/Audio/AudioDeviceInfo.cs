namespace FifineControl.Core.Audio;

public enum AudioDeviceDirection
{
    Capture,
    Render
}

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    AudioDeviceDirection Direction,
    bool IsDefault,
    bool IsMuted,
    float Volume,
    float Peak);
