using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// Form1 から分離した「ナビゲーション＋表示＋設定変更」のビジネスロジック。
    /// UI直結は最小限（Form1 の public メンバ経由）に留める。
    /// </summary>
    public static class FormNavigator
    {
        // ===== DBList 再構築 =====

        public static void BuildRankFiltered(Form1 form)
        {
            if (!form.IsRankDisplayMode || form._activeCjData == null) return;

            var filteredFolders = DbListBuilder.BuildFromActiveCj(
                form._activeCjData,
                form._activeDbFile,
                form._settings);

            form._folderList.Clear();
            form.listBoxFolders.DataSource = null;
            form.listBoxFolders.Items.Clear();

            foreach (var folderPath in filteredFolders)
            {
                if (string.IsNullOrEmpty(folderPath)) continue;
                form._folderList.Add(folderPath);

                if (form._activeCjData.Folders.TryGetValue(folderPath, out var entry))
                {
                    string folderName = Path.GetFileName(folderPath);
                    int displayCount = entry.CbzZipCount ?? 0;
                    form.listBoxFolders.Items.Add($"[{displayCount}] {folderName}");
                }
            }

            if (form._folderList.Count > 0)
            {
                int idx = form._folderList.IndexOf(form._currentFolder);
                form._currentFolderIndex = idx >= 0 ? idx : 0;
                form.listBoxFolders.SelectedIndex = form._currentFolderIndex;
            }
        }

        // ===== RootFolder / ChangeRootFolder =====

        public static void ChangeRootFolder(Form1 form, string rootPath)
        {
            if (form.InvokeRequired)
            {
                form.BeginInvoke(new Action<string>(root => ChangeRootFolder(form, root)), rootPath);
                return;
            }

            form._settings.LastRootFolder = rootPath;
            SettingsManager.Save(form._settings);
            form.BuildSubfolderList(rootPath);
            form._displayManager.UpdateSettings(form._settings);
            form.UpdateWindowTitle();

            if (form._folderList.Count > 0 && form._currentFolderIndex >= 0)
            {
                form.LoadAndSortImages(form._folderList[form._currentFolderIndex]);
                form._currentIndex = 0;
                DisplayImagesCore(form, form._currentIndex);
                form.listBoxFolders.SelectedIndex = form._currentFolderIndex;
            }
            else if (form._folderList.Count == 0)
            {
                form._folderList.Clear();
                form._currentFolderIndex = -1;

                if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
                {
                    form.LoadAndSortImages(rootPath);
                    form._currentIndex = 0;
                    DisplayImagesCore(form, form._currentIndex);
                }
                else
                {
                    form._imagePaths.Clear();
                    form._currentFolder = "";
                    form._currentIndex = 0;
                    DisplayImagesCore(form, 0);
                }

                form.labelInfo.Text = "表示可能なフォルダがありません。";
            }
        }

        // ===== SettingsDialog / filter change handling =====

        public static void ApplySettingsChanges(Form1 form)
        {
            int prevDisplayCount = form._settings.DisplayCount;
            int prevDbMinDisplayCount = form._settings.DbMinDisplayCount;
            int prevDbMaxDisplayCount = form._settings.DbMaxDisplayCount;
            int prevDbMinEvaluation = form._settings.DbMinEvaluation;

            using (var dialog = new SettingsDialog())
            {
                if (dialog.ShowDialog(form) == DialogResult.OK)
                {
                    (form._settings, _) = SettingsManager.LoadWithValidation();
                    form._displayManager.UpdateSettings(form._settings);

                    bool displayCountChanged = form._settings.DisplayCount != prevDisplayCount;

                    // DBList mode filters changed?
                    bool dbFilterChanged =
                        form._settings.DbMinEvaluation != prevDbMinEvaluation ||
                        form._settings.DbMinDisplayCount != prevDbMinDisplayCount ||
                        form._settings.DbMaxDisplayCount != prevDbMaxDisplayCount;

                    if (form.IsRankDisplayMode && dbFilterChanged)
                    {
                        BuildRankFiltered(form);

                        int newIdx = -1;
                        for (int i = 0; i < form._folderList.Count; i++)
                        {
                            if (form._folderList[i] == form._currentFolder) { newIdx = i; break; }
                        }

                        if (newIdx >= 0)
                        {
                            form._currentFolderIndex = newIdx;
                            form.listBoxFolders.SelectedIndex = newIdx;
                        }
                        else if (form._folderList.Count > 0)
                        {
                            form._currentFolderIndex = 0;
                            form.LoadAndSortImages(form._folderList[0]);
                            form._currentIndex = 0;
                            DisplayImagesCore(form, 0);
                            form.listBoxFolders.SelectedIndex = 0;
                        }
                    }

                    if (displayCountChanged)
                    {
                        form._displayManager.InitializePictureBoxes();
                        form.UpdateLayout();
                        form._currentIndex = 0;
                        DisplayImagesCore(form, 0);
                    }
                }
            }
        }

        // ===== Display / InfoText helpers =====

        public static void DisplayImages(Form1 form, int startIndex)
        {
            form._displayManager.ImagePaths = form._imagePaths;
            form._displayManager.DisplayImages(startIndex);

            string infoText = BuildInfoText(form, startIndex);
            if (!string.IsNullOrEmpty(infoText))
                form.labelInfo.Text = infoText;
            else
                form.labelInfo.Text = $"[{GetFolderDisplay(form)}] 表示可能な画像がありません。";

            // CBZ preload when near end
            if (form._cbzManager != null && form._imagePaths.Count > 0)
            {
                int remainingPages = form._imagePaths.Count - startIndex;
                if (remainingPages <= 16)
                {
                    System.Threading.Tasks.Task.Run(() => form._cbzManager.PreloadNextCbx());
                }
            }
        }

        public static string BuildInfoText(Form1 form, int startIndex)
        {
            string folderDisplay = GetFolderDisplay(form);
            string infoText = form._displayManager.GetInfoText(
                form._currentFolder,
                form._currentFolderIndex,
                form._folderService?.entries);

            if (!string.IsNullOrEmpty(infoText))
                return $"[{folderDisplay}] {infoText.TrimStart()}";
            return "";
        }

        private static string GetFolderDisplay(Form1 form)
        {
            string folderName = Path.GetFileName(form._currentFolder);
            string cbzInfo = "";

            if (form._cbzManager != null && form._cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(form._cbzManager.ActiveCbxIndex, 0, form._cbzManager.CbxFiles.Count - 1);
                string cbzFileName = Path.GetFileNameWithoutExtension(form._cbzManager.CbxFiles[idx]);
                int volNum = FolderService.ExtractNumberFromFileName(cbzFileName);
                if (volNum > 0)
                    cbzInfo = $" [{cbzFileName}]";
            }

            return $"{folderName}{cbzInfo}";
        }

        // ===== Navigation helpers (Ctrl/Alt arrows, etc.) =====

        public static void NavigateForward(Form1 form, int pageCount)
        {
            if (form._imagePaths.Count == 0 || pageCount <= 0) return;

            form._currentIndex = NavigationHandler.ComputeForwardIndex(
                form._currentIndex, pageCount, form._imagePaths.Count, form._displayManager.DisplayCount);

            // CBZ末尾を超えた場合、次のCBZに切り替え
            if (NavigationHandler.IsPastEnd(form._currentIndex, form._imagePaths.Count, form._displayManager.DisplayCount) &&
                form._cbzManager != null)
            {
                var nextPaths = form._cbzManager.MoveToNextCbxIfEndReached();
                if (nextPaths.Any())
                {
                    form._imagePaths = nextPaths;
                    form._currentIndex = 0;
                }
                else
                {
                    form._currentIndex = NavigationHandler.ComputeMaxPageIndex(
                        form._imagePaths.Count, form._displayManager.DisplayCount);
                }
            }

            DisplayImages(form, form._currentIndex);
        }

        public static void NavigateBackward(Form1 form, int pageCount)
        {
            if (form._imagePaths.Count == 0 || pageCount <= 0) return;

            form._currentIndex = NavigationHandler.ComputeBackwardIndex(
                form._currentIndex, pageCount, form._imagePaths.Count, form._displayManager.DisplayCount);

            // CBZ先頭を超えた場合、前のCBZに切り替え
            if (NavigationHandler.IsBeforeStart(form._currentIndex) && form._cbzManager != null)
            {
                var prevPaths = form._cbzManager.MoveToPreviousCbxIfAtStart();
                if (prevPaths.Any())
                {
                    form._imagePaths = prevPaths;
                    form._currentIndex = NavigationHandler.ComputeMaxPageIndex(
                        form._imagePaths.Count, form._displayManager.DisplayCount);
                }
                else
                {
                    form._currentIndex = 0;
                }
            }

            DisplayImages(form, form._currentIndex);
        }

        public static void NavigateFolderBy(Form1 form, int delta)
        {
            if (form._folderList.Count == 0) return;
            int newIndex = form._currentFolderIndex + delta;
            newIndex = Math.Max(0, Math.Min(newIndex, form._folderList.Count - 1));
            if (newIndex == form._currentFolderIndex) return;

            form._currentFolderIndex = newIndex;
            form.LoadAndSortImages(form._folderList[newIndex]);
            form._currentIndex = 0;
            DisplayImagesCore(form, 0);
            form.listBoxFolders.SetSelected(newIndex, true);
        }

        public static void NavigateToNextUnrated(Form1 form)
        {
            for (int i = 0; i < form._folderList.Count; i++)
            {
                if (RatingService.ReadRating(form._folderList[i]) == -1)
                {
                    form._currentFolderIndex = i;
                    form.LoadAndSortImages(form._folderList[i]);
                    form._currentIndex = 0;
                    DisplayImagesCore(form, 0);
                    form.listBoxFolders.SelectedIndex = i;
                    return;
                }
            }
        }

        public static void SetDisplayCount(Form1 form, int count)
        {
            if (count <= 0) return;

            int prevDisplayCount = form._settings.DisplayCount;
            form._settings.DisplayCount = count;
            SettingsManager.Save(form._settings);

            form._displayManager.UpdateSettings(form._settings);

            if (form._settings.DisplayCount != prevDisplayCount)
            {
                form._displayManager.InitializePictureBoxes();
                form.UpdateLayout();
                form._currentIndex = 0;
                DisplayImagesCore(form, 0);
            }

            form.Focus();
        }

        public static void NavigateCbzNext(Form1 form)
        {
            if (form._cbzManager == null || !NavigationHandler.CanNavigateCbx(form._cbzManager.CbxFiles.Count)) return;

            var nextCbx = form._cbzManager.SwitchToNextCbx();
            if (nextCbx != null)
            {
                form._imagePaths = form._cbzManager.CurrentImagePaths;
                form._currentIndex = 0;
                DisplayImagesCore(form, 0);
            }
        }

        public static void NavigateCbzPrev(Form1 form)
        {
            if (form._cbzManager == null || !NavigationHandler.CanNavigateCbx(form._cbzManager.CbxFiles.Count)) return;

            var prevCbx = form._cbzManager.SwitchToPreviousCbx();
            if (prevCbx != null)
            {
                form._imagePaths = form._cbzManager.CurrentImagePaths;
                form._currentIndex = 0;
                DisplayImagesCore(form, 0);
            }
        }

        // ===== FullScreen info-text refresh =====

        public static void RefreshFullScreenInfo(Form1 form)
        {
            string infoText = BuildInfoText(form, form._currentIndex);
            if (!string.IsNullOrEmpty(infoText))
                form.labelInfo.Text = infoText;
            else
                form.labelInfo.Text = $"[{GetFolderDisplay(form)}] 表示可能な画像がありません。";

            form.UpdateLayout();
        }

        // ===== Internal helper (no public caller) =====

        private static void DisplayImagesCore(Form1 form, int index)
        {
            form._displayManager.ImagePaths = form._imagePaths;
            form._displayManager.DisplayImages(index);
        }
    }
}
