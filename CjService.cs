using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MangaViewer
{
    /// <summary>
    /// CJ（Collection JSON）の読み込み・作成・同期を管理する。
    /// </summary>
    public static class CjService
    {
        private static readonly object SyncLock = new();

        public static (string cjFile, CjRoot data)? FindAndLoadCj(string parentFolder)
        {
            if (!Directory.Exists(parentFolder)) return null;

            var merged = TryLoadMergedCj(parentFolder);
            if (merged.HasValue)
                return merged;

            // 互換: 親フォルダ直下の collection_*.json をフォールバック読込
            var candidates = Directory.GetFiles(parentFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(p => p.Contains("collection_", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var file in candidates)
            {
                var fallback = LoadCjFromFile(file);
                if (fallback == null || fallback.Folders.Count == 0)
                    continue;

                SaveCjSynced(parentFolder, fallback);
                return (AppPaths.GetCacheFilePath(parentFolder), fallback);
            }

            return null;
        }

        public static (string cjFile, CjRoot data) CreateCj(string parentFolder, IEnumerable<string> folderPaths)
        {
            if (!Directory.Exists(parentFolder))
                throw new DirectoryNotFoundException($"Parent folder not found: {parentFolder}");

            var cjData = new CjRoot
            {
                ParentFolder = parentFolder,
                Folders = new Dictionary<string, CjFolderEntry>()
            };

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

            SaveCjSynced(parentFolder, cjData);
            return (AppPaths.GetCacheFilePath(parentFolder), cjData);
        }

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
                        string ext = Path.GetExtension(f)?.ToLowerInvariant() ?? "";
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
                catch
                {
                    // フォルダ読み取りエラーはスキップ
                }
            }

            var cjData = new CjRoot
            {
                ParentFolder = parentFolder,
                Folders = folders
            };

            SaveCjSynced(parentFolder, cjData);
            return (AppPaths.GetCacheFilePath(parentFolder), cjData);
        }

        public static CjRoot? LoadCjFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = new CjRoot();

                if (doc.RootElement.TryGetProperty("parentFolder", out var pf))
                    root.ParentFolder = pf.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("cjVersion", out var sv) && sv.ValueKind == JsonValueKind.Number)
                    root.SharedVersion = sv.GetInt64();
                if (doc.RootElement.TryGetProperty("localVersion", out var lv) && lv.ValueKind == JsonValueKind.Number)
                    root.LocalVersion = lv.GetInt64();
                if (doc.RootElement.TryGetProperty("lastWriter", out var lw) && lw.ValueKind == JsonValueKind.String)
                    root.LastWriter = lw.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("savedAtUtcMs", out var sa) && sa.ValueKind == JsonValueKind.Number)
                    root.SavedAtUtcMs = sa.GetInt64();

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

                root.Folders = folders;
                return root;
            }
            catch
            {
                return null;
            }
        }

        public static void SaveCjSynced(string parentFolder, CjRoot cjRoot)
        {
            lock (SyncLock)
            {
                cjRoot.ParentFolder = parentFolder;
                string writerId = GetOrCreateWriterId();
                string localPath = AppPaths.GetCacheFilePath(parentFolder);
                string sharedPath = GetSharedCacheFilePath(parentFolder);

                var localExisting = LoadCjFromFile(localPath);
                var sharedExisting = TryLoadShared(sharedPath, out bool sharedReadable);

                long baseShared = Math.Max(cjRoot.SharedVersion, Math.Max(localExisting?.SharedVersion ?? 0, sharedExisting?.SharedVersion ?? 0));
                cjRoot.SavedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                cjRoot.LastWriter = writerId;

                if (sharedReadable)
                {
                    cjRoot.SharedVersion = Math.Max(1, baseShared + 1);
                    cjRoot.LocalVersion = 0;
                }
                else
                {
                    long baseLocal = Math.Max(cjRoot.LocalVersion, localExisting?.LocalVersion ?? 0);
                    cjRoot.LocalVersion = Math.Max(1, baseLocal + 1);
                    if (cjRoot.SharedVersion <= 0)
                        cjRoot.SharedVersion = Math.Max(1, baseShared);
                }

                EnsureVersionDefaults(cjRoot);

                bool localOk = TryWriteAtomic(localPath, cjRoot, makeConflictCopy: false);
                bool sharedOk = sharedReadable && TryWriteAtomic(sharedPath, cjRoot, makeConflictCopy: false);

                StartupHandler.WriteStartupLog($"[CJ-SYNC] write local={(localOk ? "ok" : "ng")} shared={(sharedOk ? "ok" : "ng")} sharedVer={cjRoot.SharedVersion} localVer={cjRoot.LocalVersion}");
            }
        }

        public static CjRoot? LoadBestCjForParent(string parentFolder)
        {
            return TryLoadMergedCj(parentFolder)?.data;
        }

        private static (string cjFile, CjRoot data)? TryLoadMergedCj(string parentFolder)
        {
            string localPath = AppPaths.GetCacheFilePath(parentFolder);
            string sharedPath = GetSharedCacheFilePath(parentFolder);

            var local = LoadCjFromFile(localPath);
            var shared = TryLoadShared(sharedPath, out bool sharedReadable);

            StartupHandler.WriteStartupLog($"[CJ-SYNC] read local={(local != null ? "ok" : "ng")} shared={(shared != null ? "ok" : "ng")}");

            if (local == null && shared == null)
                return null;

            if (local != null) EnsureVersionDefaults(local);
            if (shared != null) EnsureVersionDefaults(shared);

            var winner = SelectWinner(local, shared);
            if (winner == null)
                return null;

            var selected = winner.Value;

            if (local == null && selected.Source == "shared")
                TryWriteAtomic(localPath, selected.Data, makeConflictCopy: false);
            else if (shared == null && sharedReadable && selected.Source == "local")
                TryWriteAtomic(sharedPath, selected.Data, makeConflictCopy: false);
            else if (local != null && shared != null && selected.Source == "local" && sharedReadable)
                TryRepair(sharedPath, shared, selected.Data);
            else if (local != null && shared != null && selected.Source == "shared")
                TryRepair(localPath, local, selected.Data);

            StartupHandler.WriteStartupLog($"[CJ-SYNC] winner={selected.Source} reason={selected.Reason} sharedVer={selected.Data.SharedVersion} localVer={selected.Data.LocalVersion}");
            return (selected.Source == "shared" ? sharedPath : localPath, selected.Data);
        }

        private static void TryRepair(string targetPath, CjRoot current, CjRoot winner)
        {
            bool sameContent = ComputeHash(current) == ComputeHash(winner);
            bool ok = TryWriteAtomic(targetPath, winner, makeConflictCopy: !sameContent);
            StartupHandler.WriteStartupLog($"[CJ-SYNC] repair target={targetPath} result={(ok ? "ok" : "ng")}");
        }

        private static (string Source, string Reason, CjRoot Data)? SelectWinner(CjRoot? local, CjRoot? shared)
        {
            if (local == null && shared == null) return null;
            if (local != null && shared == null) return ("local", "single", local);
            if (local == null && shared != null) return ("shared", "single", shared);

            int cmp = CompareVersion(local!, shared!);
            if (cmp > 0) return ("local", "version", local!);
            if (cmp < 0) return ("shared", "version", shared!);

            long localTime = local!.SavedAtUtcMs;
            long sharedTime = shared!.SavedAtUtcMs;
            if (localTime > sharedTime) return ("local", "time", local);
            if (localTime < sharedTime) return ("shared", "time", shared);

            int writerCmp = string.Compare(local.LastWriter ?? string.Empty, shared.LastWriter ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            return writerCmp >= 0 ? ("local", "writer", local) : ("shared", "writer", shared);
        }

        private static int CompareVersion(CjRoot a, CjRoot b)
        {
            if (a.SharedVersion != b.SharedVersion)
                return a.SharedVersion.CompareTo(b.SharedVersion);
            return a.LocalVersion.CompareTo(b.LocalVersion);
        }

        private static CjRoot? TryLoadShared(string sharedPath, out bool sharedReadable)
        {
            sharedReadable = false;
            try
            {
                string? dir = Path.GetDirectoryName(sharedPath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    return null;
                sharedReadable = true;
                return LoadCjFromFile(sharedPath);
            }
            catch
            {
                return null;
            }
        }

        private static string GetSharedCacheFilePath(string parentFolder)
        {
            string localPath = AppPaths.GetCacheFilePath(parentFolder);
            string fileName = Path.GetFileName(localPath);
            return Path.Combine(AppPaths.SharedCacheDir, fileName);
        }

        private static string GetOrCreateWriterId()
        {
            try
            {
                string path = AppPaths.WriterIdFilePath;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(path))
                {
                    string cached = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(cached))
                        return cached;
                }

                string created = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];
                File.WriteAllText(path, created);
                return created;
            }
            catch
            {
                return Environment.MachineName;
            }
        }

        private static void EnsureVersionDefaults(CjRoot data)
        {
            if (data.SharedVersion <= 0) data.SharedVersion = 1;
            if (data.LocalVersion < 0) data.LocalVersion = 0;
            if (data.SavedAtUtcMs <= 0) data.SavedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (string.IsNullOrWhiteSpace(data.LastWriter)) data.LastWriter = Environment.MachineName;
        }

        private static string ComputeHash(CjRoot data)
        {
            var canonical = new StringBuilder();
            canonical.Append(data.ParentFolder).Append('|');
            foreach (var kv in data.Folders.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                canonical.Append(kv.Key).Append('|')
                    .Append(kv.Value.FolderName).Append('|')
                    .Append(kv.Value.CbzZipCount?.ToString() ?? "").Append('|')
                    .Append(kv.Value.ImageCount?.ToString() ?? "").Append('|')
                    .Append(kv.Value.Rating).Append('|')
                    .Append(kv.Value.UpdatedAt).Append(';');
            }

            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        private static bool TryWriteAtomic(string path, CjRoot data, bool makeConflictCopy)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir))
                    return false;

                Directory.CreateDirectory(dir);

                string tmpPath = path + ".tmp";
                string bakPath = path + ".bak";
                string conflictPath = path + ".conflict";

                string json = BuildCjJson(data);
                File.WriteAllText(tmpPath, json);

                if (File.Exists(path))
                {
                    if (makeConflictCopy)
                    {
                        try { File.Copy(path, conflictPath, overwrite: true); } catch { }
                    }

                    try
                    {
                        if (File.Exists(bakPath)) File.Delete(bakPath);
                        File.Move(path, bakPath);
                    }
                    catch
                    {
                        // bak化できない場合でも上書きを試す
                    }
                }

                File.Move(tmpPath, path, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildCjJson(CjRoot data)
        {
            var obj = new Dictionary<string, object>
            {
                ["parentFolder"] = data.ParentFolder,
                ["cjVersion"] = data.SharedVersion,
                ["localVersion"] = data.LocalVersion,
                ["lastWriter"] = data.LastWriter,
                ["savedAtUtcMs"] = data.SavedAtUtcMs,
                ["folders"] = new Dictionary<object, object>()
            };

            foreach (var kvp in data.Folders)
            {
                var entryObj = new Dictionary<object, object>
                {
                    ["folderName"] = kvp.Value.FolderName,
                    ["rating"] = kvp.Value.Rating,
                    ["updatedAt"] = kvp.Value.UpdatedAt
                };

                if (kvp.Value.CbzZipCount.HasValue && kvp.Value.CbzZipCount.Value > 0)
                    entryObj["CbzZipCount"] = kvp.Value.CbzZipCount.Value;
                if (kvp.Value.ImageCount.HasValue && kvp.Value.ImageCount.Value > 0)
                    entryObj["imageCount"] = kvp.Value.ImageCount.Value;

                ((Dictionary<object, object>)obj["folders"])[kvp.Key] = entryObj;
            }

            return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
