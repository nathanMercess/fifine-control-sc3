using FifineControl.Core.Audio;
using FifineControl.Core.Logging;

namespace FifineControl.Core.Configuration;

public sealed class ProfileService(IAudioEndpointService audio, JsonSettingsStore store, IAppLogger logger)
{
    public AppSettings Apply(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        var settings = store.LoadOrCreate();
        var profile = settings.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Profile '{profileName}' was not found.");

        if (!string.IsNullOrWhiteSpace(profile.CaptureDeviceId))
        {
            audio.SetVolume(profile.CaptureDeviceId, profile.CaptureVolume);
        }

        var updated = settings with { CurrentProfile = profile.Name };
        store.Save(updated);
        logger.Info("profile.applied", new { profile.Name, profile.CaptureDeviceId, profile.CaptureVolume });
        return updated;
    }
}
