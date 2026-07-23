namespace FifineControl.Core.Startup;

public interface IStartupRegistrationService
{
    bool IsEnabled(string executablePath);
    void SetEnabled(bool enabled, string executablePath);
}
