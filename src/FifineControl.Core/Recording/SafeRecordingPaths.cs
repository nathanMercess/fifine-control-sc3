namespace FifineControl.Core.Recording;

public static class SafeRecordingPaths
{
    public static string CreateUniqueWavPath(string directory, string? label, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);

        var safeLabel = SanitizeLabel(label);
        var stem = string.IsNullOrEmpty(safeLabel)
            ? timestamp.ToString("yyyy-MM-dd_HH-mm-ss")
            : $"{timestamp:yyyy-MM-dd_HH-mm-ss}_{safeLabel}";

        var candidate = Path.Combine(fullDirectory, stem + ".wav");
        for (var suffix = 1; File.Exists(candidate) || File.Exists(candidate + ".partial"); suffix++)
        {
            candidate = Path.Combine(fullDirectory, $"{stem}_{suffix}.wav");
        }

        return candidate;
    }

    public static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string(label.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return normalized.Trim(' ', '.');
    }

    public static void EnsureFreeSpace(string directory, long requiredBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory))
            ?? throw new IOException("Could not determine the recording volume.");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException($"Insufficient disk space. Required {requiredBytes:N0} bytes; available {drive.AvailableFreeSpace:N0} bytes.");
        }
    }
}
