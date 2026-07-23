namespace FifineControl.App.ViewModels;

public sealed record RecentRecordingItem(string Path, string Name, long SizeBytes, DateTime CreatedAt)
{
    public string SizeText => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024d:0} KB"
        : $"{SizeBytes / 1024d / 1024d:0.0} MB";

    public string CreatedText => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");
}
