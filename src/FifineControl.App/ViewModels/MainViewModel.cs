using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FifineControl.Core.Audio;
using FifineControl.Core.Configuration;
using FifineControl.Core.Hotkeys;
using FifineControl.Core.Integrations.Obs;
using FifineControl.Core.Logging;
using FifineControl.Core.Recording;
using FifineControl.Core.Startup;
using WinForms = System.Windows.Forms;

namespace FifineControl.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAudioEndpointService audio;
    private readonly WavRecordingService recorder;
    private readonly RecordingFileManager recordingFiles;
    private readonly JsonSettingsStore settingsStore;
    private readonly IAppLogger logger;
    private readonly string logDirectory;
    private readonly DispatcherTimer meterTimer;
    private AppSettings settings = new();
    private AudioDeviceInfo? selectedCaptureDevice;
    private AudioDeviceInfo? selectedMonitorDevice;
    private RecentRecordingItem? selectedRecording;
    private string? selectedProfileName;
    private string profileName = "Default";
    private string recordingDirectory;
    private double endpointVolumePercent;
    private double digitalGainDb;
    private double peakPercent;
    private bool isMuted;
    private bool isRecording;
    private bool minimizeToTray = true;
    private DateTimeOffset recordingStartedAt;
    private string recordingElapsed = "00:00:00";
    private string recordingNewName = string.Empty;
    private string statusText = "Inicializando…";
    private string lastMessage = "Pronto.";
    private string diagnosticSummary = string.Empty;
    private bool suppressEndpointUpdate;
    private bool disposed;

    public MainViewModel(
        IAudioEndpointService audio,
        WavRecordingService recorder,
        JsonSettingsStore settingsStore,
        IAppLogger logger,
        string logDirectory,
        IObsWebSocketService obs,
        IGlobalHotkeyService hotkeys,
        RecordingFileManager recordingFiles,
        IStartupRegistrationService startupRegistration,
        string startupExecutablePath)
    {
        this.audio = audio;
        this.recorder = recorder;
        this.recordingFiles = recordingFiles;
        this.settingsStore = settingsStore;
        this.logger = logger;
        this.logDirectory = logDirectory;
        recordingDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "FifineControl");

        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        ToggleMuteCommand = new RelayCommand(ToggleMute, () => SelectedCaptureDevice is not null);
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, () => SelectedCaptureDevice is not null);
        BrowseRecordingDirectoryCommand = new RelayCommand(BrowseRecordingDirectory);
        OpenRecordingFolderCommand = new RelayCommand(() => OpenDirectory(RecordingDirectory));
        RefreshRecentCommand = new RelayCommand(RefreshRecentRecordings);
        OpenSelectedRecordingCommand = new RelayCommand(
            () => OpenFile(SelectedRecording?.Path),
            () => SelectedRecording is not null);
        RenameSelectedRecordingCommand = new RelayCommand(
            RenameSelectedRecording,
            () => SelectedRecording is not null && !string.IsNullOrWhiteSpace(RecordingNewName));
        DeleteSelectedRecordingCommand = new RelayCommand(
            DeleteSelectedRecording,
            () => SelectedRecording is not null);
        ApplyProfileCommand = new RelayCommand(ApplySelectedProfile, () => !string.IsNullOrWhiteSpace(SelectedProfileName));
        SaveProfileCommand = new RelayCommand(SaveCurrentProfile, () => !string.IsNullOrWhiteSpace(ProfileName));
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);
        OpenLogsCommand = new RelayCommand(() => OpenDirectory(logDirectory));
        InitializeIntegrations(obs, hotkeys);
        InitializeStartup(startupRegistration, startupExecutablePath);
        InitializeRouting();

        LoadSettings();
        RefreshDevices();
        RefreshRecentRecordings();

        meterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        meterTimer.Tick += OnMeterTick;
        meterTimer.Start();
    }

    public ObservableCollection<AudioDeviceInfo> CaptureDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> RenderDevices { get; } = [];
    public ObservableCollection<RecentRecordingItem> RecentRecordings { get; } = [];
    public ObservableCollection<string> Profiles { get; } = [];

    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public AsyncRelayCommand ToggleRecordingCommand { get; }
    public RelayCommand BrowseRecordingDirectoryCommand { get; }
    public RelayCommand OpenRecordingFolderCommand { get; }
    public RelayCommand RefreshRecentCommand { get; }
    public RelayCommand OpenSelectedRecordingCommand { get; }
    public RelayCommand RenameSelectedRecordingCommand { get; }
    public RelayCommand DeleteSelectedRecordingCommand { get; }
    public RelayCommand ApplyProfileCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand CopyDiagnosticsCommand { get; }
    public RelayCommand OpenLogsCommand { get; }

    public AudioDeviceInfo? SelectedCaptureDevice
    {
        get => selectedCaptureDevice;
        set
        {
            if (!SetProperty(ref selectedCaptureDevice, value))
            {
                return;
            }

            suppressEndpointUpdate = true;
            EndpointVolumePercent = value?.Volume * 100 ?? 0;
            IsMuted = value?.IsMuted ?? false;
            suppressEndpointUpdate = false;
            ToggleMuteCommand.RaiseCanExecuteChanged();
            ToggleRecordingCommand.RaiseCanExecuteChanged();
            RaiseRoutingCanExecuteChanged();
            UpdateDiagnostics();
        }
    }

    public AudioDeviceInfo? SelectedMonitorDevice
    {
        get => selectedMonitorDevice;
        set
        {
            if (SetProperty(ref selectedMonitorDevice, value))
            {
                RaiseRoutingCanExecuteChanged();
            }
        }
    }

    public RecentRecordingItem? SelectedRecording
    {
        get => selectedRecording;
        set
        {
            if (SetProperty(ref selectedRecording, value))
            {
                RecordingNewName = value is null ? string.Empty : Path.GetFileNameWithoutExtension(value.Name);
                OpenSelectedRecordingCommand.RaiseCanExecuteChanged();
                RenameSelectedRecordingCommand.RaiseCanExecuteChanged();
                DeleteSelectedRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RecordingNewName
    {
        get => recordingNewName;
        set
        {
            if (SetProperty(ref recordingNewName, value))
            {
                RenameSelectedRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedProfileName
    {
        get => selectedProfileName;
        set
        {
            if (SetProperty(ref selectedProfileName, value) && value is not null)
            {
                ProfileName = value;
                ApplyProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProfileName
    {
        get => profileName;
        set
        {
            if (SetProperty(ref profileName, value))
            {
                SaveProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RecordingDirectory
    {
        get => recordingDirectory;
        set
        {
            if (SetProperty(ref recordingDirectory, value))
            {
                RefreshRecentRecordings();
            }
        }
    }

    public double EndpointVolumePercent
    {
        get => endpointVolumePercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref endpointVolumePercent, clamped) || suppressEndpointUpdate || SelectedCaptureDevice is null)
            {
                return;
            }

            TryExecute(
                () =>
                {
                    var actual = audio.SetVolume(SelectedCaptureDevice.Id, (float)(clamped / 100));
                    LastMessage = $"Volume do endpoint: {actual:P0}.";
                },
                "Não foi possível alterar o volume do endpoint.");
        }
    }

    public double DigitalGainDb
    {
        get => digitalGainDb;
        set
        {
            if (SetProperty(ref digitalGainDb, Math.Clamp(value, -24, 24)))
            {
                UpdateActiveRoutingSettings();
            }
        }
    }

    public double PeakPercent
    {
        get => peakPercent;
        private set => SetProperty(ref peakPercent, Math.Clamp(value, 0, 100));
    }

    public bool IsMuted
    {
        get => isMuted;
        private set
        {
            if (SetProperty(ref isMuted, value))
            {
                OnPropertyChanged(nameof(MuteButtonText));
                OnPropertyChanged(nameof(MuteStateText));
            }
        }
    }

    public bool IsRecording
    {
        get => isRecording;
        private set
        {
            if (SetProperty(ref isRecording, value))
            {
                OnPropertyChanged(nameof(RecordingButtonText));
            }
        }
    }

    public bool MinimizeToTray
    {
        get => minimizeToTray;
        set => SetProperty(ref minimizeToTray, value);
    }

    public string RecordingElapsed
    {
        get => recordingElapsed;
        private set => SetProperty(ref recordingElapsed, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string LastMessage
    {
        get => lastMessage;
        private set => SetProperty(ref lastMessage, value);
    }

    public string DiagnosticSummary
    {
        get => diagnosticSummary;
        private set => SetProperty(ref diagnosticSummary, value);
    }

    public string MuteButtonText => IsMuted ? "Desmutar" : "Mutar";
    public string MuteStateText => IsMuted ? "Endpoint mutado" : "Endpoint ativo";
    public string RecordingButtonText => IsRecording ? "Parar gravação" : "Iniciar gravação";

    public async Task StopRecordingAsync()
    {
        if (!IsRecording)
        {
            return;
        }

        try
        {
            var completed = await recorder.StopAsync();
            LastMessage = completed is null
                ? "A gravação já estava encerrada."
                : $"Gravação salva: {completed.FinalPath}";
        }
        catch (Exception ex)
        {
            HandleError("recording.ui.stop.failed", "Falha ao finalizar a gravação; o arquivo parcial foi preservado.", ex);
        }
        finally
        {
            IsRecording = false;
            RecordingElapsed = "00:00:00";
            RefreshRecentRecordings();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        meterTimer.Stop();
        DisposeRouting();
        DisposeIntegrations();
        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void LoadSettings()
    {
        try
        {
            settings = settingsStore.LoadOrCreate();
        }
        catch (Exception ex)
        {
            logger.Error("settings.ui.load.failed", ex);
            settings = new AppSettings();
            LastMessage = "Configuração inválida; usando valores padrão. Consulte os logs.";
        }

        ReloadProfileNames();
        var profile = settings.Profiles.First(candidate =>
            string.Equals(candidate.Name, settings.CurrentProfile, StringComparison.OrdinalIgnoreCase));
        SelectedProfileName = profile.Name;
        ProfileName = profile.Name;
        RecordingDirectory = ResolveRecordingDirectory(profile.RecordingDirectory);
        DigitalGainDb = profile.DigitalGainDb;
        LoadIntegrationSettings();
    }

    private void RefreshDevices()
    {
        TryExecute(
            () =>
            {
                var previousCaptureId = SelectedCaptureDevice?.Id;
                var previousRenderId = SelectedMonitorDevice?.Id;
                var currentProfile = settings.Profiles.FirstOrDefault(profile =>
                    string.Equals(profile.Name, settings.CurrentProfile, StringComparison.OrdinalIgnoreCase));
                var devices = audio.GetActiveDevices();
                Replace(CaptureDevices, devices.Where(device => device.Direction == AudioDeviceDirection.Capture));
                Replace(RenderDevices, devices.Where(device => device.Direction == AudioDeviceDirection.Render));

                SelectedCaptureDevice = FindDevice(CaptureDevices, currentProfile?.CaptureDeviceId ?? previousCaptureId)
                    ?? CaptureDevices.FirstOrDefault(device => device.IsDefault)
                    ?? CaptureDevices.FirstOrDefault();
                SelectedMonitorDevice = FindDevice(RenderDevices, currentProfile?.MonitorDeviceId ?? previousRenderId)
                    ?? RenderDevices.FirstOrDefault(device => device.IsDefault)
                    ?? RenderDevices.FirstOrDefault();
                StatusText = $"{CaptureDevices.Count} entrada(s), {RenderDevices.Count} saída(s)";
                LastMessage = "Endpoints Core Audio atualizados.";
                UpdateDiagnostics();
            },
            "Falha ao enumerar os endpoints de áudio.");
    }

    private void ToggleMute()
    {
        if (SelectedCaptureDevice is null)
        {
            return;
        }

        TryExecute(
            () =>
            {
                IsMuted = audio.ToggleMute(SelectedCaptureDevice.Id);
                LastMessage = IsMuted ? "Endpoint de captura mutado." : "Endpoint de captura desmutado.";
            },
            "Não foi possível alterar o mute do endpoint.");
    }

    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync();
            return;
        }

        if (SelectedCaptureDevice is null)
        {
            LastMessage = "Selecione um endpoint de captura.";
            return;
        }

        try
        {
            var session = await recorder.StartAsync(SelectedCaptureDevice.Id, RecordingDirectory, ProfileName);
            recordingStartedAt = session.StartedAt;
            IsRecording = true;
            LastMessage = $"Gravando em {session.FinalPath}";
        }
        catch (Exception ex)
        {
            HandleError("recording.ui.start.failed", "Não foi possível iniciar a gravação.", ex);
        }
    }

    private void BrowseRecordingDirectory()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Escolha a pasta para as gravações WAV",
            SelectedPath = Directory.Exists(RecordingDirectory) ? RecordingDirectory : string.Empty,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            RecordingDirectory = dialog.SelectedPath;
            LastMessage = "Diretório de gravação alterado. Salve o perfil para persistir.";
        }
    }

    private void RefreshRecentRecordings()
    {
        try
        {
            if (!Directory.Exists(RecordingDirectory))
            {
                RecentRecordings.Clear();
                return;
            }

            var files = new DirectoryInfo(RecordingDirectory)
                .EnumerateFiles("*.wav", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(12)
                .Select(file => new RecentRecordingItem(file.FullName, file.Name, file.Length, file.LastWriteTime))
                .ToArray();
            Replace(RecentRecordings, files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HandleError("recordings.ui.list.failed", "Não foi possível listar as gravações recentes.", ex);
        }
    }

    private void RenameSelectedRecording()
    {
        if (SelectedRecording is null)
        {
            return;
        }

        TryExecute(
            () =>
            {
                var renamed = recordingFiles.Rename(RecordingDirectory, SelectedRecording.Path, RecordingNewName);
                RefreshRecentRecordings();
                SelectedRecording = RecentRecordings.FirstOrDefault(item =>
                    string.Equals(item.Path, renamed, StringComparison.OrdinalIgnoreCase));
                LastMessage = $"Gravacao renomeada para '{Path.GetFileName(renamed)}'.";
            },
            "Nao foi possivel renomear a gravacao selecionada.");
    }

    private void DeleteSelectedRecording()
    {
        if (SelectedRecording is null)
        {
            return;
        }

        string validatedPath;
        try
        {
            validatedPath = recordingFiles.ValidateManagedWav(RecordingDirectory, SelectedRecording.Path);
        }
        catch (Exception ex)
        {
            HandleError("recordings.ui.delete.validation.failed", "A gravacao selecionada nao pode ser removida.", ex);
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"Mover '{Path.GetFileName(validatedPath)}' para a Lixeira?",
            "FifineControl",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            LastMessage = "Remocao cancelada.";
            return;
        }

        TryExecute(
            () =>
            {
                recordingFiles.Delete(RecordingDirectory, validatedPath);
                SelectedRecording = null;
                RefreshRecentRecordings();
                LastMessage = "Gravacao movida para a Lixeira.";
            },
            "Nao foi possivel mover a gravacao para a Lixeira.");
    }

    private void ApplySelectedProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileName))
        {
            return;
        }

        TryExecute(
            () =>
            {
                settings = new ProfileService(audio, settingsStore, logger).Apply(SelectedProfileName);
                var profile = settings.Profiles.First(candidate =>
                    string.Equals(candidate.Name, settings.CurrentProfile, StringComparison.OrdinalIgnoreCase));
                RecordingDirectory = ResolveRecordingDirectory(profile.RecordingDirectory);
                DigitalGainDb = profile.DigitalGainDb;
                SelectedCaptureDevice = FindDevice(CaptureDevices, profile.CaptureDeviceId) ?? SelectedCaptureDevice;
                SelectedMonitorDevice = FindDevice(RenderDevices, profile.MonitorDeviceId) ?? SelectedMonitorDevice;
                if (SelectedCaptureDevice is not null)
                {
                    suppressEndpointUpdate = true;
                    EndpointVolumePercent = audio.GetDevice(SelectedCaptureDevice.Id).Volume * 100;
                    suppressEndpointUpdate = false;
                }
                LastMessage = $"Perfil '{profile.Name}' aplicado.";
            },
            "Não foi possível aplicar o perfil.");
    }

    private void SaveCurrentProfile()
    {
        var name = ProfileName.Trim();
        if (name.Length == 0)
        {
            LastMessage = "Informe um nome para o perfil.";
            return;
        }

        TryExecute(
            () =>
            {
                var profile = new AudioProfile
                {
                    Name = name,
                    CaptureDeviceId = SelectedCaptureDevice?.Id,
                    MonitorDeviceId = SelectedMonitorDevice?.Id,
                    CaptureVolume = (float)(EndpointVolumePercent / 100),
                    DigitalGainDb = (float)DigitalGainDb,
                    RecordingDirectory = RecordingDirectory
                };
                var profiles = settings.Profiles.ToList();
                var index = profiles.FindIndex(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    profiles[index] = profile;
                }
                else
                {
                    profiles.Add(profile);
                }

                settings = settings with { CurrentProfile = name, Profiles = profiles };
                settingsStore.Save(settings);
                ReloadProfileNames();
                SelectedProfileName = name;
                LastMessage = $"Perfil '{name}' salvo.";
            },
            "Não foi possível salvar o perfil.");
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        UpdateRoutingMeters();
        if (SelectedCaptureDevice is not null)
        {
            try
            {
                PeakPercent = audio.GetPeak(SelectedCaptureDevice.Id) * 100;
            }
            catch
            {
                PeakPercent = 0;
            }
        }

        if (IsRecording)
        {
            RecordingElapsed = (DateTimeOffset.Now - recordingStartedAt).ToString(@"hh\:mm\:ss");
        }
    }

    private void UpdateDiagnostics()
    {
        var capture = SelectedCaptureDevice is null
            ? "Captura: nenhuma."
            : $"Captura: {SelectedCaptureDevice.Name} | {SelectedCaptureDevice.Id}";
        var render = SelectedMonitorDevice is null
            ? "Saída: nenhuma."
            : $"Saída: {SelectedMonitorDevice.Name} | {SelectedMonitorDevice.Id}";
        DiagnosticSummary = $"{capture}{Environment.NewLine}{render}{Environment.NewLine}Logs: {logDirectory}";
    }

    private void CopyDiagnostics()
    {
        TryExecute(
            () =>
            {
                System.Windows.Clipboard.SetText(DiagnosticSummary);
                LastMessage = "Diagnóstico copiado para a área de transferência.";
            },
            "Não foi possível copiar o diagnóstico.");
    }

    private void OpenDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            LastMessage = "Nenhum caminho foi selecionado.";
            return;
        }

        TryExecute(
            () =>
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            },
            $"Não foi possível abrir '{path}'.");
    }

    private void OpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            LastMessage = "Nenhum arquivo foi selecionado.";
            return;
        }

        if (!File.Exists(path))
        {
            LastMessage = "A gravação selecionada não existe mais. Recarregue a lista.";
            return;
        }

        TryExecute(
            () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }),
            $"Não foi possível abrir '{path}'.");
    }

    private void ReloadProfileNames()
    {
        Replace(Profiles, settings.Profiles.Select(profile => profile.Name));
    }

    private void TryExecute(Action action, string userMessage)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            HandleError("ui.operation.failed", userMessage, ex);
        }
    }

    private void HandleError(string eventName, string userMessage, Exception exception)
    {
        logger.Error(eventName, exception);
        LastMessage = $"{userMessage} {exception.Message}";
    }

    private static AudioDeviceInfo? FindDevice(IEnumerable<AudioDeviceInfo> devices, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : devices.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string ResolveRecordingDirectory(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "FifineControl");
    }

    private static void Replace<T>(ObservableCollection<T> destination, IEnumerable<T> values)
    {
        destination.Clear();
        foreach (var value in values)
        {
            destination.Add(value);
        }
    }
}
