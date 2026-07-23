using FifineControl.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FifineControl.Core.Recording;

public sealed class WavRecordingService(IAppLogger logger) : IAsyncDisposable
{
    private const long MinimumFreeBytes = 50L * 1024 * 1024;
    private readonly SemaphoreSlim stateLock = new(1, 1);
    private readonly object writerLock = new();
    private WasapiCapture? capture;
    private WaveFileWriter? writer;
    private RecordingSession? session;
    private TaskCompletionSource? stopped;

    public RecordingSession? Current => session;

    public async Task<RecordingSession> StartAsync(
        string captureDeviceId,
        string outputDirectory,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureDeviceId);
        await stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (capture is not null)
            {
                throw new InvalidOperationException("A recording is already active.");
            }

            Directory.CreateDirectory(outputDirectory);
            SafeRecordingPaths.EnsureFreeSpace(outputDirectory, MinimumFreeBytes);
            var finalPath = SafeRecordingPaths.CreateUniqueWavPath(outputDirectory, label, DateTimeOffset.Now);
            var temporaryPath = finalPath + ".partial";

            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(captureDeviceId);
            if (device.DataFlow != DataFlow.Capture || device.State != DeviceState.Active)
            {
                throw new InvalidOperationException("The selected endpoint is not an active capture device.");
            }

            var newCapture = new WasapiCapture(device);
            var newWriter = new WaveFileWriter(temporaryPath, newCapture.WaveFormat);
            newCapture.DataAvailable += OnDataAvailable;
            newCapture.RecordingStopped += OnRecordingStopped;

            capture = newCapture;
            writer = newWriter;
            stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session = new RecordingSession(captureDeviceId, temporaryPath, finalPath, DateTimeOffset.Now, 0);
            newCapture.StartRecording();
            logger.Info("recording.started", new { captureDeviceId, temporaryPath, finalPath, format = newCapture.WaveFormat.ToString() });
            return session;
        }
        catch
        {
            DisposeCaptureResources();
            throw;
        }
        finally
        {
            stateLock.Release();
        }
    }

    public async Task<RecordingSession?> StopAsync(CancellationToken cancellationToken = default)
    {
        Task waitForStop;
        RecordingSession? snapshot;
        await stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (capture is null || stopped is null)
            {
                return null;
            }

            snapshot = session;
            waitForStop = stopped.Task;
            capture.StopRecording();
        }
        finally
        {
            stateLock.Release();
        }

        await waitForStop.WaitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public static IReadOnlyList<string> RecoverInterruptedRecordings(string directory, IAppLogger logger)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var recovered = new List<string>();
        foreach (var partialPath in Directory.EnumerateFiles(directory, "*.wav.partial", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (!WavRecovery.TryRepair(partialPath))
                {
                    logger.Info("recording.recovery.skipped", new { partialPath, reason = "Unrecognized or unsupported WAV." });
                    continue;
                }

                var recoveredPath = partialPath[..^".partial".Length];
                recoveredPath = Path.Combine(
                    Path.GetDirectoryName(recoveredPath)!,
                    Path.GetFileNameWithoutExtension(recoveredPath) + "_recovered.wav");
                recoveredPath = GetUniquePath(recoveredPath);
                File.Move(partialPath, recoveredPath);
                recovered.Add(recoveredPath);
                logger.Info("recording.recovered", new { partialPath, recoveredPath });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Error("recording.recovery.failed", ex, new { partialPath });
            }
        }

        return recovered;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        stateLock.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        try
        {
            lock (writerLock)
            {
                writer?.Write(args.Buffer, 0, args.BytesRecorded);
                if (session is not null)
                {
                    session = session with { BytesWritten = session.BytesWritten + args.BytesRecorded };
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("recording.write.failed", ex, new { session?.TemporaryPath });
            capture?.StopRecording();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        var completion = stopped;
        var completedSession = session;
        Exception? failure = args.Exception;

        try
        {
            lock (writerLock)
            {
                writer?.Dispose();
                writer = null;
            }

            if (failure is null && completedSession is not null)
            {
                File.Move(completedSession.TemporaryPath, completedSession.FinalPath, overwrite: false);
                logger.Info("recording.completed", new { completedSession.FinalPath, completedSession.BytesWritten });
            }
            else if (failure is not null)
            {
                logger.Error("recording.stopped.with-error", failure, new { completedSession?.TemporaryPath });
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            logger.Error("recording.finalize.failed", ex, new { completedSession?.TemporaryPath, completedSession?.FinalPath });
        }
        finally
        {
            capture?.Dispose();
            capture = null;
            session = null;
            stopped = null;
            if (failure is null)
            {
                completion?.TrySetResult();
            }
            else
            {
                completion?.TrySetException(failure);
            }
        }
    }

    private void DisposeCaptureResources()
    {
        lock (writerLock)
        {
            writer?.Dispose();
            writer = null;
        }

        capture?.Dispose();
        capture = null;
        session = null;
        stopped = null;
    }

    private static string GetUniquePath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var candidate = path;
        for (var suffix = 1; File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(directory, $"{stem}_{suffix}{extension}");
        }

        return candidate;
    }
}
