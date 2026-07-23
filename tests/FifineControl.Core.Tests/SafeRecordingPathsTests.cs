using FifineControl.Core.Recording;

namespace FifineControl.Core.Tests;

public sealed class SafeRecordingPathsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FifineControlTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateUniqueWavPath_SanitizesAndAvoidsExistingFiles()
    {
        var timestamp = new DateTimeOffset(2026, 7, 21, 12, 34, 56, TimeSpan.Zero);
        var first = SafeRecordingPaths.CreateUniqueWavPath(directory, "podcast: test", timestamp);
        File.WriteAllText(first, "occupied");

        var second = SafeRecordingPaths.CreateUniqueWavPath(directory, "podcast: test", timestamp);

        Assert.EndsWith("2026-07-21_12-34-56_podcast_ test_1.wav", second);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
