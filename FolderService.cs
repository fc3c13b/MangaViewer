using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MangaViewer
{
    /// <summary>
    /// フォルダリストの構築・フィルタリング・JSONキャッシュを担当するサービス。
    /// </summary>
    public class FolderService
    {
        private readonly Settings _settings;

        public FolderService(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// ルートフォルダ内のサブフォルダ一覧を取得し、フィルタリング適用
        /// </summary>
        public List<string> BuildSubfolderList(string rootPath)
        {
            var folderList = new List<string>();

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                var sorted = dirs
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 第一段階: JSONキャッシュからimageCountを一括取得
                var cacheResult = new Dictionary<string, int>();

                foreach (var dir in sorted)
                {
                    string folderName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{folderName}.json");
                    var (imageCount, _) = ReadFolderJson(jsonPath);

                    if (imageCount > 0)
                    {
                        cacheResult[dir] = imageCount;
                    }
                    else
                    {
                        // キャッシュ未存在の場合は直接カウント
                        int count = CountImages(dir);
                        cacheResult[dir] = count;
                        SaveImageCountJson(jsonPath, count);
                    }
                }

                // 第三段階: フィルタ＋ListBox表示用データ作成
                foreach (var dir in sorted)
                {
                    int ic = cacheResult.TryGetValue(dir, out var v) ? v : 0;

                    // フィルタ基準未満のフォルダは除外（有効な場合のみ）
                    if (_settings.MinDisplayCountEnabled && ic < _settings.MinDisplayCount)
                        continue;

                    folderList.Add(dir);
                }
            }
            catch { }

            return folderList;
        }

        /// <summary>
        /// 指定フォルダ内の画像パスを取得・ソート
        /// </summary>
        public List<string> LoadAndSortImages(string folderPath)
        {
            var imagePaths = new List<string>();

            string[] extensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };
            var allFiles = new List<string>();
            foreach (var ext in extensions)
            {
                try { allFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly)); } catch { }
            }
            imagePaths = allFiles.OrderBy(f => ExtractNumberFromFileName(f)).ToList();

            return imagePaths;
        }

        /// <summary>
        /// ファイル名から数字を抽出（ソート用）
        /// </summary>
        public static int ExtractNumberFromFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int number)) return number;
            return 0;
        }

        /// <summary>
        /// 指定フォルダ内の画像数をカウント（1回のディスクI/Oで完了）
        /// </summary>
        public static int CountImages(string dir)
        {
            try
            {
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".webp", ".png" };
                return Directory.EnumerateFiles(dir, "*.*")
                    .Where(f => extensions.Contains(Path.GetExtension(f)))
                    .Count();
            }
            catch { return 0; }
        }

        /// <summary>
        /// フォルダのJSONメタデータを1回のファイルI/Oで読み込む（imageCount, rating）。
        /// </summary>
        public static (int imageCount, int rating) ReadFolderJson(string jsonPath)
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

        /// <summary>
        /// 画像数を JSON ファイルに保存（{ "imageCount": N }）
        /// </summary>
        public static void SaveImageCountJson(string jsonPath, int imageCount)
        {
            try
            {
                var obj = new Dictionary<string, int> { { "imageCount", imageCount } };
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>
        /// 指定フォルダの評価値を JSON に保存（既存のJSONにratingを追加・更新）
        /// </summary>
        public static void SaveRatingToFolder(string folderPath, int rating)
        {
            try
            {
                string folderName = Path.GetFileName(folderPath);
                string jsonPath = Path.Combine(folderPath, $"{folderName}.json");

                var (imageCount, _) = ReadFolderJson(jsonPath);

                var obj = new Dictionary<string, object> { { "rating", rating } };
                if (imageCount > 0)
                    obj["imageCount"] = imageCount;

                File.WriteAllText(jsonPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>
        /// ListBox表示用のフォルダデータ一覧を取得（パス + 画像数）
        /// </summary>
        public List<(string path, int imageCount)> GetFolderDisplayData(string rootPath)
        {
            var folderData = new List<(string path, int imageCount)>();

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                var sorted = dirs
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var dir in sorted)
                {
                    int ic;
                    if (_settings.MinDisplayCountEnabled && _settings.MinDisplayCount > 0)
                        ic = GetCachedOrCount(dir);
                    else
                        ic = GetCachedOrCount(dir);

                    // フィルタ基準未満のフォルダは除外（有効な場合のみ）
                    if (_settings.MinDisplayCountEnabled && ic < _settings.MinDisplayCount)
                        continue;

                    folderData.Add((dir, ic));
                }
            }
            catch { }

            return folderData;
        }

        /// <summary>
        /// フィルタ統計情報を計算（合计数、不합계数、总数）
        /// </summary>
        public (int passCount, int failCount, int totalCount) ComputeFilterStats(string rootPath)
        {
            int passCount = 0;
            int failCount = 0;
            int totalCount = 0;

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                foreach (var dir in dirs)
                {
                    string folderName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{folderName}.json");
                    var (imageCount, _) = ReadFolderJson(jsonPath);

                    if (imageCount == 0)
                        imageCount = GetCachedOrCount(dir);

                    totalCount++;

                    if (_settings.MinDisplayCountEnabled && imageCount < _settings.MinDisplayCount)
                        failCount++;
                    else
                        passCount++;
                }
            }
            catch { }

            return (passCount, failCount, totalCount);
        }

        private int GetCachedOrCount(string dir)
        {
            string folderName = Path.GetFileName(dir);
            string jsonPath = Path.Combine(dir, $"{folderName}.json");
            var (imageCount, _) = ReadFolderJson(jsonPath);

            if (imageCount > 0)
                return imageCount;

            int count = CountImages(dir);
            SaveImageCountJson(jsonPath, count);
            return count;
        }
    }
}