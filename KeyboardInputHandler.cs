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
            // D2: 2枚表示に切り替え
            else if (e.KeyCode == Keys.D2)
            {
                e.Handled = true;
                nav.SetDisplayCount(2);
            }
            // D8: 8枚表示に切り替え
            else if (e.KeyCode == Keys.D8)
            {
                e.Handled = true;
                nav.SetDisplayCount(8);
            }
            // D1: ルートフォルダ変更
            else if (e.KeyCode == Keys.D1)
            {
                HandleRootFolderChange(nav);
            }
            // Ctrl + Left/Right: ±16ページ
            else if (e.Control && !e.Alt && nav.ImageCount > 0)
            {
                int count = 16;
                if (!e.Shift && e.KeyCode == Keys.Right)
                {
                    e.Handled = true;
                    nav.NavigateBackward(count);
                }
                else if (!e.Shift && e.KeyCode == Keys.Left)
                {
                    e.Handled = true;
                    nav.NavigateForward(count);
                }
            }
            // Alt + Left/Right: ±32ページ
            else if (e.Alt && !e.Control && nav.ImageCount > 0)
            
                int count = 32;
                if (!e.Shift && e.KeyCode == Keys.Right)
                {
                    e.Handled = true;
                    nav.NavigateBackward(count);
                }
                else if (!e.Shift && e.KeyCode == Keys.Left)
                {
                    e.Handled = true;
                    nav.NavigateForward(count);
                }
            }
            // 右矢印: 2枚戻る
            else if (e.KeyCode == Keys.Right && !e.Control && !e.Alt)
            {
                nav.NavigateBackwardTwoPages();
            }
            // 左矢印: 2枚進む（または単枚表示）
            else if (e.KeyCode == Keys.Left && !e.Control && !e.Alt)
            {
                nav.NavigateForwardTwoPages();
            }
            // ESC / D9: フルスクリーンモードトグル（ESC または 9）
            // 複数ハンドラが重ならないよう Handled を使用する
            else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.D9)
            {
                if (!e.Handled)
                {
                    e.Handled = true;
                    nav.ToggleFullScreen();
                }
            }
            // S: スライドショー開始/停止
            else if (e.KeyCode == Keys.S)
            {
                ToggleSlideshow(nav);
            }
            // Alt + Up: 50個上へジャンプ（フォルダリスト）
            else if (e.Alt && !e.Control && e.KeyCode == Keys.Up && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolders(-50);
            }
            // Alt + Down: 50個下へジャンプ（フォルダリスト）
            else if (e.Alt && !e.Control && e.KeyCode == Keys.Down && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolders(50);
            }
            // Ctrl + Up: 5つ上に移動（前のフォルダ）
            else if (e.Control && !e.Alt && e.KeyCode == Keys.Up && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolderBy(-5);
            }
            // Ctrl + Down: 5つ下に移動（次のフォルダ）
            else if (e.Control && !e.Alt && e.KeyCode == Keys.Down && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolderBy(5);
            }
            // Up: 前のフォルダ
            else if (!e.Alt && !e.Control && e.KeyCode == Keys.Up && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolderUp();
            }
            // Down: 次のフォルダ
            else if (!e.Alt && !e.Control && e.KeyCode == Keys.Down && nav.FolderListCount > 0)
            {
                e.Handled = true;
                nav.NavigateFolderDown();
            }
            // Ctrl + テンキー: 評価値保存
            else if (e.Control && nav.ImageCount > 0)
            {
                HandleRatingInput(e, nav);
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
                nav.NavigateToNextUnrated();
            }
        }

        private static void ToggleSlideshow(INavigationActions nav)
        {
            // Start/stop slideshow via interface.
            // Form1 should implement the concrete behavior using a Timer and settings.
            if (nav.IsSlideshowRunning)
            {
                nav.StopSlideshow();
            }
            else
            {
                nav.StartSlideshow();
            }
        }
    }
}