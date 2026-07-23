using FifineControl.Core.Logging;

namespace FifineControl.Core.Startup;

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    public const string ValueName = "FifineControl";

    private readonly IStartupRegistry registry;
    private readonly IAppLogger logger;

    public WindowsStartupRegistrationService(IStartupRegistry registry, IAppLogger? logger = null)
    {
        this.registry = registry;
        this.logger = logger ?? NullAppLogger.Instance;
    }

    public bool IsEnabled(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return registry.GetString(ValueName) is not null;
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (enabled)
        {
            registry.SetString(ValueName, BuildRunCommand(executablePath));
            logger.Info("startup.enabled", new { ValueName });
            return;
        }

        // Delete only the value owned by this application. Other Run values are untouched.
        registry.DeleteValue(ValueName);
        logger.Info("startup.disabled", new { ValueName });
    }

    public static string BuildRunCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        if (fullPath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("O caminho do executavel contem aspas invalidas.", nameof(executablePath));
        }

        return $"\"{fullPath}\"";
    }
}
