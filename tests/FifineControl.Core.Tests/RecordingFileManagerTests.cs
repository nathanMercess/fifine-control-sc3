using FifineControl.Core.Recording;

namespace FifineControl.Core.Tests;

public sealed class RecordingFileManagerTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "FifineControlRecordingTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Rename_SanitizesNameAndKeepsWavExtension()
    {
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "old.wav");
        File.WriteAllBytes(source, [1, 2, 3]);
        var manager = new RecordingFileManager();

        var renamed = manager.Rename(directory, source, "new: episode.wav");

        Assert.Equal("new_ episode.wav", Path.GetFileName(renamed));
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void ValidateManagedWav_RejectsFileOutsideConfiguredDirectory()
    {
        Directory.CreateDirectory(directory);
        var outsideDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDirectory);
        var outside = Path.Combine(outsideDirectory, "outside.wav");
        File.WriteAllBytes(outside, [1]);
        try
        {
            var manager = new RecordingFileManager();
            Assert.Throws<InvalidDataException>(() => manager.ValidateManagedWav(directory, outside));
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public void Delete_RejectsAndPreservesPartialFile()
    {
        Directory.CreateDirectory(directory);
        var partial = Path.Combine(directory, "recording.wav.partial");
        File.WriteAllBytes(partial, [1, 2, 3]);
        var manager = new RecordingFileManager();

        Assert.Throws<InvalidDataException>(() => manager.Delete(directory, partial));
        Assert.True(File.Exists(partial));
    }

    [Fact]
    public void SanitizeRecordingName_RejectsDirectoryTraversal()
    {
        var manager = new RecordingFileManager();

        Assert.Throws<InvalidDataException>(() => manager.SanitizeRecordingName("..\\escape.wav"));
        Assert.Throws<InvalidDataException>(() => manager.SanitizeRecordingName("../escape.wav"));
        Assert.Equal("_CON.txt.wav", manager.SanitizeRecordingName("CON.txt"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
