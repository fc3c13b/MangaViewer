using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MangaViewer
{
    /// <summary>
    /// 評価値の保存・読み込みを担当するサービス。
    /// </summary>
    public class RatingService
    {
        /// <summary>
        /// 指定フォルダの評価値を JSON に保存（既存のJSONにratingを追加・更新）
        /// </summary>
        public static void SaveRating(string folderPath, int rating)
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
        /// フォルダの評価値を読み取る
        /// </summary>
        public static int ReadRating(string folderPath)
        {
            string folderName = Path.GetFileName(folderPath);
            string jsonPath = Path.Combine(folderPath, $"{folderName}.json");
            var (_, rating) = ReadFolderJson(jsonPath);
            return rating;
        }

        /// <summary>
        /// フォルダパスの「フォルダー名部分」が、評価1キーワード（カンマ区切り）をいずれかを含んでいるかを判定する。
        /// 大文字・小文字は区別しない。
        /// </summary>
        public static bool IsFolderMatchedByRatingOneKeywords(string folderPath, string ratingOneKeywords)
        {
            if (string.IsNullOrWhiteSpace(ratingOneKeywords))
                return false;

            var trimmed = folderPath?.TrimEnd('/', '\\');
            string? folderName = !string.IsNullOrWhiteSpace(trimmed)
                ? Path.GetFileName(trimmed)
                : null;
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

        private static string GetFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            // 末尾のスラッシュなどを除去してからフォルダー名を抽出
            var trimmed = path.TrimEnd('/', '\\');
            var dir = Path.GetDirectoryName(trimmed);
            // DirectoryName が null または空なら、パス自体をフォルダー名とみなす
            if (!string.IsNullOrEmpty(dir))
                return Path.GetFileName(dir);
            return Path.GetFileName(trimmed);
        }

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
    }
}