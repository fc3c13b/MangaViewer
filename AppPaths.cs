using System;
using System.IO;
using System.Linq;

namespace MangaViewer
{
    /// <summary>
    /// アプリケーションのファイルパス関連の定数・ユーティリティを一元管理する静的クラス。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>
        /// 設定ファイル (setting.json) のフルパス。
        /// </summary>
        public static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.json");

        /// <summary>
        /// エラーログファイルのフルパス。
        /// </summary>
        public static string ErrorLogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");

        /// <summary>
        /// READ ONLY フォルダ向けの評価キャッシュJSON (ratings_cache.json) のフルパス。
        /// </summary>
        public static string RatingsCacheFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ratings_cache.json");

        /// <summary>
        /// READ ONLY フォルダ向けのフォルダメタデータローカル保存ディレクトリ。
        /// </summary>
        public static string FolderMetaLocalDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MangaViewer",
            "folder_meta");

        /// <summary>
        /// CJ（アプリ缓存JSON）的save directory。
        /// Parent folder eachCJ filesave here。
        /// </summary>
        public static string CacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MangaViewer",
            "cache");

        /// <summary>
        /// CJ（アプリ缓存JSON）的save file path for specified parent folder.
        /// e.g. "O:\NEW" → "%APPDATA%\MangaViewer\cache\ratings_cache_O__New.json"
        /// </summary>
        public static string GetCacheFilePath(string parentFolder)
        {
            string escapedName = EscapeForFileName(parentFolder);
            return Path.Combine(CacheDir, $"ratings_cache_{escapedName}.json");
        }

        private static readonly System.Text.RegularExpressions.Regex _safeCharRegex = 
            new System.Text.RegularExpressions.Regex(@"[^A-Za-z0-9_.\-]");

        private static string EscapeForFileName(string path)
        {
            // 1. Replace with "_"。
            string escaped = _safeCharRegex.Replace(path, "_");
            // 2. Limit length to prevent excessively long filenames。
            if (escaped.Length > 64)
                escaped = escaped.Substring(0, 64);
            return escaped;
        }
        /// <summary>
        /// NAS書き込み不可時のフォルダスキャン結果ローカルキャッシュファイル。
        /// </summary>
        public static string FolderScanCacheFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MangaViewer",
            "folder_scan_cache.json");

        /// <summary>
        /// rank_display_db.json のパス (data/rank_display_db.json)。
        /// </summary>
        public static string RankDisplayDbPath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "data",
            "rank_display_db.json");

    }
}
