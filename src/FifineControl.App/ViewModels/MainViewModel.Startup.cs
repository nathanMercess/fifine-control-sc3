using FifineControl.Core.Startup;

namespace FifineControl.App.ViewModels;

public sealed partial class MainViewModel
{
    private IStartupRegistrationService startupRegistration = null!;
    private string startupExecutablePath = string.Empty;
    private bool runAtStartup;

    public bool RunAtStartup
    {
        get => runAtStartup;
        set
        {
            if (!SetProperty(ref runAtStartup, value))
            {
                return;
            }

            try
            {
                startupRegistration.SetEnabled(value, startupExecutablePath);
                LastMessage = value
                    ? "Inicializacao com o Windows habilitada para este usuario."
                    : "Inicializacao com o Windows desabilitada.";
            }
            catch (Exception ex)
            {
                runAtStartup = !value;
                OnPropertyChanged();
                HandleError(
                    "startup.ui.update.failed",
                    "Nao foi possivel alterar a inicializacao com o Windows.",
                    ex);
            }
        }
    }

    private void InitializeStartup(
        IStartupRegistrationService registrationService,
        string executablePath)
    {
        startupRegistration = registrationService;
        startupExecutablePath = executablePath;
        try
        {
            // This is read-only. Startup is never enabled merely by constructing the app or tests.
            runAtStartup = startupRegistration.IsEnabled(startupExecutablePath);
        }
        catch (Exception ex)
        {
            runAtStartup = false;
            logger.Error("startup.ui.read.failed", ex);
        }
    }
}
