using System;
using System.IO;

namespace MangaViewer
{
    public static class RatingDisplayService
    {
        /// <summary>
        /// 指定フォルダの評価値を現在のDBList表示へ即時反映する。
        /// UI表示の更新責務を RatingService から切り離す。
        /// </summary>
        public static void RefreshFolderRatingDisplay(Form1 form, string folderPath)
        {
            if (form == null || string.IsNullOrWhiteSpace(folderPath))
                return;

            int idx = form._folderList.IndexOf(folderPath);
            if (idx < 0 || idx >= form.listBoxFolders.Items.Count)
                return;

            int rating = RatingService.ReadRating(folderPath);

            if (form._activeCjData != null && form._activeCjData.Folders.TryGetValue(folderPath, out var entry))
            {
                entry.Rating = rating;
                int displayCount = entry.CbzZipCount ?? 0;
                string ratingStr = rating >= 0 ? $"({rating})" : "[-]";
                string folderName = Path.GetFileName(folderPath);
                form.listBoxFolders.Items[idx] = $"[{displayCount}] {ratingStr} {folderName}";
                return;
            }

            // Fallback: CJエントリがない場合でも評価だけは即時表示する
            int cbzCount = 0;
            string folderOnlyName = Path.GetFileName(folderPath);
            string metaJson = Path.Combine(folderPath, $"{folderOnlyName}.json");
            if (File.Exists(metaJson))
            {
                try
                {
                    var (_, cnt, _) = FolderService.ReadFolderJson(metaJson);
                    cbzCount = cnt;
                }
                catch { }
            }

            string fallbackRatingStr = rating >= 0 ? $"({rating})" : "[-]";
            form.listBoxFolders.Items[idx] = $"[{cbzCount}] {fallbackRatingStr} {folderOnlyName}";
        }
    }
}
