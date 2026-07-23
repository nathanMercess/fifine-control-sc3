using System.Text.Json;
using FifineControl.Core.Logging;

namespace FifineControl.Core.Configuration;

public sealed class JsonSettingsStore(string path, IAppLogger logger)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string path = Path.GetFullPath(path);

    public AppSettings LoadOrCreate()
    {
        if (!File.Exists(path))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)
                ?? throw new InvalidDataException("The settings file is empty.");
            Validate(settings);
            logger.Info("settings.loaded", new { path, settings.SchemaVersion, profileCount = settings.Profiles.Count });
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            var backup = path + $".invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backup, overwrite: false);
            logger.Error("settings.load.failed", ex, new { path, backup });
            throw new InvalidDataException($"Invalid settings. A backup was written to '{backup}'.", ex);
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, path, overwrite: true);
            logger.Info("settings.saved", new { path, settings.SchemaVersion, profileCount = settings.Profiles.Count });
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported settings schema {settings.SchemaVersion}.");
        }

        if (settings.Profiles.Count == 0)
        {
            throw new InvalidDataException("At least one profile is required.");
        }

        if (settings.Profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
        {
            throw new InvalidDataException("Every profile must have a name.");
        }

        if (settings.Profiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Profiles.Count)
        {
            throw new InvalidDataException("Profile names must be unique.");
        }

        if (!settings.Profiles.Any(profile => string.Equals(profile.Name, settings.CurrentProfile, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("CurrentProfile must reference an existing profile.");
        }

        if (settings.Profiles.Any(profile => profile.CaptureVolume is < 0 or > 1))
        {
            throw new InvalidDataException("CaptureVolume must be between 0 and 1.");
        }

        if (!Uri.TryCreate(settings.Obs.ServerUri, UriKind.Absolute, out var obsUri) ||
            (obsUri.Scheme != Uri.UriSchemeWs && obsUri.Scheme != Uri.UriSchemeWss) ||
            !string.IsNullOrEmpty(obsUri.UserInfo) ||
            !string.IsNullOrEmpty(obsUri.Query) ||
            !string.IsNullOrEmpty(obsUri.Fragment))
        {
            throw new InvalidDataException(
                "Obs.ServerUri must be an absolute ws:// or wss:// URI without credentials, query, or fragment.");
        }

        if (settings.Hotkeys.MuteVirtualKey is 0 or > 0xFF ||
            settings.Hotkeys.RecordingVirtualKey is 0 or > 0xFF)
        {
            throw new InvalidDataException("Hotkey virtual-key values must be between 1 and 0xFF.");
        }
    }
}
