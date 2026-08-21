using System;

namespace MangaViewer
{
    /// <summary>
    /// アプリケーション設定のデータモデル。
    /// setting.json に読み書きされる値を保持する。
    /// </summary>
    public class Settings
    {
        /// <summary>最小表示枚数フィルタの有効/無効</summary>
        public bool MinDisplayCountEnabled { get; set; } = false;

        /// <summary>フォルダに表示される最低画像数（2〜100）</summary>
        public int MinDisplayCount { get; set; } = 20;

        /// <summary>フォルダに表示される最大画像数（MinDisplayCount+1 以上、デフォルト1000）</summary>
        public int MaxDisplayCount { get; set; } = 1000;

        /// <summary>最小評価値（0〜10、これ未満のフォルダを非表示にする基準に使用）</summary>
        public int MinEvaluation { get; set; } = 8;

        /// <summary>DBList モード：最小表示枚数</summary>
        public int DbMinDisplayCount { get; set; } = 20;

        /// <summary>DBList モード：最大表示枚数</summary>
        public int DbMaxDisplayCount { get; set; } = 1000;

        /// <summary>DBList モード：最小評価値フィルタ</summary>
        public int DbMinEvaluation { get; set; } = 8;

        /// <summary>画面表示数（1, 2, または 8 のみ許可）</summary>
        public int DisplayCount 
        { 
            get => _displayCount;
            set => _displayCount = (value == 1 || value == 2 || value == 8) ? value : 2;
        }
        private int _displayCount = 2;

        /// <summary>最後に選択したルートフォルダのパス</summary>
        public string LastRootFolder { get; set; } = "";

        /// <summary>スライドショーの間隔（ミリ秒）</summary>
        public int SlideshowIntervalMs { get; set; } = 2000;

        /// <summary>ノーマル表示時の画像領域幅比率（%、リスト=100-この値）。デフォルト 72%</summary>
        public int NormalModeImageAreaPercent { get; set; } = 72;

        /// <summary>全画面表示時の画像領域幅比率（%、リスト=100-この値）。デフォルト 85%</summary>
        public int FullScreenModeImageAreaPercent { get; set; } = 85;

        /// <summary>評価1キーワード（カンマ区切りで複数指定可能）</summary>
        public string RatingOneKeywords { get; set; } = "";

        /// <summary>CBZの最後まで到達した際に、同じフォルダ内の次のCBZに自動切り替えるかどうか</summary>
        public bool AutoSwitchNextCbz { get; set; } = true;

        /// <summary>表示モード: 0=フォルダー階層順, 1=評価値ランク順</summary>
        public int DisplayMode { get; set; } = 0;

        /// <summary>DB表示：評価値フィルタを「以上（≥）」で扱うか、「一致（==）」で扱うか。デフォルト=true（以上）</summary>
        public bool DbFilterGreaterOrEqual { get; set; } = true;

        /// <summary>
        /// デフォルト設定（ファイルがない場合に使用）
        /// </summary>
        public static Settings Default => new Settings();
    }
}
