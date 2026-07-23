using System.Buffers.Binary;

namespace FifineControl.Core.Recording;

public static class WavRecovery
{
    public static bool TryRepair(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (stream.Length < 44 || stream.Length > uint.MaxValue)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[12];
        if (stream.Read(header) != header.Length ||
            !header[..4].SequenceEqual("RIFF"u8) ||
            !header[8..12].SequenceEqual("WAVE"u8))
        {
            return false;
        }

        long? dataSizePosition = null;
        long? dataStart = null;
        Span<byte> chunkHeader = stackalloc byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            if (stream.Read(chunkHeader) != chunkHeader.Length)
            {
                return false;
            }

            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            if (chunkHeader[..4].SequenceEqual("data"u8))
            {
                dataSizePosition = stream.Position - 4;
                dataStart = stream.Position;
                break;
            }

            var next = stream.Position + chunkSize + (chunkSize & 1);
            if (next > stream.Length)
            {
                return false;
            }

            stream.Position = next;
        }

        if (dataSizePosition is null || dataStart is null)
        {
            return false;
        }

        var dataLength = stream.Length - dataStart.Value;
        if (dataLength > uint.MaxValue)
        {
            return false;
        }

        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(value, checked((uint)(stream.Length - 8)));
        stream.Position = 4;
        stream.Write(value);
        BinaryPrimitives.WriteUInt32LittleEndian(value, checked((uint)dataLength));
        stream.Position = dataSizePosition.Value;
        stream.Write(value);
        stream.Flush(flushToDisk: true);
        return true;
    }
}
