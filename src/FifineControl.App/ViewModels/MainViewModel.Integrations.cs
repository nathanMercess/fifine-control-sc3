using System.Windows;
using FifineControl.Core.Configuration;
using FifineControl.Core.Hotkeys;
using FifineControl.Core.Integrations.Obs;

namespace FifineControl.App.ViewModels;

public sealed partial class MainViewModel
{
    private const int MuteHotkeyId = 0x5100;
    private const int RecordingHotkeyId = 0x5101;

    private IObsWebSocketService obs = null!;
    private IGlobalHotkeyService hotkeys = null!;
    private string obsServerUri = "ws://127.0.0.1:4455";
    private string obsPassword = string.Empty;
    private string obsStatusText = "Desconectado";
    private bool obsConnected;
    private bool obsRecording;
    private bool obsConnectOnStartup;
    private string hotkeyStatusText = "Atalhos ainda nao registrados.";

    public AsyncRelayCommand ToggleObsConnectionCommand { get; private set; } = null!;
    public AsyncRelayCommand StartObsRecordingCommand { get; private set; } = null!;
    public AsyncRelayCommand StopObsRecordingCommand { get; private set; } = null!;

    public string ObsServerUri
    {
        get => obsServerUri;
        set => SetProperty(ref obsServerUri, value);
    }

    public string ObsPassword
    {
        get => obsPassword;
        set => SetProperty(ref obsPassword, value);
    }

    public bool ObsConnectOnStartup
    {
        get => obsConnectOnStartup;
        set => SetProperty(ref obsConnectOnStartup, value);
    }

    public string ObsStatusText
    {
        get => obsStatusText;
        private set => SetProperty(ref obsStatusText, value);
    }

    public bool ObsConnected
    {
        get => obsConnected;
        private set
        {
            if (SetProperty(ref obsConnected, value))
            {
                OnPropertyChanged(nameof(ObsConnectionButtonText));
                StartObsRecordingCommand.RaiseCanExecuteChanged();
                StopObsRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ObsRecording
    {
        get => obsRecording;
        private set
        {
            if (SetProperty(ref obsRecording, value))
            {
                OnPropertyChanged(nameof(ObsRecordingStateText));
                StartObsRecordingCommand.RaiseCanExecuteChanged();
                StopObsRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ObsConnectionButtonText => ObsConnected ? "Desconectar" : "Conectar";
    public string ObsRecordingStateText => ObsRecording ? "Gravando no OBS" : "OBS sem gravacao";

    public string HotkeyStatusText
    {
        get => hotkeyStatusText;
        private set => SetProperty(ref hotkeyStatusText, value);
    }

    public bool ProcessWindowMessage(int message, IntPtr wParam) =>
        hotkeys.ProcessWindowMessage(message, wParam);

    public void AttachGlobalHotkeys(IntPtr windowHandle)
    {
        hotkeys.AttachWindow(windowHandle);
        var mute = hotkeys.Register(new GlobalHotkeyBinding(
            MuteHotkeyId,
            GlobalHotkeyAction.ToggleMute,
            settings.Hotkeys.MuteModifiers,
            settings.Hotkeys.MuteVirtualKey));
        var recording = hotkeys.Register(new GlobalHotkeyBinding(
            RecordingHotkeyId,
            GlobalHotkeyAction.ToggleRecording,
            settings.Hotkeys.RecordingModifiers,
            settings.Hotkeys.RecordingVirtualKey));

        HotkeyStatusText = mute.Succeeded && recording.Succeeded
            ? $"Ativos: {FormatHotkey(settings.Hotkeys.MuteModifiers, settings.Hotkeys.MuteVirtualKey)} (mute) e " +
              $"{FormatHotkey(settings.Hotkeys.RecordingModifiers, settings.Hotkeys.RecordingVirtualKey)} (WAV)."
            : $"Falha ao registrar atalhos (Win32: mute={mute.Win32Error}, gravacao={recording.Win32Error}).";

        if (settings.Obs.ConnectOnStartup && !ObsConnected)
        {
            ToggleObsConnectionCommand.Execute(null);
        }
    }

    private void InitializeIntegrations(IObsWebSocketService obsService, IGlobalHotkeyService hotkeyService)
    {
        obs = obsService;
        hotkeys = hotkeyService;
        obs.ConnectionStateChanged += OnObsConnectionStateChanged;
        hotkeys.HotkeyPressed += OnGlobalHotkeyPressed;
        ToggleObsConnectionCommand = new AsyncRelayCommand(ToggleObsConnectionAsync);
        StartObsRecordingCommand = new AsyncRelayCommand(StartObsRecordingAsync, () => ObsConnected && !ObsRecording);
        StopObsRecordingCommand = new AsyncRelayCommand(StopObsRecordingAsync, () => ObsConnected && ObsRecording);
    }

    private void LoadIntegrationSettings()
    {
        ObsServerUri = settings.Obs.ServerUri;
        // OBS credentials deliberately remain in memory for this process only.
        ObsPassword = string.Empty;
        ObsConnectOnStartup = settings.Obs.ConnectOnStartup;
    }

    private async Task ToggleObsConnectionAsync()
    {
        try
        {
            if (ObsConnected)
            {
                await obs.DisconnectAsync();
                ObsRecording = false;
                LastMessage = "OBS desconectado.";
                return;
            }

            if (!Uri.TryCreate(ObsServerUri.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss))
            {
                LastMessage = "Informe um endereco OBS WebSocket ws:// ou wss:// valido.";
                return;
            }

            SaveObsSettings();
            ObsStatusText = "Conectando...";
            await obs.ConnectAsync(uri, string.IsNullOrEmpty(ObsPassword) ? null : ObsPassword);
            await RefreshObsRecordingStatusAsync();
            LastMessage = "Conectado ao OBS WebSocket.";
        }
        catch (Exception ex)
        {
            HandleError("obs.ui.connection.failed", "Falha na conexao com o OBS.", ex);
        }
    }

    private async Task StartObsRecordingAsync()
    {
        try
        {
            await obs.StartRecordingAsync();
            await RefreshObsRecordingStatusAsync();
            LastMessage = "Gravacao iniciada no OBS.";
        }
        catch (Exception ex)
        {
            HandleError("obs.ui.recording.start.failed", "Nao foi possivel iniciar a gravacao no OBS.", ex);
        }
    }

    private async Task StopObsRecordingAsync()
    {
        try
        {
            await obs.StopRecordingAsync();
            await RefreshObsRecordingStatusAsync();
            LastMessage = "Gravacao encerrada no OBS.";
        }
        catch (Exception ex)
        {
            HandleError("obs.ui.recording.stop.failed", "Nao foi possivel parar a gravacao no OBS.", ex);
        }
    }

    private async Task RefreshObsRecordingStatusAsync()
    {
        var status = await obs.GetRecordingStatusAsync();
        ObsRecording = status.IsActive;
        ObsStatusText = obs.ServerVersion is null
            ? "Conectado"
            : $"Conectado - OBS {obs.ServerVersion}";
    }

    private void SaveObsSettings()
    {
        settings = settings with
        {
            Obs = new ObsWebSocketSettings
            {
                ServerUri = ObsServerUri.Trim(),
                Password = string.Empty,
                ConnectOnStartup = ObsConnectOnStartup
            }
        };
        settingsStore.Save(settings);
    }

    private void OnObsConnectionStateChanged(object? sender, ObsConnectionStateChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            ObsConnected = e.State == ObsConnectionState.Connected;
            if (!ObsConnected)
            {
                ObsRecording = false;
            }

            ObsStatusText = e.State switch
            {
                ObsConnectionState.Connecting => "Conectando...",
                ObsConnectionState.Connected when !string.IsNullOrWhiteSpace(e.ServerVersion) =>
                    $"Conectado - OBS {e.ServerVersion}",
                ObsConnectionState.Connected => "Conectado",
                ObsConnectionState.Faulted => $"Falha: {e.ErrorMessage}",
                _ => "Desconectado"
            };
        });
    }

    private void OnGlobalHotkeyPressed(object? sender, GlobalHotkeyPressedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (e.Binding.Action == GlobalHotkeyAction.ToggleMute)
            {
                ToggleMuteCommand.Execute(null);
            }
            else
            {
                ToggleRecordingCommand.Execute(null);
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private static string FormatHotkey(GlobalHotkeyModifiers modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(GlobalHotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(GlobalHotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(GlobalHotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(GlobalHotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(virtualKey is >= 0x20 and <= 0x7E ? ((char)virtualKey).ToString() : $"VK_{virtualKey:X2}");
        return string.Join('+', parts);
    }

    private void DisposeIntegrations()
    {
        obs.ConnectionStateChanged -= OnObsConnectionStateChanged;
        hotkeys.HotkeyPressed -= OnGlobalHotkeyPressed;
        hotkeys.Dispose();
        obs.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
