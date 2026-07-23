using FifineControl.Core.Hotkeys;

namespace FifineControl.Core.Configuration;

public sealed record HotkeySettings
{
    public GlobalHotkeyModifiers MuteModifiers { get; init; } =
        GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Shift;
    public uint MuteVirtualKey { get; init; } = 0x4D; // M
    public GlobalHotkeyModifiers RecordingModifiers { get; init; } =
        GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Shift;
    public uint RecordingVirtualKey { get; init; } = 0x52; // R
}
