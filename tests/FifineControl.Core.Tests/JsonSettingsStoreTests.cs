using FifineControl.Core.Configuration;
using FifineControl.Core.Logging;

namespace FifineControl.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FifineControlTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadOrCreate_RoundTripsProfiles()
    {
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonSettingsStore(path, NullAppLogger.Instance);
        var expected = new AppSettings
        {
            CurrentProfile = "Podcast",
            Profiles =
            [
                new AudioProfile
                {
                    Name = "Podcast",
                    CaptureDeviceId = "device-1",
                    CaptureVolume = .75f,
                    EnabledFilters = ["gate", "compressor"]
                }
            ]
        };

        store.Save(expected);
        var actual = store.LoadOrCreate();

        Assert.Equal("Podcast", actual.CurrentProfile);
        var profile = Assert.Single(actual.Profiles);
        Assert.Equal("device-1", profile.CaptureDeviceId);
        Assert.Equal(.75f, profile.CaptureVolume);
    }

    [Fact]
    public void Save_RejectsCurrentProfileThatDoesNotExist()
    {
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"), NullAppLogger.Instance);
        var invalid = new AppSettings { CurrentProfile = "Missing" };

        Assert.Throws<InvalidDataException>(() => store.Save(invalid));
    }

    [Fact]
    public void Save_RejectsInvalidObsUri()
    {
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"), NullAppLogger.Instance);
        var wrongScheme = new AppSettings
        {
            Obs = new ObsWebSocketSettings { ServerUri = "https://127.0.0.1:4455" }
        };
        var embeddedCredentials = new AppSettings
        {
            Obs = new ObsWebSocketSettings { ServerUri = "ws://user:secret@127.0.0.1:4455" }
        };

        Assert.Throws<InvalidDataException>(() => store.Save(wrongScheme));
        Assert.Throws<InvalidDataException>(() => store.Save(embeddedCredentials));
    }

    [Fact]
    public void Save_NeverPersistsObsPassword()
    {
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonSettingsStore(path, NullAppLogger.Instance);
        var settings = new AppSettings
        {
            Obs = new ObsWebSocketSettings
            {
                ServerUri = "ws://127.0.0.1:4455",
                Password = "super-secret-value"
            }
        };

        store.Save(settings);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("super-secret-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, store.LoadOrCreate().Obs.Password);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
