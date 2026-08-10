using System;
using System.IO;

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
    }
}
