namespace FifineControl.Core.Startup;

public interface IStartupRegistry
{
    string? GetString(string valueName);
    void SetString(string valueName, string value);
    void DeleteValue(string valueName);
}
