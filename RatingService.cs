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