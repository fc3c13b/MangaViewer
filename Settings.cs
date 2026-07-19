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

        /// <summary>最小評価値（0〜10、これ未満のフォルダを非表示にする基準に使用）</summary>
        public int MinEvaluation { get; set; } = 8;

        /// <summary>画面表示数（2 または 8 のみ許可）</summary>
        public int DisplayCount 
        { 
            get => _displayCount;
            set => _displayCount = (value == 2 || value == 8) ? value : 2;
        }
        private int _displayCount = 2;

        /// <summary>最後に選択したルートフォルダのパス</summary>
        public string LastRootFolder { get; set; } = "";

        /// <summary>
        /// デフォルト設定（ファイルがない場合に使用）
        /// </summary>
        public static Settings Default => new Settings();
    }
}