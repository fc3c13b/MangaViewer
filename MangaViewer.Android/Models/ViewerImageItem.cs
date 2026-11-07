namespace MangaViewer.Android.Models;

public sealed class ViewerImageItem
{
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public override string ToString() => Name;
}
