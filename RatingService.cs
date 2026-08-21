using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        public static void LoadReadOnlyCache()
        {
            try
            {
                var path = AppPaths.RatingsCacheFilePath;
                if (!File.Exists(path))
                    return;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("folders", out var foldersElem))
                    return;

                foreach (var kv in foldersElem.EnumerateObject())
                {
                    var folderPath = kv.Name; // フォルダパスをキーとする
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
                }
            }
            catch
            {
                // キャッシュ読み込み失敗は許容（存在しない/破損など）
            }
        }

        /// <summary>
        /// 指定パスが ReadOnlyCache に登録されているか判定。
        /// </summary>
        public static bool IsReadOnlyCached(string folderPath)
        {
            return ReadOnlyCache.ContainsKey(folderPath);
        }

        /// <summary>
        /// キャッシュから指定フォルダの評価値を取得（-1: 未設定/キャッシュなし）
        /// </summary>
        public static int GetCachedRating(string folderPath)
        {
            if (ReadOnlyCache.TryGetValue(folderPath, out var meta))
                return meta.Rating;
            return -1;
        }

        /// <summary>
        /// 指定フォルダの評価値をキャッシュに直接設定（外部から呼び出し）。
        /// </summary>
        public static void SetReadOnlyCacheRating(string folderPath, int rating, int imageCount = 0, string? folderName = null)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            var meta = ReadOnlyCache.GetOrAdd(folderPath, _ => new FolderMeta());
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
                File.WriteAllText(AppPaths.RatingsCacheFilePath, json);
            }
            catch (System.UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[書き込み禁止] アクセス拒否 (FlushReadOnlyCache): {ex.Message}");
            }
            catch
            {
                // 保存失敗は許容（親ディレクトリ不可など）
            }
        }

        public static void SaveRating(string folderPath, int rating)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            var trimmed = Path.TrimEndingDirectorySeparator(folderPath);
            string folderName = Path.GetFileName(trimmed);
            string localJsonPath = Path.Combine(trimmed, $"{folderName}.json");

            // READ ONLY フォルダとして登録されている場合は直接キャッシュへ
            if (FolderService.IsReadOnlyFolder(trimmed))
            {
                var meta = ReadOnlyCache.GetOrAdd(folderPath, _ => new FolderMeta());
                meta.Rating = rating;
                if (!string.IsNullOrEmpty(meta.FolderName) && meta.FolderName == folderName)
                {
                    // 既存のFolderNameを維持
                }
                else
                {
                    meta.FolderName = folderName;
                }

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

                ScheduleFlush();
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

                // ローカル保存成功 → READ NOT ONLY とみなし、キャッシュは強制しない。
                return;
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[書き込み禁止] IOエラー (SaveRating): {ex.Message}");
                // ReadOnly/ロックなどにより失敗 → キャッシュへ
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[書き込み禁止] アクセス拒否 (SaveRating): {ex.Message}");
                // アクセス不可 → キャッシュへ
            }

            // 2. ローカル保存失敗（READ ONLY と見なす）→ メモリキャッシュを更新
            var cachedMeta = ReadOnlyCache.GetOrAdd(folderPath, _ => new FolderMeta());
            cachedMeta.Rating = rating;

            // imageCount は既存ローカルJSONから読み取れるならそれを反映
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

            if (!string.IsNullOrEmpty(cachedMeta.FolderName))
            {
                // すでに設定済みなら維持
            }
            else
            {
                cachedMeta.FolderName = folderName;
            }

            // デバウンス付き Flush（更新があった場合に一定時間後に保存）
            ScheduleFlush();
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
            bool isInCache = ReadOnlyCache.TryGetValue(folderPath, out var cacheMeta);

            if (IsFolderReadOnly(trimmed))
            {
                // READ ONLY フォルダ → ratings_cache.json の値を常に優先
                if (isInCache && cacheMeta.Rating != -1)
                    return cacheMeta.Rating;

                // キャッシュに評価がない場合のみ、ローカルJSON を参照
                if (File.Exists(localJsonPath))
                {
                    var (_, rating) = ReadFolderJson(localJsonPath);
                    if (rating != -1)
                        return rating;
                }

                return isInCache ? cacheMeta.Rating : -1;
            }
            else
            {
                // READ NOT ONLY フォルダ → ローカルJSONを優先
                var (_, localRating) = ReadFolderJson(localJsonPath);
                if (localRating != -1)
                    return localRating;

                // ローカルにない場合、キャッシュがあれば使う（補助）
                if (isInCache && cacheMeta.Rating != -1)
                    return cacheMeta.Rating;

                return -1;
            }
        }

        public static void UpdateImageCountIfReadOnly(string folderPath, int imageCount)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            // ReadOnlyCache に登録されているフォルダかつ READ ONLY の場合、imageCount を更新
            if (!ReadOnlyCache.TryGetValue(folderPath, out var meta))
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