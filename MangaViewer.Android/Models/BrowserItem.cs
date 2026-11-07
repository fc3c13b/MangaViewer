namespace MangaViewer.Android.Models;

public sealed class BrowserItem
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    public bool IsParentLink { get; init; }

    public override string ToString() => Name;
}
