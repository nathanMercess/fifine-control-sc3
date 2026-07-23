namespace FifineControl.Core.Audio;

public interface IAudioEndpointService
{
    IReadOnlyList<AudioDeviceInfo> GetActiveDevices();
    AudioDeviceInfo GetDevice(string deviceId);
    bool SetMute(string deviceId, bool muted);
    bool ToggleMute(string deviceId);
    float SetVolume(string deviceId, float scalar);
    float GetPeak(string deviceId);
}
