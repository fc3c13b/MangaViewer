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
        // 公開されたエントリーポイント: Form1/ListBox から呼び出す用
        public static void HandleKeyDown(KeyEventArgs e, INavigationActions nav)
        {
            if (nav is Form form)
            {
                HandleKeyDownInternal(e, nav, form);
            }
            else
            {
                // Form が不明な場合は基本処理のみ
                HandleBasicKeys(e, nav, listBoxFocused: false);
            }
        }

        private static void HandleKeyDownInternal(KeyEventArgs e, INavigationActions nav, Form form)
        {
            // ListBox がフォーカスを持っているかを確認
            bool listBoxFocused = IsListBoxFoldersFocused(form);
            HandleBasicKeys(e, nav, listBoxFocused);
        }

        // 基本キー処理（矢印キー、Esc、主要ショートカット）
        private static void HandleBasicKeys(KeyEventArgs e, INavigationActions nav, bool listBoxFocused)
        {
            Keys key = e.KeyCode;
            bool ctrl = (e.Control && !e.Alt);
            bool alt = e.Alt && !e.Control;

            // Ctrl+数字/テンキー/etc は評価用（既存の動作）
            if (ctrl && IsRatingKey(e))
            {
                HandleRatingInput(e, nav);
                return;
            }

            // Alt + 左右: CBZファイル間切り替え / 上下: フォルダジャンプ
            if (alt)
            {
                switch (key)
                {
                    case Keys.Left:
                        e.Handled = true;
                        nav.NavigateCbzNext();
                        return;
                    case Keys.Right:
                        e.Handled = true;
                        nav.NavigateCbzPrev();
                        return;
                    case Keys.Up:
                        e.Handled = true;
                        nav.NavigateFolders(-10);
                        return;
                    case Keys.Down:
                        e.Handled = true;
                        nav.NavigateFolders(10);
                        return;
                }
            }

            // Ctrl + 左右: DisplayCount×4 ページ移動
            if (ctrl)
            {
                switch (key)
                {
                    case Keys.Left:
                        e.Handled = true;
                        nav.NavigateForward(nav.DisplayCount * 4);
                        return;
                    case Keys.Right:
                        e.Handled = true;
                        nav.NavigateBackward(nav.DisplayCount * 4);
                        return;
                }

                // Ctrl + 上下: フォルダ移動（±1）
                switch (key)
                {
                    case Keys.Up:
                        e.Handled = true;
                        nav.NavigateFolderUp();
                        return;
                    case Keys.Down:
                        e.Handled = true;
                        nav.NavigateFolderDown();
                        return;
                }
            }

            // Esc: フルスクリーン切替
            if (key == Keys.Escape)
            {
                e.Handled = true;
                nav.ToggleFullScreen();
                return;
            }

         // 数字キー（Ctrlなし）:
             // - 1 : ルートフォルダ選択ダイアログ
             // - 2,8 : 表示枚数 (DisplayCount) を設定
             // - 9 : フルスクリーン ⇔ ノーマル切替
             // - 3〜7 : なし（何もしない）
             // - 0   : 設定ダイアログを表示
             if (!ctrl && !alt)
             {
                 switch (key)
                 {
                     case Keys.D1:
                         try { System.IO.File.AppendAllText("error.log", $"[Keyboard] D1 pressed, calling SetDisplayCount(1){Environment.NewLine}"); } catch { }
                         e.Handled = true;
                         nav.SetDisplayCount(1);
                         return;

                    case Keys.D2:
                        e.Handled = true;
                        nav.SetDisplayCount(2);
                        return;

                    case Keys.D8:
                        e.Handled = true;
                        nav.SetDisplayCount(8);
                        return;

                    case Keys.D9:
                        e.Handled = true;
                        nav.ToggleFullScreen();
                        return;

                    // 3 : ルートフォルダ選択ダイアログ
                    case Keys.D3:
                        e.Handled = true;
                        HandleRootFolderChange(nav);
                        return;

                    // 4〜7 は何もしない（fallthrough なし）
                    case Keys.D4:
                    case Keys.D5:
                    case Keys.D6:
                    case Keys.D7:
                        return;

                    case Keys.D0:
                        e.Handled = true;
                        nav.ShowSettingsDialog();
                        return;
                }
            }

            // S キー: スライドショー切替（ListBoxフォーカス時は無効化）
            if (!ctrl && !alt && key == Keys.S && !listBoxFocused)
            {
                e.Handled = true;
                ToggleSlideshow(nav);
                return;
            }

            // 左右矢印キー: DisplayCount分移動（ListBoxフォーカス時は無効化）
            if (!ctrl && !alt && !listBoxFocused)
            {
                switch (key)
                {
                    case Keys.Left:
                        e.Handled = true;
                        nav.NavigateForward(nav.DisplayCount);
                        return;
                    case Keys.Right:
                        e.Handled = true;
                        nav.NavigateBackward(nav.DisplayCount);
                        return;
                }
            }
        }

        private static bool IsListBoxFoldersFocused(Form form)
        {
            // Form1 のコントロール内から listBoxFolders を探す
            foreach (var c in form.Controls)
            {
                if (c is Panel panel)
                {
                    foreach (var pc in panel.Controls)
                    {
                        if (pc is ListBox lb && lb.Focused)
                            return true;
                    }
                }
            }
            return false;
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
            // Ctrl + (数字キー / テンキー / ＋ / −) で評価値保存。
            int rating = e.KeyCode switch
            {
                // テンキー
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
                // 上部数字キー
                Keys.D0 => 0,
                Keys.D1 => 1,
                Keys.D2 => 2,
                Keys.D3 => 3,
                Keys.D4 => 4,
                Keys.D5 => 5,
                Keys.D6 => 6,
                Keys.D7 => 7,
                Keys.D8 => 8,
                Keys.D9 => 9,
                // ＋ / − （テンキーおよびメインキーボード）
                Keys.Add      => 10,
                Keys.Oemplus  => 10,     // = / + キー (メインキーボード)
                Keys.Subtract  => -1,
                Keys.OemMinus  => -1,    // メインキーボードのー
                _               => int.MinValue
            };

            if (rating != int.MinValue)
            {
                e.Handled = true;
                RatingService.SaveRating(nav.CurrentFolder, rating);
                nav.NavigateToNextUnrated();
            }
        }

        private static bool IsRatingKey(KeyEventArgs e)
        {
            Keys k = e.KeyCode;
            if (k >= Keys.D0 && k <= Keys.D9) return true;
            if ((int)k >= (int)Keys.NumPad0 && (int)k <= (int)Keys.NumPad9) return true;
            switch (k)
            {
                case Keys.Add:
                case Keys.Subtract:
                case Keys.Oemplus:
                case Keys.OemMinus:
                    return true;
                default:
                    return false;
            }
        }

        private static void ToggleSlideshow(INavigationActions nav)
        {
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