namespace FifineControl.Core.Logging;

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();
    private NullAppLogger() { }
    public void Info(string eventName, object? data = null) { }
    public void Error(string eventName, Exception exception, object? data = null) { }
}
