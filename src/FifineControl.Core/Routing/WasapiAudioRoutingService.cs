using FifineControl.Core.Dsp;
using FifineControl.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace FifineControl.Core.Routing;

public sealed class WasapiAudioRoutingService(IAppLogger logger) : IAudioRoutingService
{
    private readonly SemaphoreSlim stateLock = new(1, 1);
    private WasapiCapture? capture;
    private WasapiOut? output;
    private BufferedWaveProvider? buffer;
    private AudioDspProcessor? processor;
    private AudioRoutingSession? current;
    private bool stopping;
    private bool disposed;

    public AudioRoutingSession? Current => current;
    public float PreDspPeak => processor?.PrePeak ?? 0;
    public float PostDspPeak => processor?.PostPeak ?? 0;

    public async Task<AudioRoutingSession> StartAsync(
        string captureDeviceId,
        string renderDeviceId,
        DspSettings settings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        await stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current is not null)
            {
                throw new InvalidOperationException("An audio route is already active.");
            }

            using var enumerator = new MMDeviceEnumerator();
            using var captureDevice = enumerator.GetDevice(captureDeviceId);
            using var renderDevice = enumerator.GetDevice(renderDeviceId);
            if (captureDevice.DataFlow != DataFlow.Capture || captureDevice.State != DeviceState.Active)
            {
                throw new InvalidOperationException("The source endpoint is not an active capture device.");
            }

            if (renderDevice.DataFlow != DataFlow.Render || renderDevice.State != DeviceState.Active)
            {
                throw new InvalidOperationException("The destination endpoint is not an active render device.");
            }

            RoutingSafety.EnsurePossibleRoute(captureDevice.ID, captureDevice.FriendlyName, renderDevice.ID, renderDevice.FriendlyName);
            var warning = RoutingSafety.GetFeedbackWarning(captureDevice.FriendlyName, renderDevice.FriendlyName);

            var newCapture = new WasapiCapture(captureDevice);
            settings.Validate(newCapture.WaveFormat.SampleRate);
            var newBuffer = new BufferedWaveProvider(newCapture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            var newProcessor = new AudioDspProcessor(
                newCapture.WaveFormat.SampleRate,
                newCapture.WaveFormat.Channels,
                settings);
            var sampleProvider = new DspSampleProvider(newBuffer, newProcessor);
            var newOutput = new WasapiOut(renderDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 60);
            newOutput.Init(new SampleToWaveProvider(sampleProvider));

            capture = newCapture;
            buffer = newBuffer;
            processor = newProcessor;
            output = newOutput;
            stopping = false;
            newCapture.DataAvailable += OnDataAvailable;
            newCapture.RecordingStopped += OnCaptureStopped;
            newOutput.PlaybackStopped += OnOutputStopped;

            try
            {
                newOutput.Play();
                newCapture.StartRecording();
            }
            catch
            {
                DisposeRouteResources();
                throw;
            }

            current = new AudioRoutingSession(
                captureDevice.ID,
                captureDevice.FriendlyName,
                renderDevice.ID,
                renderDevice.FriendlyName,
                DateTimeOffset.Now,
                warning is not null,
                warning);
            logger.Info("routing.started", new
            {
                current.CaptureDeviceId,
                current.CaptureDeviceName,
                current.RenderDeviceId,
                current.RenderDeviceName,
                format = newCapture.WaveFormat.ToString(),
                current.Warning
            });
            return current;
        }
        catch (Exception ex)
        {
            logger.Error("routing.start.failed", ex, new { captureDeviceId, renderDeviceId });
            throw;
        }
        finally
        {
            stateLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current is null)
            {
                return;
            }

            stopping = true;
            var completed = current;
            DisposeRouteResources();
            current = null;
            logger.Info("routing.stopped", new
            {
                completed.CaptureDeviceId,
                completed.RenderDeviceId,
                elapsedSeconds = (DateTimeOffset.Now - completed.StartedAt).TotalSeconds
            });
        }
        finally
        {
            stopping = false;
            stateLock.Release();
        }
    }

    public void UpdateSettings(DspSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        var activeProcessor = processor ?? throw new InvalidOperationException("No audio route is active.");
        activeProcessor.UpdateSettings(settings);
        logger.Info("routing.dsp.updated", new
        {
            settings.DigitalGainDb,
            settings.GainBypassed,
            gateBypassed = settings.NoiseGate.Bypassed,
            compressorBypassed = settings.Compressor.Bypassed,
            equalizer = settings.EqualizerBands.Select(band => new { band.Name, band.Bypassed, band.FrequencyHz, band.Q, band.GainDb })
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        if (current is not null)
        {
            await StopAsync().ConfigureAwait(false);
        }

        disposed = true;
        stateLock.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        try
        {
            buffer?.AddSamples(args.Buffer, 0, args.BytesRecorded);
        }
        catch (Exception ex)
        {
            logger.Error("routing.buffer.write.failed", ex, new { args.BytesRecorded });
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs args)
    {
        if (stopping)
        {
            return;
        }

        LogUnexpectedStop("capture", args.Exception);
        _ = StopAfterUnexpectedEndAsync("capture");
    }

    private void OnOutputStopped(object? sender, StoppedEventArgs args)
    {
        if (stopping)
        {
            return;
        }

        LogUnexpectedStop("output", args.Exception);
        _ = StopAfterUnexpectedEndAsync("output");
    }

    private void DisposeRouteResources()
    {
        var captureToDispose = capture;
        var outputToDispose = output;
        capture = null;
        output = null;
        buffer = null;
        processor = null;

        if (captureToDispose is not null)
        {
            captureToDispose.DataAvailable -= OnDataAvailable;
            captureToDispose.RecordingStopped -= OnCaptureStopped;
            try
            {
                captureToDispose.StopRecording();
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                logger.Error("routing.capture.stop.failed", ex);
            }
            finally
            {
                captureToDispose.Dispose();
            }
        }

        if (outputToDispose is not null)
        {
            outputToDispose.PlaybackStopped -= OnOutputStopped;
            try
            {
                outputToDispose.Stop();
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                logger.Error("routing.output.stop.failed", ex);
            }
            finally
            {
                outputToDispose.Dispose();
            }
        }
    }

    private async Task StopAfterUnexpectedEndAsync(string component)
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error("routing.unexpected-stop.cleanup.failed", ex, new { component });
        }
    }

    private void LogUnexpectedStop(string component, Exception? exception)
    {
        if (exception is null)
        {
            logger.Info("routing.component.stopped-unexpectedly", new { component });
        }
        else
        {
            logger.Error("routing.component.stopped-unexpectedly", exception, new { component });
        }
    }
}
