using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// キーボード入力の処理を担当する静的クラス。
    /// Form1 の internal メンバーにアクセスしてナビゲーション操作を実行する。
    /// </summary>
    public static class KeyboardInputHandler
    {
        public static void HandleKeyDown(KeyEventArgs e, Form1 form)
        {
            // D0: 設定ダイアログ
            if (e.KeyCode == Keys.D0)
            {
                HandleSettingsDialog(form);
            }
            // D1: ルートフォルダ変更
            else if (e.KeyCode == Keys.D1)
            {
                HandleRootFolderChange(form);
            }
            // 右矢印: 2枚戻る
            else if (e.KeyCode == Keys.Right)
            {
                NavigateBackwardTwoPages(form);
            }
            // 左矢印: 2枚進む（または単枚表示）
            else if (e.KeyCode == Keys.Left)
            {
                NavigateForwardTwoPages(form);
            }
            // Ctrl + テンキー: 評価値保存
            else if ((Control.ModifierKeys & Keys.Control) != 0 && form._imagePaths.Count > 0)
            {
                HandleRatingInput(e, form);
            }
            // Up: 前のフォルダ
            else if (e.KeyCode == Keys.Up && form._folderList.Count > 0)
            {
                e.Handled = true;
                NavigateFolderUp(form);
            }
            // Down: 次のフォルダ
            else if (e.KeyCode == Keys.Down && form._folderList.Count > 0)
            {
                e.Handled = true;
                NavigateFolderDown(form);
            }
        }

        private static void HandleSettingsDialog(Form1 form)
        {
            bool prevEnabled = form._settings.MinDisplayCountEnabled;
            int prevValue = form._settings.MinDisplayCount;

            using (var dialog = new SettingsDialog())
            {
                if (dialog.ShowDialog(form) == DialogResult.OK)
                {
                    form._settings = SettingsManager.Load();

                    // 最小表示枚数設定が変更された場合はフォルダリストを再フィルタ
                    if (form._settings.MinDisplayCountEnabled != prevEnabled || form._settings.MinDisplayCount != prevValue)
                    {
                        string rootPath = string.IsNullOrEmpty(form._settings.LastRootFolder)
                            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                            : form._settings.LastRootFolder;
                        form.BuildSubfolderList(rootPath);

                        // 現在のフォルダが新しいリストに含まれているか確認
                        int newIdx = -1;
                        for (int i = 0; i < form._folderList.Count; i++)
                        {
                            if (form._folderList[i] == form._currentFolder)
                            { newIdx = i; break; }
                        }

                        if (newIdx >= 0)
                        {
                            form._currentFolderIndex = newIdx;
                            form.listBoxFolders.SelectedIndex = newIdx;
                        }
                        else
                        {
                            if (form._folderList.Count > 0)
                            {
                                form._currentFolderIndex = 0;
                                form.LoadAndSortImages(form._folderList[0]);
                                form._currentIndex = 0;
                                form.DisplayTwoImages(0);
                            form.listBoxFolders.SelectedIndex = 0;
                            }
                            else
                            {
                                form._folderList.Clear();
                                form._currentFolderIndex = -1;
                                form.pictureBoxRight.Image = null;
                                 form.pictureBoxLeft.Image = null;
                                 form.labelInfo.Text = "表示可能なフォルダがありません。";
                            }
                        }
                    }
                }
            }
        }

        private static void HandleRootFolderChange(Form1 form)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string rootPath = dialog.SelectedPath;
                    form._settings.LastRootFolder = rootPath;
                    SettingsManager.Save(form._settings);
                    form.BuildSubfolderList(rootPath);

                    if (form._folderList.Count > 0)
                    {
                        form._currentFolderIndex = 0;
                        form.LoadAndSortImages(form._folderList[form._currentFolderIndex]);
                    }
                    else
                    {
                        form._folderList.Clear();
                        form._currentFolderIndex = -1;
                        form.LoadAndSortImages(rootPath);
                    }
                    form._currentIndex = 0;
                    form.DisplayTwoImages(form._currentIndex);
                    form.listBoxFolders.SelectedIndex = form._currentFolderIndex;
                }
            }
        }

        private static void NavigateBackwardTwoPages(Form1 form)
        {
            if (form._imagePaths.Count >= 2)
            {
                form._currentIndex -= 2;
                if (form._currentIndex < 0) form._currentIndex = 0;
                form.DisplayTwoImages(form._currentIndex);
            }
        }

        private static void NavigateForwardTwoPages(Form1 form)
        {
            if (form._imagePaths.Count >= 2)
            {
                form._currentIndex += 2;
                int maxIndex = form._imagePaths.Count - (form._imagePaths.Count % 2 == 0 ? 2 : 1);
                if (form._currentIndex > maxIndex) form._currentIndex = maxIndex;
                form.DisplayTwoImages(form._currentIndex);
            }
            else if (form._imagePaths.Count == 1)
            {
                form.DisplayTwoImages(0);
            }
        }

        private static void HandleRatingInput(KeyEventArgs e, Form1 form)
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
                form.SaveRatingToFolder(form._currentFolder, rating);
            }
        }

        private static void NavigateFolderUp(Form1 form)
        {
            if (form._currentFolderIndex > 0)
            {
                form._currentFolderIndex--;
                form.LoadAndSortImages(form._folderList[form._currentFolderIndex]);
                form._currentIndex = 0;
                form.DisplayTwoImages(form._currentIndex);
                form.listBoxFolders.SetSelected(form._currentFolderIndex, true);
            }
        }

        private static void NavigateFolderDown(Form1 form)
        {
            if (form._currentFolderIndex < form._folderList.Count - 1)
            {
                form._currentFolderIndex++;
                form.LoadAndSortImages(form._folderList[form._currentFolderIndex]);
                form._currentIndex = 0;
                form.DisplayTwoImages(form._currentIndex);
                form.listBoxFolders.SetSelected(form._currentFolderIndex, true);
            }
        }
    }
}