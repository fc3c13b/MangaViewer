namespace MangaViewer
{
    /// <summary>
    /// アプリケーション全体の定数を一元管理する静的クラス。
    /// </summary>
    public static class Constants
    {
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

        /// <summary>アプリケーションタイトル</summary>
        public const string AppTitle = "Manga Viewer";

        /// <summary>ウィンドウ初期幅</summary>
        public const int InitialWidth = 1400;

        /// <summary>ウィンドウ初期高さ</summary>
        public const int InitialHeight = 800;

        /// <summary>ウィンドウ最小幅</summary>
        public const int MinWidth = 900;

        /// <summary>ウィンドウ最小高さ</summary>
        public const int MinHeight = 600;

        #endregion

        #region バージョン

        /// <summary>アプリケーションバージョン（一か所管理）</summary>
        public const string AppVersion = "Ver3.7.1";

        #endregion

        #region 拡張子フィルタ

        /// <summary>サポートする画像ファイルの拡張子</summary>
        public static readonly string[] ImageExtensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };

        #endregion
    }
}