namespace FifineControl.Core.Routing;

public sealed record AudioRoutingSession(
    string CaptureDeviceId,
    string CaptureDeviceName,
    string RenderDeviceId,
    string RenderDeviceName,
    DateTimeOffset StartedAt,
    bool HasFeedbackRisk,
    string? Warning);
