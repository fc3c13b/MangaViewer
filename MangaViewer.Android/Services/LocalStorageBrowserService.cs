using Android.Content;
using AndroidX.DocumentFile.Provider;
using MangaViewer.Android.Models;
using Microsoft.Maui.ApplicationModel;

namespace MangaViewer.Android.Services;

public sealed class LocalStorageBrowserService
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] ArchiveExtensions = [".cbz", ".zip"];

    public async Task<string?> PickFolderAsync()
    {
        return await MainActivity.PickFolderAsync();
    }

    public Task<IReadOnlyList<ViewerFolderItem>> ListSubfoldersAsync(string treeUri)
    {
        var result = new List<ViewerFolderItem>();
        var activity = Platform.CurrentActivity;
        if (activity == null || string.IsNullOrWhiteSpace(treeUri))
            return Task.FromResult<IReadOnlyList<ViewerFolderItem>>(result);

        var uri = global::Android.Net.Uri.Parse(treeUri);
        if (uri == null)
            return Task.FromResult<IReadOnlyList<ViewerFolderItem>>(result);

        var root = DocumentFile.FromTreeUri(activity, uri);
        if (root == null)
            return Task.FromResult<IReadOnlyList<ViewerFolderItem>>(result);

        foreach (var child in root.ListFiles().Where(x => x.IsDirectory))
        {
            result.Add(new ViewerFolderItem
            {
                Name = child.Name ?? "(no-name)",
                Location = child.Uri?.ToString() ?? string.Empty,
                Source = "local"
            });
        }

        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return Task.FromResult<IReadOnlyList<ViewerFolderItem>>(result);
    }

    public Task<IReadOnlyList<string>> ListImageUrisAsync(string folderUri)
    {
        var result = new List<string>();
        var activity = Platform.CurrentActivity;
        if (activity == null || string.IsNullOrWhiteSpace(folderUri))
            return Task.FromResult<IReadOnlyList<string>>(result);

        var uri = global::Android.Net.Uri.Parse(folderUri);
        if (uri == null)
            return Task.FromResult<IReadOnlyList<string>>(result);

        var folder = DocumentFile.FromTreeUri(activity, uri);
        if (folder == null)
            return Task.FromResult<IReadOnlyList<string>>(result);

        foreach (var file in folder.ListFiles().Where(x => x.IsFile))
        {
            string? name = file.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (!ImageExtensions.Contains(ext))
                continue;

            string itemUri = file.Uri?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(itemUri))
                continue;

            result.Add(itemUri);
        }

        result.Sort(new CbzVolumeComparer());
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<IReadOnlyList<string>> ListArchiveUrisAsync(string folderUri)
    {
        var result = new List<string>();
        var activity = Platform.CurrentActivity;
        if (activity == null || string.IsNullOrWhiteSpace(folderUri))
            return Task.FromResult<IReadOnlyList<string>>(result);

        var uri = global::Android.Net.Uri.Parse(folderUri);
        if (uri == null)
            return Task.FromResult<IReadOnlyList<string>>(result);

        var folder = DocumentFile.FromTreeUri(activity, uri);
        if (folder == null)
            return Task.FromResult<IReadOnlyList<string>>(result);

        foreach (var file in folder.ListFiles().Where(x => x.IsFile))
        {
            string? name = file.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (!ArchiveExtensions.Contains(ext))
                continue;

            string itemUri = file.Uri?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(itemUri))
                continue;

            result.Add(itemUri);
        }

        result.Sort(new CbzVolumeComparer());
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Stream? OpenRead(string fileUri)
    {
        var activity = Platform.CurrentActivity;
        if (activity == null || string.IsNullOrWhiteSpace(fileUri))
            return null;

        var uri = global::Android.Net.Uri.Parse(fileUri);
        if (uri == null)
            return null;

        return activity.ContentResolver?.OpenInputStream(uri);
    }
}
