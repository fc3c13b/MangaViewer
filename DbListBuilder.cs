using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MangaViewer
{
    public static class DbListBuilder
    {
        public static List<string> BuildFromActiveCj(
            CjRoot? activeCjData,
            string? activeDbFile,
            Settings settings)
        {
            // DB(JSON) は起動時に1回だけ読み込み済み（_activeCjData）。
            // ここではメモリ内のデータのみを使用し、ファイル再読込を行わない。
            if (activeCjData?.Folders != null && activeCjData.Folders.Count > 0)
            {
                return BuildFromCj(activeCjData.Folders, settings);
            }

            // _activeCjData が未設定または空なら、何もしない（再読込禁止）。
            return new List<string>();
        }

        public static List<string> BuildFromCj(
            Dictionary<string, CjFolderEntry> folders,
            Settings settings)
        {
            if (folders == null || folders.Count == 0)
                return new List<string>();

            int minEvaluation = settings.DbMinEvaluation;
            bool greaterOrEqual = settings.DbFilterGreaterOrEqual;
            int dbMinImageCount = settings.DbMinDisplayCount;
            int dbMaxImageCount = settings.DbMaxDisplayCount;

            var filtered = folders
                .Where(f => ShouldIncludeFolder(
                    f.Value.Rating,
                    f.Value.ImageCount ?? 0,
                    f.Value.CbzZipCount ?? 0,
                    minEvaluation,
                    greaterOrEqual,
                    dbMinImageCount,
                    dbMaxImageCount))
                .ToList();

            var sorted = filtered
                .OrderByDescending(f => f.Value.Rating)
                .ThenByDescending(f => f.Value.ImageCount ?? 0)
                .Select(f => f.Key)
                .ToList();

            // フィルタログ：全判定後、一度に書き込み（逐次禁止）
            bool dbFilterActive = (minEvaluation > 0) || (dbMinImageCount > 0) || (dbMaxImageCount > 0);
            if (dbFilterActive && folders.Count > 0)
            {
                try
                {
                    var lines = new List<string>();
                    lines.Add("=== BuildFromCj filter log ===");
                    lines.Add($"minEvaluation={minEvaluation}, greaterOrEqual={greaterOrEqual}, dbMinImageCount={dbMinImageCount}, dbMaxImageCount={dbMaxImageCount}");
                    lines.Add($"FoldersBeforeFilter={folders.Count}, FoldersAfterFilter={sorted.Count}");

                    foreach (var f in folders)
                    {
                        string folderName = Path.GetFileName(f.Key);
                        int rating = f.Value.Rating;
                        int imgCount = f.Value.ImageCount ?? 0;
                        int cbzZipCount = f.Value.CbzZipCount ?? 0;
                        var reason = GetExclusionReason(rating, imgCount, cbzZipCount, minEvaluation, greaterOrEqual, dbMinImageCount, dbMaxImageCount);
                        lines.Add($"Folder={folderName}, Rating={rating}, ImageCount={imgCount}, CbzZipCount={cbzZipCount}, Reason={reason}");
                    }

                    string logDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MangaViewer");
                    Directory.CreateDirectory(logDir);
                    string logPath = Path.Combine(logDir, "filter_log.txt");

                    using (var sw = new StreamWriter(logPath, append: false))
                    {
                        foreach (var line in lines) sw.WriteLine(line);
                    }
                }
                catch { /* ignore */ }
            }

            return sorted;
        }

        private static string GetExclusionReason(
            int rating,
            int imageCount,
            int cbzZipCount,
            int minEvaluation,
            bool greaterOrEqual,
            int dbMinImageCount,
            int dbMaxImageCount)
        {
            var reasons = new List<string>();

            if (minEvaluation > 0 && rating >= 0)
            {
                if (greaterOrEqual)
                {
                    if (rating < minEvaluation)
                        reasons.Add($"RatingTooLow({rating}<{minEvaluation})");
                }
                else
                {
                    if (rating != minEvaluation)
                        reasons.Add($"RatingNotEqual({rating}!={minEvaluation})");
                }
            }

            // Same rule as ShouldIncludeFolder: skip min check if cbzZipCount >= 1
            if (cbzZipCount < 1 && dbMinImageCount > 0 && imageCount < dbMinImageCount)
                reasons.Add($"ImageCountTooLow({imageCount}<{dbMinImageCount})");

            if (dbMaxImageCount > 0 && imageCount > dbMaxImageCount)
                reasons.Add($"ImageCountTooHigh({imageCount}>{dbMaxImageCount})");

            return reasons.Count == 0 ? "Included" : string.Join("+", reasons);
        }

        private static bool ShouldIncludeFolder(
            int rating,
            int imageCount,
            int cbzZipCount,
            int minEvaluation,
            bool greaterOrEqual,
            int dbMinImageCount,
            int dbMaxImageCount)
        {
            if (minEvaluation > 0 && rating != -1)
            {
                if (greaterOrEqual)
                {
                    if (rating < minEvaluation) return false;
                }
                else
                {
                    if (rating != minEvaluation) return false;
                }
            }

            // Key fix: only enforce dbMinImageCount when cbzZipCount < 1
            if (cbzZipCount < 1 && dbMinImageCount > 0 && imageCount < dbMinImageCount) return false;
            if (dbMaxImageCount > 0 && imageCount > dbMaxImageCount) return false;
            return true;
        }
    }
}
