using FifineControl.Core.Routing;

namespace FifineControl.Core.Tests;

public sealed class RoutingSafetyTests
{
    [Fact]
    public void EnsurePossibleRoute_RejectsSameEndpointId()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RoutingSafety.EnsurePossibleRoute(
            "endpoint-1",
            "Microphone",
            "ENDPOINT-1",
            "Speakers"));

        Assert.Contains("identical", error.Message);
    }

    [Fact]
    public void EnsurePossibleRoute_RejectsEquivalentEndpointNames()
    {
        Assert.Throws<InvalidOperationException>(() => RoutingSafety.EnsurePossibleRoute(
            "capture-1",
            "VB-Cable Input",
            "render-2",
            "  VB-Cable   Input "));
    }

    [Fact]
    public void GetFeedbackWarning_FlagsEndpointsOnSamePhysicalDevice()
    {
        var warning = RoutingSafety.GetFeedbackWarning(
            "Microphone (fifine SC3)",
            "Speakers (FIFINE SC3)");

        Assert.NotNull(warning);
        Assert.Contains("feedback", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFeedbackWarning_AllowsVirtualCableDestination()
    {
        var warning = RoutingSafety.GetFeedbackWarning(
            "Microphone (fifine SC3)",
            "CABLE Input (VB-Audio Virtual Cable) ");

        Assert.Null(warning);
    }
}
