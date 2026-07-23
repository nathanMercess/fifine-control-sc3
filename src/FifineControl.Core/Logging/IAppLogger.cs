namespace FifineControl.Core.Logging;

public interface IAppLogger
{
    void Info(string eventName, object? data = null);
    void Error(string eventName, Exception exception, object? data = null);
}
