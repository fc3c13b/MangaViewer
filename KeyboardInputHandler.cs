using System;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// キーボード入力の処理を担当する静的クラス。
    /// INavigationActions インターフェース経由でナビゲーション操作を実行し、
    /// Form1 の内部実装に依存しないように分離されています。
    /// </summary>
    public static class KeyboardInputHandler
    {
        public static void HandleKeyDown(KeyEventArgs e, INavigationActions nav)
        {
            // D0: 設定ダイアログ
            if (e.KeyCode == Keys.D0)
            {
                nav.ShowSettingsDialog();
            }
            // D1: ルートフォルダ変更
            else if (e.KeyCode == Keys.D1)
            {
                HandleRootFolderChange(nav);
            }
            // 右矢印: 2枚戻る
            else if (e.KeyCode == Keys.Right)
            {
                nav.NavigateBackwardTwoPages();
            }
            // 左矢印: 2枚進む（または単枚表示）
            else if (e.KeyCode == Keys.Left)
            {
                nav.NavigateForwardTwoPages();
            }
            // Ctrl + テンキー: 評価値保存
            else if ((Control.ModifierKeys & Keys.Control) != 0 && nav.ImageCount > 0)
            {
                HandleRatingInput(e, nav);
            }
            // Up: 前のフォルダ
            else if (e.KeyCode == Keys.Up && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolderUp();
            }
            // Down: 次のフォルダ
            else if (e.KeyCode == Keys.Down && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolderDown();
            }
        }

        private static void HandleRootFolderChange(INavigationActions nav)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string rootPath = dialog.SelectedPath;
                    nav.ChangeRootFolder(rootPath);
                }
            }
        }

        private static void HandleRatingInput(KeyEventArgs e, INavigationActions nav)
        {
            int rating = e.KeyCode switch
            {
                Keys.NumPad0 => 0,
                Keys.NumPad1 => 1,
                Keys.NumPad2 => 2,
                Keys.NumPad3 => 3,
                Keys.NumPad4 => 4,
                Keys.NumPad5 => 5,
                Keys.NumPad6 => 6,
                Keys.NumPad7 => 7,
                Keys.NumPad8 => 8,
                Keys.NumPad9 => 9,
                Keys.Add => 10,
                Keys.Subtract => -1,
                _ => int.MinValue
            };

            if (rating != int.MinValue)
            {
                e.Handled = true;
                RatingService.SaveRating(nav.CurrentFolder, rating);
            }
        }
    }
}