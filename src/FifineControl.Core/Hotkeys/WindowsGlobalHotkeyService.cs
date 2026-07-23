using System.ComponentModel;
using System.Runtime.InteropServices;
using FifineControl.Core.Logging;

namespace FifineControl.Core.Hotkeys;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    public const int HotkeyMessage = 0x0312;

    private readonly Dictionary<int, GlobalHotkeyBinding> bindings = [];
    private readonly IAppLogger logger;
    private IntPtr windowHandle;
    private bool disposed;

    public WindowsGlobalHotkeyService(IAppLogger? logger = null)
    {
        this.logger = logger ?? NullAppLogger.Instance;
    }

    public event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

    public IReadOnlyCollection<GlobalHotkeyBinding> RegisteredHotkeys =>
        bindings.Values.ToArray();

    public void AttachWindow(IntPtr windowHandle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("O identificador da janela nao pode ser zero.", nameof(windowHandle));
        }

        if (bindings.Count > 0 && this.windowHandle != windowHandle)
        {
            throw new InvalidOperationException("Remova os atalhos antes de trocar a janela associada.");
        }

        this.windowHandle = windowHandle;
    }

    public GlobalHotkeyRegistrationResult Register(GlobalHotkeyBinding binding)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateBinding(binding);
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Associe a janela antes de registrar atalhos.");
        }

        if (bindings.TryGetValue(binding.Id, out var current))
        {
            if (current == binding)
            {
                return GlobalHotkeyRegistrationResult.Success;
            }

            Unregister(binding.Id);
        }

        var modifiers = binding.Modifiers | GlobalHotkeyModifiers.NoRepeat;
        if (!RegisterHotKey(windowHandle, binding.Id, (uint)modifiers, binding.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            logger.Error(
                "hotkey.register.failed",
                new Win32Exception(error),
                new { binding.Id, binding.Action, Modifiers = modifiers, binding.VirtualKey });
            return new GlobalHotkeyRegistrationResult(false, error);
        }

        bindings.Add(binding.Id, binding with { Modifiers = modifiers });
        logger.Info(
            "hotkey.registered",
            new { binding.Id, binding.Action, Modifiers = modifiers, binding.VirtualKey });
        return GlobalHotkeyRegistrationResult.Success;
    }

    public bool Unregister(int id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!bindings.Remove(id))
        {
            return false;
        }

        var succeeded = UnregisterHotKey(windowHandle, id);
        if (!succeeded)
        {
            var error = Marshal.GetLastWin32Error();
            logger.Error("hotkey.unregister.failed", new Win32Exception(error), new { Id = id });
        }

        return succeeded;
    }

    public void UnregisterAll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var id in bindings.Keys.ToArray())
        {
            Unregister(id);
        }
    }

    public bool ProcessWindowMessage(int message, IntPtr wParam)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (message != HotkeyMessage || !bindings.TryGetValue(wParam.ToInt32(), out var binding))
        {
            return false;
        }

        HotkeyPressed?.Invoke(this, new GlobalHotkeyPressedEventArgs(binding));
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (var id in bindings.Keys.ToArray())
        {
            if (!UnregisterHotKey(windowHandle, id))
            {
                var error = Marshal.GetLastWin32Error();
                logger.Error("hotkey.unregister.failed", new Win32Exception(error), new { Id = id });
            }

            bindings.Remove(id);
        }

        disposed = true;
        windowHandle = IntPtr.Zero;
    }

    private static void ValidateBinding(GlobalHotkeyBinding binding)
    {
        if (binding.Id is < 0 or > 0xBFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(binding), "O ID deve estar entre 0 e 0xBFFF.");
        }

        if (binding.VirtualKey is 0 or > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(binding), "A tecla virtual deve estar entre 1 e 0xFF.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
