using Microsoft.VisualBasic.FileIO;

namespace FifineControl.Core.Recording;

public sealed class RecordingFileManager
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public string ValidateManagedWav(string recordingDirectory, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(recordingDirectory));
        var candidate = Path.GetFullPath(filePath);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(candidate)
            ?? throw new InvalidDataException("A gravacao nao possui um diretorio valido."));

        if (!string.Equals(root, parent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A gravacao selecionada esta fora do diretorio configurado.");
        }

        if (!Directory.Exists(root) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("O diretorio de gravacoes nao pode ser um link ou ponto de nova analise.");
        }

        if (!string.Equals(Path.GetExtension(candidate), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Somente gravacoes WAV finalizadas podem ser gerenciadas.");
        }

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException("A gravacao selecionada nao existe.", candidate);
        }

        if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Links e pontos de nova analise nao podem ser gerenciados.");
        }

        return candidate;
    }

    public string SanitizeRecordingName(string proposedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedName);
        var trimmed = proposedName.Trim();
        if (trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException("O nome nao pode conter um caminho de diretorio.");
        }

        if (trimmed.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(trimmed
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        if (sanitized.Length == 0)
        {
            throw new InvalidDataException("O nome da gravacao ficou vazio apos a sanitizacao.");
        }

        var firstNameComponent = sanitized.Split('.', 2)[0].TrimEnd(' ');
        if (ReservedWindowsNames.Contains(firstNameComponent))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized + ".wav";
    }

    public string Rename(string recordingDirectory, string sourcePath, string proposedName)
    {
        var source = ValidateManagedWav(recordingDirectory, sourcePath);
        var destinationName = SanitizeRecordingName(proposedName);
        var destination = Path.Combine(Path.GetDirectoryName(source)!, destinationName);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        if (File.Exists(destination))
        {
            throw new IOException($"Ja existe uma gravacao chamada '{destinationName}'.");
        }

        File.Move(source, destination);
        return destination;
    }

    public void Delete(string recordingDirectory, string filePath)
    {
        var validated = ValidateManagedWav(recordingDirectory, filePath);
        FileSystem.DeleteFile(
            validated,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }
}
