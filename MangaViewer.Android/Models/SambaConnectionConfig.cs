namespace MangaViewer.Android.Models;

public sealed class SambaConnectionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public string ShareName { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 300;

    public string ToSmbUri()
    {
        if (string.IsNullOrWhiteSpace(ShareName))
            return $"smb://{Host}/";

        string normalizedRoot = RootPath.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedRoot))
            return $"smb://{Host}/{ShareName}";

        return $"smb://{Host}/{ShareName}/{normalizedRoot}";
    }

    public override string ToString() => DisplayName;
}
