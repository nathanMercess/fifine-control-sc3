using FifineControl.Core.Integrations.Obs;

namespace FifineControl.Core.Tests;

public sealed class ObsAuthenticationTests
{
    [Fact]
    public void CreateResponse_UsesObsWebSocketFiveChallengeAlgorithm()
    {
        var response = ObsAuthentication.CreateResponse("secret", "salt", "challenge");

        Assert.Equal("39cfhx7et2iyoMZvoQ6o3OPLNSKgtMmy48GQ7jnvsdE=", response);
    }
}
