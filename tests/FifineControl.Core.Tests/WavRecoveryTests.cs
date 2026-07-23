using System.Buffers.Binary;
using FifineControl.Core.Recording;

namespace FifineControl.Core.Tests;

public sealed class WavRecoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FifineControlTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryRepair_PatchesRiffAndDataLengths()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "interrupted.wav.partial");
        var file = BuildInterruptedPcmWav(100);
        File.WriteAllBytes(path, file);

        var repaired = WavRecovery.TryRepair(path);
        var actual = File.ReadAllBytes(path);

        Assert.True(repaired);
        Assert.Equal((uint)(actual.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(4, 4)));
        Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(40, 4)));
    }

    [Fact]
    public void TryRepair_RejectsNonWav()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "bad.wav.partial");
        File.WriteAllBytes(path, new byte[50]);

        Assert.False(WavRecovery.TryRepair(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] BuildInterruptedPcmWav(int dataLength)
    {
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        "fmt "u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 48_000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 96_000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        return bytes;
    }
}
