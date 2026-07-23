namespace FifineControl.Core.Integrations.Obs;

public interface IObsWebSocketService : IAsyncDisposable
{
    event EventHandler<ObsConnectionStateChangedEventArgs>? ConnectionStateChanged;

    ObsConnectionState ConnectionState { get; }
    string? ServerVersion { get; }

    Task ConnectAsync(Uri serverUri, string? password, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<ObsRecordingStatus> GetRecordingStatusAsync(CancellationToken cancellationToken = default);
    Task StartRecordingAsync(CancellationToken cancellationToken = default);
    Task StopRecordingAsync(CancellationToken cancellationToken = default);
}

public enum ObsConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted
}

public sealed class ObsConnectionStateChangedEventArgs : EventArgs
{
    public ObsConnectionStateChangedEventArgs(
        ObsConnectionState state,
        string? serverVersion = null,
        string? errorMessage = null)
    {
        State = state;
        ServerVersion = serverVersion;
        ErrorMessage = errorMessage;
    }

    public ObsConnectionState State { get; }
    public string? ServerVersion { get; }
    public string? ErrorMessage { get; }
}

public sealed record ObsRecordingStatus(
    bool IsActive,
    bool IsPaused,
    string? Timecode,
    long? DurationMilliseconds,
    long? Bytes);

public sealed class ObsWebSocketException : Exception
{
    public ObsWebSocketException(string message, int? requestStatusCode = null)
        : base(message)
    {
        RequestStatusCode = requestStatusCode;
    }

    public ObsWebSocketException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int? RequestStatusCode { get; }
}
