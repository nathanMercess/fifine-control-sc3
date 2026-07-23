using System.IO;
using System.Windows;
using FifineControl.App.ViewModels;
using FifineControl.Core.Audio;
using FifineControl.Core.Configuration;
using FifineControl.Core.Hotkeys;
using FifineControl.Core.Integrations.Obs;
using FifineControl.Core.Logging;
using FifineControl.Core.Recording;
using FifineControl.Core.Startup;

namespace FifineControl.App;

public partial class App : System.Windows.Application
{
    private MainViewModel? viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FifineControl");
        var logDirectory = Path.Combine(appDirectory, "logs");
        var logger = new JsonFileLogger(Path.Combine(logDirectory, $"fifine-{DateTime.Now:yyyyMMdd}.jsonl"));
        var settingsStore = new JsonSettingsStore(Path.Combine(appDirectory, "settings.json"), logger);
        var audio = new WindowsAudioEndpointService(logger);
        var recorder = new WavRecordingService(logger);
        var obs = new ObsWebSocketService(logger);
        var hotkeys = new WindowsGlobalHotkeyService(logger);
        var recordingFiles = new RecordingFileManager();
        var startup = new WindowsStartupRegistrationService(new CurrentUserRunRegistry(), logger);

        viewModel = new MainViewModel(
            audio,
            recorder,
            settingsStore,
            logger,
            logDirectory,
            obs,
            hotkeys,
            recordingFiles,
            startup,
            ResolveStartupExecutablePath());
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        viewModel?.Dispose();
        base.OnExit(e);
    }

    private static string ResolveStartupExecutablePath()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Nao foi possivel determinar o executavel atual.");
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var appHost = Path.Combine(AppContext.BaseDirectory, "FifineControl.exe");
            if (File.Exists(appHost))
            {
                return appHost;
            }
        }

        return processPath;
    }
}
