using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MangaViewer
{
    /// <summary>
    /// CJ（アプリ缓存JSON）の管理业务逻辑。
    /// 负责CRUD操作、timestamp比较、duplicate prevention。
    /// </summary>
    public static class CjManager
    {
        /// <summary>
        /// CJファイル的full path for specified parent folder.
        /// </summary>
        private static string GetCjFilePath(string parentFolder)
        {
            return AppPaths.GetCacheFilePath(parentFolder);
        }

        /// <summary>
        /// CJ's existence check.
        /// </summary>
        public static bool CjExists(string parentFolder)
        {
            if (string.IsNullOrWhiteSpace(parentFolder)) return false;
            return File.Exists(GetCjFilePath(parentFolder));
        }

        /// <summary>
        /// Load CJ. Return null if not exists or corrupted.
        /// </summary>
        public static CjRoot? LoadCj(string parentFolder)
        {
            string path = GetCjFilePath(parentFolder);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = new CjRoot();

                if (doc.RootElement.TryGetProperty("parentFolder", out var pf))
                    root.ParentFolder = pf.GetString() ?? "";

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
            catch { return null; }
        }

        /// <summary>
        /// Save CJ to disk.
        /// </summary>
        public static void SaveCj(string parentFolder, CjRoot cjRoot)
        {
            if (string.IsNullOrWhiteSpace(parentFolder)) throw new ArgumentNullException(nameof(parentFolder));
            
            try
            {
                string dir = AppPaths.CacheDir;
                Directory.CreateDirectory(dir);

                var obj = new Dictionary<string, object>
                {
                    ["parentFolder"] = parentFolder,
                    ["folders"] = new Dictionary<object, object>()
                };

                foreach (var kvp in cjRoot.Folders)
                {
                    var entryObj = new Dictionary<object, object>
                    {
                        ["folderName"] = kvp.Value.FolderName
                    };

                    if (kvp.Value.CbzZipCount.HasValue && kvp.Value.CbzZipCount.Value > 0)
                        entryObj["CbzZipCount"] = kvp.Value.CbzZipCount.Value;
                    
                    if (kvp.Value.ImageCount.HasValue && kvp.Value.ImageCount.Value > 0)
                        entryObj["imageCount"] = kvp.Value.ImageCount.Value;
                    
                    entryObj["rating"] = kvp.Value.Rating;
                    entryObj["updatedAt"] = kvp.Value.UpdatedAt;

                    ((Dictionary<object, object>)obj["folders"])[kvp.Key] = entryObj;
                }

                string path = GetCjFilePath(parentFolder);
                File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (UnauthorizedAccessException) { throw; } // Re-throw auth exceptions so caller can handle them
            catch { /* Other errors are silently ignored */ }
        }

        /// <summary>
        /// Generate CjRoot with folder entry populated from DJ data.
        /// This is used when creating a new CJ or updating an existing one based on DJ changes.
        /// </summary>
        public static void UpdateFolderEntryInCj(
            string parentFolder,
            string folderPath,
            int cbzZipCount,
            int imageCount,
            int rating,
            long updatedAt)
        {
            CjRoot? cjRoot = LoadCj(parentFolder);
            if (cjRoot == null)
                return;

            var entry = new CjFolderEntry
            {
                FolderName = Path.GetFileName(folderPath),
                Rating = rating,
                UpdatedAt = updatedAt
            };

            // 根據file type set imageCount or cbzZipCount (only one should be non-null)
            if (cbzZipCount > 0)
            {
                entry.CbzZipCount = cbzZipCount;
                entry.ImageCount = null;
            }
            else
            {
                entry.ImageCount = imageCount;
                entry.CbzZipCount = null;
            }

            cjRoot.Folders[folderPath] = entry;
            SaveCj(parentFolder, cjRoot);
        }

        /// <summary>
        /// Perform the core CJ update logic based on timestamp comparison.
        /// Returns true if the DJ is newer than the CJ and has been updated.
        /// </summary>
        public static bool TryUpdateCjFromNewerDj(
            string parentFolder,
            string folderPath)
        {
            CjRoot? cjRoot = LoadCj(parentFolder);
            if (cjRoot == null || !cjRoot.Folders.TryGetValue(folderPath, out var cjEntry))
                return false;

            string jsonPath = Path.Combine(Path.GetDirectoryName(folderPath), $"{Path.GetFileName(folderPath)}.json");
            if (!File.Exists(jsonPath))
                return false; // DJ does not exist - skip per spec rule 2.2

            DateTime djWriteTime = File.GetLastWriteTime(jsonPath);
            long djTicksMs = new DateTimeOffset(djWriteTime).ToUnixTimeMilliseconds();

            if (djTicksMs <= cjEntry.UpdatedAt)
                return false; // DJ is not newer

            // DJ is newer - update the CJ entry from the DJ
            var (imageCount, cbzFileCount, rating) = FolderService.ReadFolderJson(jsonPath);
            
            if (cbzFileCount > 0)
            {
                UpdateFolderEntryInCj(
                    parentFolder, 
                    folderPath, 
                    cbzZipCount: cbzFileCount, 
                    imageCount: 0, 
                    rating: rating, 
                    updatedAt: djTicksMs);
            }
            else
            {
                UpdateFolderEntryInCj(
                    parentFolder, 
                    folderPath, 
                    cbzZipCount: 0, 
                    imageCount: imageCount, 
                    rating: rating, 
                    updatedAt: djTicksMs);
            }

            return true;
        }

        /// <summary>
        /// Create a new CJ entry from DJ data for a folder that doesn't have a CJ entry yet.
        /// </summary>
        public static void CreateCjEntryFromDj(
            string parentFolder,
            string folderPath)
        {
            try
            {
                string jsonPath = Path.Combine(Path.GetDirectoryName(folderPath), $"{Path.GetFileName(folderPath)}.json");
                
                if (!File.Exists(jsonPath))
                    return; // No DJ exists - skip per spec rule 1.2 requires actual scan

                var (imageCount, cbzFileCount, rating) = FolderService.ReadFolderJson(jsonPath);
                
                long updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                UpdateFolderEntryInCj(
                    parentFolder, 
                    folderPath, 
                    cbzZipCount: cbzFileCount, 
                    imageCount: imageCount, 
                    rating: rating, 
                    updatedAt: updatedAt);
            }
            catch { /* Creation failure is silently ignored */ }
        }

        /// <summary>
        /// Scan a folder and create a CJ entry for it.
        /// Used when DJ does not exist (spec rule 1.2).
        /// </summary>
        public static void CreateCjEntryFromScan(
            string parentFolder,
            string folderPath)
        {
            try
            {
                int scanImageCount = 0;
                int scanCbzCount = FolderService.CountCbzFiles(folderPath);
                
                if (scanCbzCount > 0)
                    scanImageCount = FolderService.CountImagesWithoutCbzFallback(folderPath);

                long updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                UpdateFolderEntryInCj(
                    parentFolder, 
                    folderPath, 
                    cbzZipCount: scanCbzCount, 
                    imageCount: scanImageCount, 
                    rating: -1, // No DJ so rating is unset
                    updatedAt: updatedAt);
            }
            catch { /* Scan failure is silently ignored */ }
        }

        /// <summary>
        /// Get folder display data from a CJ (without re-scanning).
        /// Used for fast load mode when only local cache should be used.
        /// </summary>
        public static List<FolderEntry> GetFoldersFromCj(string parentFolder)
        {
            var entries = new List<FolderEntry>();
            
            CjRoot? cjRoot = LoadCj(parentFolder);
            if (cjRoot == null) return entries;

            foreach (var kvp in cjRoot.Folders)
            {
                var entry = kvp.Value;
                
                int imageCount = entry.ImageCount ?? 0;
                int cbzFileCount = entry.CbzZipCount ?? 0;
                int rating = entry.Rating;

                // Apply DBList-specific filter logic (separate from folder-list filters)
                bool inRange = true;
                Settings settings = SettingsManager.Load();

                if (settings.DbMinDisplayCount > 0 && imageCount < settings.DbMinDisplayCount)
                    inRange = false;
                if (settings.DbMaxDisplayCount > 0 && imageCount > settings.DbMaxDisplayCount)
                    inRange = false;

                bool imageConditionOk = inRange || (cbzFileCount >= 1);

                // Rating filter using DBList-specific MinEvaluation
                if (!imageConditionOk) continue;

                if (settings.DbMinEvaluation > 0 && rating >= 0 && rating < settings.DbMinEvaluation)
                    continue;

                entries.Add(new FolderEntry { Path = kvp.Key, ImageCount = imageCount, CbzFileCount = cbzFileCount, Rating = rating });
            }

            return entries;
        }
    }
}