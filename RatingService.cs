using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace MangaViewer
{
    public class RatingService
    {
        // READ ONLY フォルダ向けの評価キャッシュ（メモリ内）
        private static readonly ConcurrentDictionary<string, FolderMeta> ReadOnlyCache = new();
        // IsFolderReadOnly の結果をキャッシュ（同じフォルダの重複テストを避ける）
        private static readonly ConcurrentDictionary<string, bool> _readOnlyCheckCache = new();

        // ratings_cache.json への書き込み用デバウンス
        private static Timer? _flushTimer;
        private static readonly object FlushLock = new();
        private const int FlushDelayMs = 800; // 更新後一定時間経過で保存（複数回の操作をまとめる）

        private static string NormalizeFolderKey(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return string.Empty;
            var trimmed = Path.TrimEndingDirectorySeparator(folderPath.Trim());
            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch
            {
                return trimmed;
            }
        }

        public static void LoadReadOnlyCache()
        {
            try
            {
                string primaryPath = AppPaths.RatingsCacheFilePath;
                string legacyPath = AppPaths.LegacyRatingsCacheFilePath;

                string pathToLoad = File.Exists(primaryPath) ? primaryPath : legacyPath;
                if (!File.Exists(pathToLoad))
                    return;

                using var doc = JsonDocument.Parse(File.ReadAllText(pathToLoad));
                if (!doc.RootElement.TryGetProperty("folders", out var foldersElem))
                    return;

                int loaded = 0;
                foreach (var kv in foldersElem.EnumerateObject())
                {
                    var folderPath = NormalizeFolderKey(kv.Name);
                    if (string.IsNullOrEmpty(folderPath))
                        continue;
                    var value = kv.Value;

                    int rating = -1;
                    if (value.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number)
                        rating = r.GetInt32();

                    string folderName = "";
                    if (value.TryGetProperty("folderName", out var fn) && fn.ValueKind == JsonValueKind.String)
                        folderName = fn.GetString() ?? "";

                    int imageCount = 0;
                    if (value.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                        imageCount = ic.GetInt32();

                    ReadOnlyCache[folderPath] = new FolderMeta
                    {
                        Rating = rating,
                        ImageCount = imageCount,
                        FolderName = folderName
                    };
                    loaded++;
                }

                // 旧配置の bin 配下 JSON が存在していた場合だけ、正本ファイルへ移行して次回からは正本を読む。
                if (File.Exists(legacyPath) && !File.Exists(primaryPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(primaryPath)!);
                    File.Copy(legacyPath, primaryPath, overwrite: true);
                    StartupHandler.WriteStartupLog($"[RATING] Migrated legacy cache from={legacyPath} to={primaryPath}");
                }

                StartupHandler.WriteStartupLog($"[RATING] LoadReadOnlyCache path={pathToLoad} count={loaded}");
            }
            catch (Exception ex)
            {
                StartupHandler.WriteErrorLog($"[RATING] LoadReadOnlyCache failed path={AppPaths.RatingsCacheFilePath} error={ex}");
            }
        }

        /// <summary>
        /// 指定パスが ReadOnlyCache に登録されているか判定。
        /// </summary>
        public static bool IsReadOnlyCached(string folderPath)
        {
            var key = NormalizeFolderKey(folderPath);
            return !string.IsNullOrEmpty(key) && ReadOnlyCache.ContainsKey(key);
        }

        /// <summary>
        /// キャッシュから指定フォルダの評価値を取得（-1: 未設定/キャッシュなし）
        /// </summary>
        public static int GetCachedRating(string folderPath)
        {
            var key = NormalizeFolderKey(folderPath);
            if (!string.IsNullOrEmpty(key) && ReadOnlyCache.TryGetValue(key, out var meta))
                return meta.Rating;
            return -1;
        }

        /// <summary>
        /// 指定フォルダの評価値をキャッシュに直接設定（外部から呼び出し）。
        /// </summary>
        public static void SetReadOnlyCacheRating(string folderPath, int rating, int imageCount = 0, string? folderName = null)
        {
            string key = NormalizeFolderKey(folderPath);
            if (string.IsNullOrEmpty(key)) return;
            var meta = ReadOnlyCache.GetOrAdd(key, _ => new FolderMeta());
            meta.Rating = rating;
            if (imageCount > 0) meta.ImageCount = imageCount;
            if (!string.IsNullOrEmpty(folderName)) meta.FolderName = folderName;
        }

        public static void FlushReadOnlyCache()
        {
            try
            {
                lock (FlushLock)
                {
                    _flushTimer?.Dispose();
                    _flushTimer = null;
                }

                var folders = new Dictionary<string, object>();
                foreach (var kv in ReadOnlyCache)
                {
                    var obj = new Dictionary<string, object>
                    {
                        ["rating"] = kv.Value.Rating,
                        ["imageCount"] = kv.Value.ImageCount
                    };
                    if (!string.IsNullOrEmpty(kv.Value.FolderName))
                        obj["folderName"] = kv.Value.FolderName;

                    folders[kv.Key] = obj;
                }

                var root = new Dictionary<string, object> { ["folders"] = folders };
                var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });

                string directory = Path.GetDirectoryName(AppPaths.RatingsCacheFilePath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(AppPaths.RatingsCacheFilePath, json);
                StartupHandler.WriteStartupLog($"[RATING] FlushReadOnlyCache path={AppPaths.RatingsCacheFilePath} count={folders.Count}");
            }
            catch (System.UnauthorizedAccessException ex)
            {
                StartupHandler.WriteErrorLog($"[RATING] FlushReadOnlyCache access denied path={AppPaths.RatingsCacheFilePath} error={ex}");
            }
            catch (Exception ex)
            {
                StartupHandler.WriteErrorLog($"[RATING] FlushReadOnlyCache failed path={AppPaths.RatingsCacheFilePath} error={ex}");
            }
        }

        public static void SaveRating(string folderPath, int rating)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            var trimmed = Path.TrimEndingDirectorySeparator(folderPath);
            string cacheKey = NormalizeFolderKey(trimmed);
            if (string.IsNullOrEmpty(cacheKey))
                return;

            string folderName = Path.GetFileName(trimmed);
            string localJsonPath = Path.Combine(trimmed, $"{folderName}.json");
            bool isReadOnly = FolderService.IsReadOnlyFolder(trimmed);

            int knownImageCount = 0;
            try
            {
                if (File.Exists(localJsonPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(localJsonPath));
                    if (doc.RootElement.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                        knownImageCount = ic.GetInt32();
                }
            }
            catch { }

            // Always mirror to internal cache json so restart can restore ratings consistently.
            SetReadOnlyCacheRating(cacheKey, rating, knownImageCount, folderName);
            ScheduleFlush();
            SyncRatingToActiveCjCache(trimmed, rating);

            StartupHandler.WriteStartupLog($"[RATING] SaveRating folder={trimmed} rating={rating} localJson={localJsonPath} isReadOnly={isReadOnly} cacheKey={cacheKey}");

            // READ ONLY フォルダの場合はローカルJSONではなくグローバルキャッシュのみに保持する。
            if (isReadOnly)
            {
                var meta = ReadOnlyCache.GetOrAdd(cacheKey, _ => new FolderMeta());
                meta.Rating = rating;
                meta.FolderName = folderName;

                try
                {
                    if (File.Exists(localJsonPath))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(localJsonPath));
                        if (doc.RootElement.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                            meta.ImageCount = ic.GetInt32();
                    }
                }
                catch { }

                StartupHandler.WriteStartupLog($"[RATING] SaveRating stored-only-in-cache folder={trimmed} rating={rating}");
                return;
            }

            // 1. ローカルJSONへの保存を試みる（既存動作）
            try
            {
                var (imageCount, _) = ReadFolderJson(localJsonPath);
                var obj = new Dictionary<string, object> { ["rating"] = rating };
                if (imageCount > 0)
                    obj["imageCount"] = imageCount;

                File.WriteAllText(localJsonPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
                StartupHandler.WriteStartupLog($"[RATING] SaveRating wrote-local-json folder={trimmed} path={localJsonPath} rating={rating}");
                return;
            }
            catch (IOException ex)
            {
                StartupHandler.WriteErrorLog($"[RATING] SaveRating local-json IO error folder={trimmed} path={localJsonPath} error={ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                StartupHandler.WriteErrorLog($"[RATING] SaveRating local-json access denied folder={trimmed} path={localJsonPath} error={ex}");
            }

            // 2. ローカル保存失敗（READ ONLY と見なす）→ メモリキャッシュを更新
            var cachedMeta = ReadOnlyCache.GetOrAdd(cacheKey, _ => new FolderMeta());
            cachedMeta.Rating = rating;
            cachedMeta.FolderName = folderName;

            try
            {
                if (File.Exists(localJsonPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(localJsonPath));
                    if (doc.RootElement.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                        cachedMeta.ImageCount = ic.GetInt32();
                }
            }
            catch
            {
                // imageCount 読み込み失敗は無視
            }

            StartupHandler.WriteStartupLog($"[RATING] SaveRating fallback-to-cache folder={trimmed} rating={rating}");
        }

        private static void SyncRatingToActiveCjCache(string folderPath, int rating)
        {
            try
            {
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MangaViewer",
                    "cache");

                if (!Directory.Exists(cacheDir))
                    return;

                string normalizedFolderPath = NormalizeFolderKey(folderPath);
                foreach (var file in Directory.GetFiles(cacheDir, "ratings_cache_*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        using var doc = JsonDocument.Parse(json);
                        if (!doc.RootElement.TryGetProperty("folders", out var foldersElem) || foldersElem.ValueKind != JsonValueKind.Object)
                            continue;

                        bool matched = false;
                        foreach (var entry in foldersElem.EnumerateObject())
                        {
                            string key = NormalizeFolderKey(entry.Name);
                            if (string.Equals(key, normalizedFolderPath, StringComparison.OrdinalIgnoreCase))
                            {
                                var root = JsonNode.Parse(json)!;
                                var folderNode = root["folders"]![entry.Name]!;
                                folderNode["rating"] = rating;
                                File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                                StartupHandler.WriteStartupLog($"[RATING] Synced CJ cache rating file={Path.GetFileName(file)} folder={folderPath} rating={rating}");
                                matched = true;
                                break;
                            }
                        }

                        if (matched)
                            break;
                    }
                    catch (Exception ex)
                    {
                        StartupHandler.WriteErrorLog($"[RATING] SyncRatingToActiveCjCache failed file={file} error={ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                StartupHandler.WriteErrorLog($"[RATING] SyncRatingToActiveCjCache failed folder={folderPath} error={ex}");
            }
        }

        public static int ReadRating(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return -1;

            var trimmed = Path.TrimEndingDirectorySeparator(folderPath);
            string folderName = Path.GetFileName(trimmed);
            string localJsonPath = Path.Combine(trimmed, $"{folderName}.json");

            // READ ONLY フォルダかどうかを判定：
            // キャッシュに存在し、かつローカルJSONが書き込み不可なら「READ ONLY」と見なしてキャッシュ優先。
            string cacheKey = NormalizeFolderKey(trimmed);
            FolderMeta? cacheMeta = null;
            bool isInCache = false;
            if (!string.IsNullOrEmpty(cacheKey))
                isInCache = ReadOnlyCache.TryGetValue(cacheKey, out cacheMeta);

            bool readOnly = IsFolderReadOnly(trimmed);
            int cachedRatingForLog = isInCache && cacheMeta != null ? cacheMeta.Rating : -999;
            StartupHandler.WriteStartupLog($"[RATING] ReadRating folder={trimmed} readOnly={readOnly} inCache={isInCache} cachedRating={cachedRatingForLog} localPath={localJsonPath}");

            if (readOnly)
            {
                // READ ONLY フォルダ → ratings_cache.json の値を常に優先
                if (isInCache && cacheMeta != null && cacheMeta.Rating != -1)
                    return cacheMeta.Rating;

                // キャッシュに評価がない場合のみ、ローカルJSON を参照
                if (File.Exists(localJsonPath))
                {
                    var (_, rating) = ReadFolderJson(localJsonPath);
                    if (rating != -1)
                        return rating;
                }

                return isInCache && cacheMeta != null ? cacheMeta.Rating : -1;
            }
            else
            {
                // READ NOT ONLY フォルダ → ローカルJSONを優先
                var (_, localRating) = ReadFolderJson(localJsonPath);
                if (localRating != -1)
                    return localRating;

                // ローカルにない場合、キャッシュがあれば使う（補助）
                if (isInCache && cacheMeta != null && cacheMeta.Rating != -1)
                    return cacheMeta.Rating;

                return -1;
            }
        }

        public static void UpdateImageCountIfReadOnly(string folderPath, int imageCount)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            // ReadOnlyCache に登録されているフォルダかつ READ ONLY の場合、imageCount を更新
            string cacheKey = NormalizeFolderKey(folderPath);
            if (string.IsNullOrEmpty(cacheKey))
                return;

            if (!ReadOnlyCache.TryGetValue(cacheKey, out var meta))
                return;

            if (!IsFolderReadOnly(Path.TrimEndingDirectorySeparator(folderPath)))
                return;

            meta.ImageCount = imageCount;
            ScheduleFlush();
        }

        public static bool IsFolderMatchedByRatingOneKeywords(string folderPath, string ratingOneKeywords)
        {
            if (string.IsNullOrWhiteSpace(ratingOneKeywords))
                return false;

            var trimmed = folderPath?.TrimEnd('/', '\\');
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            string? folderName = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(folderName))
                return false;

            var keywords = ratingOneKeywords.Split(',');
            foreach (var k in keywords)
            {
                var kw = k.Trim();
                if (!string.IsNullOrEmpty(kw) &&
                    folderName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ローカルJSONから imageCount と rating を読む（既存ロジック相当）
        private static (int imageCount, int rating) ReadFolderJson(string jsonPath)
        {
            int imageCount = 0;
            int rating = -1;

            if (!File.Exists(jsonPath))
                return (imageCount, rating);

            try
            {
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                    imageCount = ic.GetInt32();
                if (doc.RootElement.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number)
                    rating = r.GetInt32();
            }
            catch { }

            return (imageCount, rating);
        }

        // フォルダが READ ONLY か判定（書き込み試行ベース、結果をキャッシュ）
        private static bool IsFolderReadOnly(string folderPath)
        {
            var normalized = Path.GetFullPath(Path.TrimEndingDirectorySeparator(folderPath));
            return _readOnlyCheckCache.GetOrAdd(normalized, path =>
            {
                string testFile = Path.Combine(path, ".write_test_tmp");
                try
                {
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    return false; // 書き込み成功 → READ NOT ONLY
                }
                catch
                {
                    // 書き込み不可は期待される動作（ReadOnlyフォルダの正常判定）なのでログを抑制
                    try
                    {
                        if (File.Exists(testFile))
                            File.Delete(testFile);
                    }
                    catch { }

                    return true; // 失敗 → READ ONLY と見なす
                }
            });
        }

        /// <summary>
        /// 指定された評価値と一致するフォルダパスのリストを返す（ダミー実装）。
        /// </summary>
        public static List<string> GetFoldersByRating(int rating)
        {
            var result = new List<string>();
            foreach (var kv in ReadOnlyCache)
            {
                if (kv.Value.Rating == rating)
                    result.Add(kv.Key);
            }
            return result;
        }

        private static void ScheduleFlush()
        {
            lock (FlushLock)
            {
                _flushTimer?.Dispose();
                _flushTimer = new Timer(_ => FlushReadOnlyCache(), null, FlushDelayMs, Timeout.Infinite);
            }
        }
        // メモリ内データ構造
        private class FolderMeta
        {
            public int Rating { get; set; } = -1;
            public int ImageCount { get; set; }
            public string FolderName { get; set; } = "";
            public long UpdatedAt { get; set; } = 0;
        }
    }
}
