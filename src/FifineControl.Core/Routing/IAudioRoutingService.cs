using FifineControl.Core.Dsp;

namespace FifineControl.Core.Routing;

public interface IAudioRoutingService : IAsyncDisposable
{
    AudioRoutingSession? Current { get; }
    float PreDspPeak { get; }
    float PostDspPeak { get; }
    Task<AudioRoutingSession> StartAsync(
        string captureDeviceId,
        string renderDeviceId,
        DspSettings settings,
        CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void UpdateSettings(DspSettings settings);
}
