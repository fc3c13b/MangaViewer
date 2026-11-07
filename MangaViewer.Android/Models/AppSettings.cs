namespace MangaViewer.Android.Models;

public sealed class AppSettings
{
    public int MinDisplayCount { get; set; } = 20;

    public int MaxDisplayCount { get; set; } = 1000;

    public int MinEvaluation { get; set; } = 8;

    public int DisplayCount { get; set; } = 2;

    public int NormalModeImageAreaPercent { get; set; } = 72;

    public int FullScreenModeImageAreaPercent { get; set; } = 85;

    public bool StartFromLastViewedPosition { get; set; } = true;

    public bool MinEvaluationFilterEnabled { get; set; } = true;

    public bool MinEvaluationEqualFilter { get; set; }

    public string? LastViewedFolderLocation { get; set; }

    public string? LastViewedImagePath { get; set; }

    public bool CbzSupportHintShown { get; set; }

    public string? LocalRootUri { get; set; }

    public string? SambaRootPath { get; set; }

    public string? LastSourceType { get; set; }

    public string? DefaultSambaConnectionId { get; set; }

    public string? SharedCacheConnectionId { get; set; }

    public string? SharedCacheSambaRootPath { get; set; }

    public List<SambaConnectionConfig> SambaConnections { get; set; } = new();
}
