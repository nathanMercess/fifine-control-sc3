namespace FifineControl.Core.Hotkeys;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

    IReadOnlyCollection<GlobalHotkeyBinding> RegisteredHotkeys { get; }

    void AttachWindow(IntPtr windowHandle);
    GlobalHotkeyRegistrationResult Register(GlobalHotkeyBinding binding);
    bool Unregister(int id);
    void UnregisterAll();
    bool ProcessWindowMessage(int message, IntPtr wParam);
}

public enum GlobalHotkeyAction
{
    ToggleMute,
    ToggleRecording
}

[Flags]
public enum GlobalHotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}

public sealed record GlobalHotkeyBinding(
    int Id,
    GlobalHotkeyAction Action,
    GlobalHotkeyModifiers Modifiers,
    uint VirtualKey);

public sealed class GlobalHotkeyPressedEventArgs : EventArgs
{
    public GlobalHotkeyPressedEventArgs(GlobalHotkeyBinding binding)
    {
        Binding = binding;
    }

    public GlobalHotkeyBinding Binding { get; }
}

public sealed record GlobalHotkeyRegistrationResult(bool Succeeded, int Win32Error = 0)
{
    public static GlobalHotkeyRegistrationResult Success { get; } = new(true);
}
