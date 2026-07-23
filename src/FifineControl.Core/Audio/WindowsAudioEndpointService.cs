using FifineControl.Core.Logging;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace FifineControl.Core.Audio;

public sealed class WindowsAudioEndpointService(IAppLogger logger) : IAudioEndpointService
{
    public IReadOnlyList<AudioDeviceInfo> GetActiveDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaults = GetDefaultIds(enumerator);
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
            .Select(device => Map(device, defaults))
            .OrderBy(device => device.Direction)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        logger.Info("audio.devices.enumerated", new { count = devices.Length });
        return devices;
    }

    public AudioDeviceInfo GetDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        return Map(device, GetDefaultIds(enumerator));
    }

    public bool SetMute(string deviceId, bool muted)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.Mute = muted;
        var actual = device.AudioEndpointVolume.Mute;
        logger.Info("audio.mute.changed", new { deviceId, requested = muted, actual });
        return actual;
    }

    public bool ToggleMute(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        var requested = !device.AudioEndpointVolume.Mute;
        device.AudioEndpointVolume.Mute = requested;
        var actual = device.AudioEndpointVolume.Mute;
        logger.Info("audio.mute.toggled", new { deviceId, actual });
        return actual;
    }

    public float SetVolume(string deviceId, float scalar)
    {
        if (scalar is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scalar), "Volume must be between 0 and 1.");
        }

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
        var actual = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        logger.Info("audio.volume.changed", new { deviceId, requested = scalar, actual });
        return actual;
    }

    public float GetPeak(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        return device.AudioMeterInformation.MasterPeakValue;
    }

    private static HashSet<string> GetDefaultIds(MMDeviceEnumerator enumerator)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TryAddDefault(enumerator, DataFlow.Capture, result);
        TryAddDefault(enumerator, DataFlow.Render, result);
        return result;
    }

    private static void TryAddDefault(MMDeviceEnumerator enumerator, DataFlow flow, ISet<string> result)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            result.Add(device.ID);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // A machine can legitimately have no active endpoint for one direction.
        }
    }

    private static AudioDeviceInfo Map(MMDevice device, IReadOnlySet<string> defaultIds)
    {
        var direction = device.DataFlow == DataFlow.Capture
            ? AudioDeviceDirection.Capture
            : AudioDeviceDirection.Render;

        return new AudioDeviceInfo(
            device.ID,
            device.FriendlyName,
            direction,
            defaultIds.Contains(device.ID),
            device.AudioEndpointVolume.Mute,
            device.AudioEndpointVolume.MasterVolumeLevelScalar,
            device.AudioMeterInformation.MasterPeakValue);
    }
}
