namespace MangaViewer
{
    /// <summary>
    /// ナビゲーション操作のインターフェース。
    /// KeyboardInputHandler が Form1 に直接依存しないようにするための抽象化レイヤー。
    /// </summary>
     public interface INavigationActions
     {
         void ShowSettingsDialog();
         // フォルダ選択ダイアログをモーダルで表示（Form1 で owner を指定）
         void ShowRootFolderDialog();
         // 直接パスを設定（内部用 / テスト用）
         void ChangeRootFolder(string rootPath);
        void NavigateBackwardTwoPages();
        void NavigateForwardTwoPages();
         void NavigateFolderUp();
         void NavigateFolderDown();
          // 一度に N フォルダ分移動（内部で画像の再読み込みは最後に1回のみ）
          void NavigateFolderBy(int delta);

          // フォルダリストを指定量だけジャンプ移動（例: Alt+上下 ±50）
          void NavigateFolders(int delta);
         void NavigateToNextUnrated();
         void ToggleFullScreen();
          void SetDisplayCount(int count); // 2 or 8 (or other configured values)
          int DisplayCount { get; }

          // Nページ分移動（Ctrl/Alt/jump・矢印キー用）
         void NavigateForward(int pageCount);
         void NavigateBackward(int pageCount);

         int ImageCount { get; }
        int FolderListCount { get; }
        string CurrentFolder { get; }

         bool IsSlideshowRunning { get; }
         void StartSlideshow();
          void StopSlideshow();

          // CBZファイル間を切り替え（Alt+左右用）
          void NavigateCbzNext();
          void NavigateCbzPrev();

          // 表示モード切替（フォルダー階層順 ⇔ 評価値ランク順）
          void ToggleDisplayMode();

          // DBモードフラグ
          bool IsRankDisplayMode { get; }

          // 親フォルダー指定でCJを作成（キー3用、DBリストモード時）
          void CreateCjForParent(string parentFolder);
    }
}
