using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MangaViewer
{
    /// <summary>
    /// DBList（CJベース）のフィルタリング・再構築ロジックを一元管理。
    /// Form1 から呼ばれるだけで、内部計算はここに閉じる。
    /// </summary>
    public static class DbListBuilder
    {
        /// <summary>
        /// アクティブ CJ（オンメモリまたはファイル）から _folderList を再構築するロジック。
        /// 戻り値: フィルタリング済みフォルダパスリスト。
        /// </summary>
        public static List<string> BuildFromActiveCj(
            CjRoot? activeCjData,
            string? activeDbFile,
            Settings settings)
        {
            // 1) オンメモリ CJ データがあればそれを優先使用
            if (activeCjData?.Folders != null && activeCjData.Folders.Count > 0)
            {
                return BuildFromCj(activeCjData.Folders, settings);
            }

            // 2) ファイルからフォールバック（activeDbFile が存在する場合）
            if (!string.IsNullOrEmpty(activeDbFile) && File.Exists(activeDbFile))
            {
                try
                {
                    var json = File.ReadAllText(activeDbFile);
                    var options = new JsonSerializerOptions();
                    options.PropertyNameCaseInsensitive = true;
                    var cjRoot = JsonSerializer.Deserialize<CjRoot>(json, options);

                    if (cjRoot?.Folders != null)
                    {
                        return BuildFromCj(cjRoot.Folders, settings);
                    }
                }
                catch
                {
                    // 読み込み失敗時は空リストを返す（Form1 でログ出力可）
                }
            }

            // 3) アクティブ CJ が未選択または無効な場合：空リスト
            return new List<string>();
        }

        /// <summary>
        /// CJ の Folders から評価値・画像数フィルタを適用し、
        /// ソート済みフォルダパスリストを生成。
        /// </summary>
        public static List<string> BuildFromCj(
            Dictionary<string, CjFolderEntry> folders,
            Settings settings)
        {
            if (folders == null || folders.Count == 0)
                return new List<string>();

            // DBList モードのフィルタ設定を使用
            int minEvaluation = settings.DbMinEvaluation;
            int dbMinImageCount = settings.DbMinDisplayCount;   // DbMinDisplayCount を画像数フィルタとして流用
            int dbMaxImageCount = settings.DbMaxDisplayCount;

            var filtered = folders
                .Where(f => ShouldIncludeFolder(
                    f.Value.Rating,
                    f.Value.ImageCount ?? 0,
                    minEvaluation,
                    dbMinImageCount,
                    dbMaxImageCount))
                .ToList();

            // ソート: 評価値降順 → ImageCount 降順
            var sorted = filtered
                .OrderByDescending(f => f.Value.Rating)
                .ThenByDescending(f => f.Value.ImageCount ?? 0)
                .Select(f => f.Key)
                .ToList();

            return sorted;
        }

        /// <summary>
        /// フォルダを含めるかどうかを判定（DBList モード用）。
        /// </summary>
        private static bool ShouldIncludeFolder(
            int rating,
            int imageCount,
            int minEvaluation,
            int dbMinImageCount,
            int dbMaxImageCount)
        {
            // 評価値フィルタ（minEvaluation が設定されている場合）
            if (rating < minEvaluation && minEvaluation > 0)
                return false;

            // ImageCount フィルタ
            if (dbMinImageCount > 0 && imageCount < dbMinImageCount)
                return false;

            if (dbMaxImageCount > 0 && imageCount > dbMaxImageCount)
                return false;

            return true;
        }
    }
}