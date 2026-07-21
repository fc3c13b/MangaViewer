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
        void NavigateToNextUnrated();
        void ToggleFullScreen();
        int ImageCount { get; }
        int FolderListCount { get; }
        string CurrentFolder { get; }

        void StartSlideshow();
        void StopSlideshow();
    }
}
