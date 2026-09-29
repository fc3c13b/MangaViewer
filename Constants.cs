using System;
using System.Reflection;

namespace MangaViewer
{
    /// <summary>
    /// アプリケーション全体の定数を一元管理する静的クラス。
    /// </summary>
    public static class Constants
    {
        #region バージョン

        /// <summary>
        /// csproj の Version から自動取得したアプリケーションバージョン（例：0.9.0）。
        /// </summary>
        public static string AppVersion => GetAppVersion();

        private static string? _appVersion;

        private static string GetAppVersion()
        {
            if (_appVersion != null)
                return _appVersion;

            try
            {
                var attr = Assembly.GetExecutingAssembly()
                                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var informational = attr?.InformationalVersion;
                if (!string.IsNullOrEmpty(informational))
                {
                    // "0.9.0+git..." のような形式なら "+" 以降を切る
                    int idx = informational.IndexOf('+');
                    _appVersion = idx > 0
                        ? informational.Substring(0, idx)
                        : informational;
                    return _appVersion;
                }

                // Fallback: FileVersion (0.9.0.0) -> "0.9.0"
                var fva = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
                if (!string.IsNullOrEmpty(fva))
                {
                    var parts = fva.Split('.');
                    if (parts.Length >= 3)
                        _appVersion = string.Join(".", parts[..3]);
                    else
                        _appVersion = fva;
                }
            }
            catch
            {
                // Fallback: hard-coded safety version
                _appVersion = "0.0.1";
            }

                return _appVersion ?? "0.0.1";
        }

        #endregion

        #region レイアウト比率

        /// <summary>左画像領域の比率（36）</summary>
        public const int RatioLeftImg = 36;

        /// <summary>右画像領域の比率（36）</summary>
        public const int RatioRightImg = 36;

        /// <summary>フォルダリスト領域の比率（28）</summary>
        public const int RatioList = 28;

        /// <summary>合計比率（100）</summary>
        public const int TotalRatio = RatioLeftImg + RatioRightImg + RatioList;

        #endregion

        #region 画像キャッシュ

        /// <summary>キャッシュに保持する最大画像数</summary>
        public const int MaxCacheSize = 200;

        #endregion

        #region ウィンドウ

        /// <summary>アプリケーションタイトル（バージョンは csproj から自動取得）</summary>
        public static string AppTitle => "Manga Viewer v" + AppVersion;

        /// <summary>ウィンドウ初期幅</summary>
        public const int InitialWidth = 1400;

        /// <summary>ウィンドウ初期高さ</summary>
        public const int InitialHeight = 800;

        /// <summary>ウィンドウ最小幅</summary>
        public const int MinWidth = 900;

        /// <summary>ウィンドウ最小高さ</summary>
        public const int MinHeight = 600;

        #endregion


        #region 拡張子フィルタ

        /// <summary>サポートする画像ファイルの拡張子</summary>
        public static readonly string[] ImageExtensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };

        #endregion

        #region 評価表示DB

        /// <summary>フィルタ対象の評価値（固定）</summary>
        internal const int TargetDisplayRating = 8;

        /// <summary>rank_display_db.json のファイル名</summary>
        public const string RankDisplayDbName = "rank_display_db.json";

        #endregion
    }
}
