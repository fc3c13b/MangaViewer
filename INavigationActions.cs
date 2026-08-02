namespace MangaViewer
{
    /// <summary>
    /// ナビゲーション操作のインターフェース。
    /// KeyboardInputHandler が Form1 に直接依存しないようにするための抽象化レイヤー。
    /// </summary>
    public interface INavigationActions
    {
        void ShowSettingsDialog();
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
        int ImageCount { get; }
        int FolderListCount { get; }
        string CurrentFolder { get; }

         bool IsSlideshowRunning { get; }
         void StartSlideshow();
         void StopSlideshow();
     }
}
