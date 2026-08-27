using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MangaViewer
{
    /// <summary>
    /// CJ（Collection JSON）の読み込み・作成・適用に関するロジックを一元管理。
    /// Form1 から呼ばれるだけで、詳細なファイル操作やデータ変換はここで完結させる。
    /// </summary>
    public static class CjService
    {
        // 指定された親フォルダ配下から既存の CJ ファイルを検索し、見つかったら読み込んで返す。
        // なければ null を返す（Form1 で新規作成へ移行する）。
        public static (string cjFile, CjRoot data)? FindAndLoadCj(string parentFolder)
        {
            if (!Directory.Exists(parentFolder)) return null;

            var candidates = Directory.GetFiles(
                parentFolder,
                "*.json",
                SearchOption.TopDirectoryOnly
            ).Where(p => p.Contains("collection_", StringComparison.OrdinalIgnoreCase)).ToArray();

            foreach (var file in candidates)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var cjRoot = new CjRoot();

                    if (doc.RootElement.TryGetProperty("parentFolder", out var pf))
                        cjRoot.ParentFolder = pf.GetString() ?? "";

                    var folders = new Dictionary<string, CjFolderEntry>();
                    if (doc.RootElement.TryGetProperty("folders", out var fj) && fj.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var entry in fj.EnumerateObject())
                        {
                            var cjEntry = new CjFolderEntry();
                            if (entry.Value.TryGetProperty("folderName", out var fn))
                                cjEntry.FolderName = fn.GetString() ?? "";
                            if (entry.Value.TryGetProperty("CbzZipCount", out var cz) && cz.ValueKind == JsonValueKind.Number)
                                cjEntry.CbzZipCount = cz.GetInt32();
                            if (entry.Value.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                                cjEntry.ImageCount = ic.GetInt32();
                            if (entry.Value.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number)
                                cjEntry.Rating = r.GetInt32();
                            if (entry.Value.TryGetProperty("updatedAt", out var ua) && ua.ValueKind == JsonValueKind.Number)
                                cjEntry.UpdatedAt = ua.GetInt64();

                            folders[entry.Name] = cjEntry;
                        }
                    }

                    cjRoot.Folders = folders;

                    if (cjRoot.Folders.Count > 0)
                        return (file, cjRoot);
                }
                catch
                {
                    // ファイル破損または形式違いはスキップ
                }
            }

            return null;
        }

        // 新しい CJ を作成・保存し、返却。Form1 から呼ばれる。
        public static (string cjFile, CjRoot data) CreateCj(string parentFolder, IEnumerable<string> folderPaths)
        {
            if (!Directory.Exists(parentFolder))
                throw new DirectoryNotFoundException($"Parent folder not found: {parentFolder}");

            var cjData = new CjRoot();
            cjData.ParentFolder = parentFolder;
            cjData.Folders = new Dictionary<string, CjFolderEntry>();

            foreach (var path in folderPaths)
            {
                if (!Directory.Exists(path)) continue;
                string name = Path.GetFileName(path);
                int rating = RatingService.GetCachedRating(path);
                cjData.Folders[path] = new CjFolderEntry
                {
                    FolderName = name,
                    Rating = rating,
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }

            // 親フォルダ内に collection_*.json を作成
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"collection_{timestamp}.json";
            string cjPath = Path.Combine(parentFolder, fileName);

            FolderService.SaveCj(parentFolder, cjData);

            return (cjPath, cjData);
        }

        /// 指定された JSON ファイルを CjRoot として読み込む。
        /// StartupHandler など、外部から直接ファイル指定してロードしたい場合に使う。
        public static CjRoot? LoadCjFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var cjRoot = new CjRoot();

                if (doc.RootElement.TryGetProperty("parentFolder", out var pf))
                    cjRoot.ParentFolder = pf.GetString() ?? "";

                var folders = new Dictionary<string, CjFolderEntry>();
                if (doc.RootElement.TryGetProperty("folders", out var fj) && fj.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in fj.EnumerateObject())
                    {
                        var cjEntry = new CjFolderEntry();
                        if (entry.Value.TryGetProperty("folderName", out var fn))
                            cjEntry.FolderName = fn.GetString() ?? "";
                        if (entry.Value.TryGetProperty("CbzZipCount", out var cz) && cz.ValueKind == JsonValueKind.Number)
                            cjEntry.CbzZipCount = cz.GetInt32();
                        if (entry.Value.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                            cjEntry.ImageCount = ic.GetInt32();
                        if (entry.Value.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number)
                            cjEntry.Rating = r.GetInt32();
                        if (entry.Value.TryGetProperty("updatedAt", out var ua) && ua.ValueKind == JsonValueKind.Number)
                            cjEntry.UpdatedAt = ua.GetInt64();

                        folders[entry.Name] = cjEntry;
                    }
                }

                cjRoot.Folders = folders;

                return cjRoot.Folders.Count > 0 ? cjRoot : null;
            }
            catch
            {
                return null;
            }
        }

        // 親フォルダ配下のサブフォルダをスキャンしてCJを作成・保存し、結果を返す。
        // UI直結は行わず、データ構築＋ファイル操作のみを担当（Form1から非UIスレッドで呼ばれる）。
        public static (string cjPath, CjRoot data) CreateForParent(string parentFolder)
        {
            if (!Directory.Exists(parentFolder))
                throw new DirectoryNotFoundException($"Parent folder not found: {parentFolder}");

            var folders = new Dictionary<string, CjFolderEntry>();

            foreach (string dir in Directory.GetDirectories(parentFolder, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    int imageCount = 0;
                    int cbzZipCount = 0;

                    var files = Directory.GetFiles(dir, "*");
                    foreach (var f in files)
                    {
                        string ext = Path.GetExtension(f)?.ToLowerInvariant();
                        if (ext == ".cbz" || ext == ".zip")
                            cbzZipCount++;
                        else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp")
                            imageCount++;
                    }

                    int rating = RatingService.ReadRating(dir);

                    folders[dir] = new CjFolderEntry
                    {
                        FolderName = Path.GetFileName(dir),
                        ImageCount = imageCount,
                        CbzZipCount = cbzZipCount,
                        Rating = rating,
                        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                }
                catch { /* フォルダ読み取りエラーはスキップ */ }
            }

            var cjData = new CjRoot
            {
                ParentFolder = parentFolder,
                Folders = folders
            };

            string cjPath = AppPaths.GetCacheFilePath(parentFolder);
            FolderService.SaveCj(parentFolder, cjData);

            return (cjPath, cjData);
        }
    }
}
