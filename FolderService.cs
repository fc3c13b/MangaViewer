using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MangaViewer
{
    /// <summary>
    /// フォルダエントリ（パス、画像数、評価値を保持）
    /// </summary>
    public struct FolderEntry
    {
        public string Path;
        public int ImageCount;
        public int Rating; // -1: 評価未設定
    }

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

        #region 中核関数（フォルダスキャン＋フィルタ＋表示データを一括構築）

        /// <summary>
        /// ルートフォルダ内のサブフォルダを「一度だけ」スキャンし、
        /// フィルタ（最小画像数・最小評価値）を適用した結果を返す。
        /// ディレクトリスキャンは1回のみで、キャッシュを活用する。
        /// </summary>
        public List<FolderEntry> BuildFolderIndex(string rootPath)
        {
            var entries = new List<FolderEntry>();

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                var sorted = dirs
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var dir in sorted)
                {
                    string folderName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{folderName}.json");
                    var (imageCount, rating) = ReadFolderJson(jsonPath);

                    // キャッシュ未存在の場合は直接カウント＋保存
                    if (imageCount == 0)
                    {
                        imageCount = CountImages(dir);
                        SaveImageCountJson(jsonPath, imageCount);
                    }

                    // フィルタ: 最小画像数（MinDisplayCount > 0 の場合に有効）
                    if (_settings.MinDisplayCount > 0 && imageCount < _settings.MinDisplayCount)
                        continue;

                    // フィルタ: 最小評価値（MinEvaluation > 0 の場合に有効）
                    // rating == -1（未設定）または < 0 は通過する
                    if (_settings.MinEvaluation > 0 && rating >= 0 && rating < _settings.MinEvaluation)
                        continue;

                    entries.Add(new FolderEntry { Path = dir, ImageCount = imageCount, Rating = rating });
                }
            }
            catch { /* ディレクトリ読み取り失敗時は空リストを返す */ }

            return entries;
        }

        #endregion

        #region 公開メソッド（BuildFolderIndex を中核として利用）

        /// <summary>
        /// ルートフォルダ内のサブフォルダ一覧を取得し、フィルタリング適用
        /// （後方互換用：BuildFolderIndex の Path のみを返す）
        /// </summary>
        public List<string> BuildSubfolderList(string rootPath)
        {
            var entries = BuildFolderIndex(rootPath);
            return entries.Select(e => e.Path).ToList();
        }

        /// <summary>
        /// ListBox表示用のフォルダデータ一覧を取得（パス + 画像数）
        /// （後方互換用：BuildFolderIndex から派生）
        /// </summary>
        public List<(string path, int imageCount)> GetFolderDisplayData(string rootPath)
        {
            var entries = BuildFolderIndex(rootPath);
            return entries.Select(e => (e.Path, e.ImageCount)).ToList();
        }

        #endregion

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
                try { allFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly)); } catch { /* 拡張子ごとのファイル取得は失敗しても他の拡張子は続行 */ }
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
            catch { return 0; } /* カウント失敗時は0を返す（フォルダアクセス権限なしなど） */
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
            catch { /* JSON解析エラーはデフォルト値(0,-1)で継続 */ }

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
            catch { /* 画像数JSONの保存失敗は無視（次回再試行でカバー） */ }
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
            catch { /* 評価値JSONの保存失敗は無視 */ }
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

                    if (_settings.MinDisplayCount > 0 && imageCount < _settings.MinDisplayCount)
                        failCount++;
                    else
                        passCount++;
                }
            }
            catch { /* フィルタ統計計算失敗時は0で継続 */ }

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

        /// <summary>
        /// SettingsDialog で使用。指定されたパラメータで2段階フィルタ統計を計算。
        /// 戻り値：(画像数フィルター通過数, 総フォルダ数, 評価フィルター通過数)
        /// 評価フィルターは画像数フィルターを通ったフォルダのみを対象とする。
        /// </summary>
        public (int imagePassCount, int totalCount, int ratingPassCount) ComputeDisplayStats(string rootPath, int minImages, int minRating)
        {
            int imagePassCount = 0;
            int totalCount = 0;
            int ratingPassCount = 0;

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                foreach (var dir in dirs)
                {
                    string folderName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{folderName}.json");
                    var (imageCount, rating) = ReadFolderJson(jsonPath);

                    // キャッシュ未存在の場合はカウント＋保存
                    if (imageCount == 0)
                    {
                        imageCount = CountImages(dir);
                        SaveImageCountJson(jsonPath, imageCount);
                    }

                    totalCount++;

                    // 1段目：画像数フィルター
                    if (minImages > 0 && imageCount < minImages)
                        continue;
                    imagePassCount++;

                    // 2段目：評価フィルター（minRating <= 0 は無効）
                    // rating == -1（未設定）または < 0 は通過する
                    if (minRating <= 0)
                    {
                        ratingPassCount++;
                        continue;
                    }
                    // rating >= 0 で且つ rating < minRating の場合のみ除外
                    if (rating >= 0 && rating < minRating)
                        continue;
                    ratingPassCount++;
                }
            }
            catch { /* 失敗時は0で継続 */ }

            return (imagePassCount, totalCount, ratingPassCount);
        }
    }
}