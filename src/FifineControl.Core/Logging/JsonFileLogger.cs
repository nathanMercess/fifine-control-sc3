using System.Text.Json;

namespace FifineControl.Core.Logging;

public sealed class JsonFileLogger : IAppLogger
{
    private readonly object sync = new();
    private readonly string path;
    private readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

    public JsonFileLogger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
    }

    public void Info(string eventName, object? data = null) => Write("Information", eventName, data, null);

    public void Error(string eventName, Exception exception, object? data = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write("Error", eventName, data, new { type = exception.GetType().FullName, exception.Message, exception.StackTrace });
    }

    private void Write(string level, string eventName, object? data, object? error)
    {
        var entry = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level,
            eventName,
            data,
            error
        };

        var line = JsonSerializer.Serialize(entry, options);
        lock (sync)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
