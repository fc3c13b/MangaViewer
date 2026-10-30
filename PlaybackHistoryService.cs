using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MangaViewer
{
    /// <summary>
    /// タイトル（フォルダ）ごとの既読履歴を playback-history.json に永続化するサービス。
    /// 起動時に1回読み込み、オンメモリで管理。フォルダ離脱時にのみ書き込む。
    /// </summary>
    internal static class PlaybackHistoryService
    {
        private static readonly object _lock = new();
        private static Dictionary<string, PlaybackEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded = false;
        private static bool _dirty = false;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ===== ロード =====

        internal static void EnsureLoaded(Settings settings)
        {
            lock (_lock)
            {
                if (_loaded) return;
                _entries = Load(settings);
                _loaded = true;
            }
        }

        private static Dictionary<string, PlaybackEntry> Load(Settings settings)
        {
            var entries = new Dictionary<string, PlaybackEntry>(StringComparer.OrdinalIgnoreCase);

            // playback-history.json が存在すればそちらを使う
            string path = AppPaths.PlaybackHistoryFilePath;
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("entries", out var entriesEl)
                        && entriesEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in entriesEl.EnumerateObject())
                        {
                            var e = ParseEntry(prop.Value);
                            if (e != null)
                                entries[prop.Name] = e;
                        }
                    }
                    StartupHandler.WriteStartupLog($"[PLAYBACK] loaded {entries.Count} entries from playback-history.json");
                    return entries;
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteErrorLog($"[PLAYBACK] load failed: {ex.Message}");
                }
            }

            // ファイルが存在しない場合は setting.json から移行
            int migrated = 0;
            foreach (var kv in settings.LastViewedImageIndexByTitle)
            {
                string folder = kv.Key;
                if (string.IsNullOrWhiteSpace(folder)) continue;

                settings.LastViewedCbzFileByTitle.TryGetValue(folder, out string? cbz);
                entries[folder] = new PlaybackEntry
                {
                    LastImageIndex = Math.Max(0, kv.Value),
                    MaxReachedIndex = Math.Max(0, kv.Value),
                    LastCbzFile = cbz ?? string.Empty,
                    LastOpenedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    OpenCount = 1
                };
                migrated++;
            }

            if (migrated > 0)
                StartupHandler.WriteStartupLog($"[PLAYBACK] migrated {migrated} entries from setting.json");

            return entries;
        }

        private static PlaybackEntry? ParseEntry(JsonElement el)
        {
            var entry = new PlaybackEntry();
            if (el.TryGetProperty("lastImageIndex", out var v) && v.ValueKind == JsonValueKind.Number)
                entry.LastImageIndex = v.GetInt32();
            if (el.TryGetProperty("maxReachedIndex", out v) && v.ValueKind == JsonValueKind.Number)
                entry.MaxReachedIndex = v.GetInt32();
            bool hasVolIndex = false;
            if (el.TryGetProperty("maxReadVolIndex", out v) && v.ValueKind == JsonValueKind.Number)
            {
                entry.MaxReadVolIndex = v.GetInt32();
                hasVolIndex = true;
            }
            if (el.TryGetProperty("lastCbzFile", out v) && v.ValueKind == JsonValueKind.String)
                entry.LastCbzFile = v.GetString() ?? string.Empty;
            if (el.TryGetProperty("totalImages", out v) && v.ValueKind == JsonValueKind.Number)
                entry.TotalImages = v.GetInt32();
            if (el.TryGetProperty("isCompleted", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                entry.IsCompleted = v.GetBoolean();
            if (el.TryGetProperty("completedVols", out var volsEl) && volsEl.ValueKind == JsonValueKind.Array)
            {
                var vols = new List<string>();
                foreach (var item in volsEl.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                        vols.Add(s);
                entry.CompletedVols = vols;
            }
            if (el.TryGetProperty("lastOpenedAt", out v) && v.ValueKind == JsonValueKind.Number)
                entry.LastOpenedAt = v.GetInt64();
            if (el.TryGetProperty("openCount", out v) && v.ValueKind == JsonValueKind.Number)
                entry.OpenCount = v.GetInt32();

            // 旧データ移行: maxReadVolIndex が無ければ completedVols 件数からシード
            if (!hasVolIndex && entry.CompletedVols != null && entry.CompletedVols.Count > 0)
                entry.MaxReadVolIndex = entry.CompletedVols.Count - 1;

            return entry;
        }

        // ===== 読み取り =====

        internal static PlaybackEntry? Get(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return null;
            lock (_lock)
            {
                _entries.TryGetValue(folderPath, out var e);
                return e;
            }
        }

        // ===== 更新（ページ表示時）=====

        /// <summary>
        /// ページを表示するたびに呼び出す。maxReachedIndex を更新する。
        /// </summary>
        internal static void OnPageDisplayed(string folderPath, int displayedIndex, int displayCount, int totalImages, int activeVolIndex)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || totalImages <= 0) return;

            int reachedIndex = displayedIndex + displayCount - 1;

            lock (_lock)
            {
                if (!_entries.TryGetValue(folderPath, out var entry))
                    entry = new PlaybackEntry();

                bool changed = false;

                if (reachedIndex > entry.MaxReachedIndex)
                {
                    entry.MaxReachedIndex = reachedIndex;
                    changed = true;
                }

                if (totalImages != entry.TotalImages)
                {
                    entry.TotalImages = totalImages;
                    changed = true;
                }

                // 既読巻数の単一情報源を更新（最大値のみ保持）
                if (activeVolIndex > entry.MaxReadVolIndex)
                {
                    entry.MaxReadVolIndex = activeVolIndex;
                    changed = true;
                }

                // 全ページ到達で読了フラグをセット（一度立てたら下げない）
                if (!entry.IsCompleted && entry.MaxReachedIndex >= totalImages - 1)
                {
                    entry.IsCompleted = true;
                    changed = true;
                }

                if (changed)
                {
                    _entries[folderPath] = entry;
                    _dirty = true;
                }
            }
        }

        // ===== 更新（フォルダ離脱時）=====

        /// <summary>
        /// フォルダを離れるときに呼び出す。再開位置・CBZファイル・到達済みの巻を保存する。
        /// </summary>
        internal static void OnFolderLeave(string folderPath, int lastIndex, string lastCbzFile,
            int activeVolIndex, int totalImages)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            lock (_lock)
            {
                if (!_entries.TryGetValue(folderPath, out var entry))
                    entry = new PlaybackEntry();

                entry.LastImageIndex = Math.Max(0, lastIndex);
                entry.LastCbzFile = lastCbzFile ?? string.Empty;
                entry.LastOpenedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                entry.OpenCount = entry.OpenCount + 1;

                if (totalImages > 0)
                    entry.TotalImages = totalImages;

                // 既読巻数の単一情報源を更新（最大値のみ保持）
                if (activeVolIndex > entry.MaxReadVolIndex)
                    entry.MaxReadVolIndex = activeVolIndex;

                if (!entry.IsCompleted && entry.TotalImages > 0 && entry.MaxReachedIndex >= entry.TotalImages - 1)
                    entry.IsCompleted = true;

                _entries[folderPath] = entry;
                _dirty = true;
            }

            Save();
        }

        // ===== 保存 =====

        internal static void Save()
        {
            lock (_lock)
            {
                if (!_dirty) return;

                try
                {
                    var obj = new Dictionary<string, object>
                    {
                        { "schemaVersion", 1 },
                        { "entries", _entries }
                    };
                    string dir = Path.GetDirectoryName(AppPaths.PlaybackHistoryFilePath)!;
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(AppPaths.PlaybackHistoryFilePath,
                        JsonSerializer.Serialize(obj, _jsonOptions));
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteErrorLog($"[PLAYBACK] save failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// タイトル（フォルダ）1件分の既読履歴エントリ。
    /// </summary>
    internal class PlaybackEntry
    {
        [JsonPropertyName("lastImageIndex")]
        public int LastImageIndex { get; set; } = 0;

        [JsonPropertyName("maxReachedIndex")]
        public int MaxReachedIndex { get; set; } = 0;

        /// <summary>
        /// これまでに開いた最大の巻インデックス（0始まり）。CBZ の既読巻数の単一情報源。
        /// -1 は未読（CBZ を開いたことがない）を表す。
        /// </summary>
        [JsonPropertyName("maxReadVolIndex")]
        public int MaxReadVolIndex { get; set; } = -1;

        [JsonPropertyName("lastCbzFile")]
        public string LastCbzFile { get; set; } = string.Empty;

        [JsonPropertyName("totalImages")]
        public int TotalImages { get; set; } = 0;

        [JsonPropertyName("isCompleted")]
        public bool IsCompleted { get; set; } = false;

        [JsonPropertyName("completedVols")]
        public List<string>? CompletedVols { get; set; }

        [JsonPropertyName("lastOpenedAt")]
        public long LastOpenedAt { get; set; } = 0;

        [JsonPropertyName("openCount")]
        public int OpenCount { get; set; } = 0;
    }
}
