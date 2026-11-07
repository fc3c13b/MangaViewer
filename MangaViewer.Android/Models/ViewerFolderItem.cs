namespace MangaViewer.Android.Models;

public sealed class ViewerFolderItem
{
    public string Name { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public override string ToString() => Name;
}
