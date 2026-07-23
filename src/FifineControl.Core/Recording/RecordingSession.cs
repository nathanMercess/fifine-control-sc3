namespace FifineControl.Core.Recording;

public sealed record RecordingSession(
    string DeviceId,
    string TemporaryPath,
    string FinalPath,
    DateTimeOffset StartedAt,
    long BytesWritten);
