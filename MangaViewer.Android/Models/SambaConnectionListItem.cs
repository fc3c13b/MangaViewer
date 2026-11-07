namespace MangaViewer.Android.Models;

public sealed class SambaConnectionListItem
{
    public int Number { get; init; }

    public string FolderName { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public string ServerRoot { get; init; } = string.Empty;

    public SambaConnectionConfig Connection { get; init; } = new();
}
