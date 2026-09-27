using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
                    string ratingStr = entry.Rating >= 0 ? $"({entry.Rating})" : "[-]";
                    form.listBoxFolders.Items.Add($"[{displayCount}] {ratingStr} {folderName}");
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
            form.LoadCjForParent(rootPath);
            form._displayManager.UpdateSettings(form._settings);
            form.ScheduleWindowTitleUpdate();

            if (form._folderList.Count > 0 && form._currentFolderIndex >= 0)
            {
                form.LoadAndSortImages(form._folderList[form._currentFolderIndex]);
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
                    DisplayImagesCore(form, form._currentIndex);
                }
                else
                {
                    form._imagePaths.Clear();
                    form._currentFolder = "";
                    form._currentIndex = 0;
                    DisplayImagesCore(form, 0);
                }

                form.UpdateInfoLabelBase("表示可能なフォルダがありません。", "navigator:no-folders");
            }
        }

        // ===== SettingsDialog / filter change handling =====

        public static void ApplySettingsChanges(Form1 form)
        {
            int prevDisplayCount = form._settings.DisplayCount;
            int prevMinEvaluation = form._settings.MinEvaluation;
            int prevMinDisplayCount = form._settings.MinDisplayCount;
            int prevMaxDisplayCount = form._settings.MaxDisplayCount;

            using (var dialog = new SettingsDialog())
            {
                if (dialog.ShowDialog(form) == DialogResult.OK)
                {
                    (form._settings, _) = SettingsManager.LoadWithValidation();
                    form._displayManager.UpdateSettings(form._settings);

                    bool displayCountChanged = form._settings.DisplayCount != prevDisplayCount;

                    // DBList mode filters changed?
                    bool dbFilterChanged =
                        form._settings.MinEvaluation != prevMinEvaluation ||
                        form._settings.MinDisplayCount != prevMinDisplayCount ||
                        form._settings.MaxDisplayCount != prevMaxDisplayCount;

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
                            DisplayImagesCore(form, form._currentIndex);
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

        public static void ShowSettingsDialog(Form1 form)
        {
            ApplySettingsChanges(form);
            form.UpdateInfoLabelBase($"[設定] 反映済み: {Path.GetFileName(form._currentFolder)}", "settings:applied");
        }

        // ===== Display / InfoText helpers =====

        public static void DisplayImages(Form1 form, int startIndex)
        {
            form._displayManager.ImagePaths = form._imagePaths;
            form._displayManager.DisplayImages(startIndex);

            string infoText = BuildInfoText(form, startIndex);
            if (!string.IsNullOrEmpty(infoText))
                form.UpdateInfoLabelBase(infoText);
            else
                form.UpdateInfoLabelBase($"[{GetFolderDisplay(form)}] 表示可能な画像がありません。");

            // CBZ preload when near end
            if (form._cbzManager != null && form._imagePaths.Count > 0)
            {
                int remainingPages = form._imagePaths.Count - startIndex;
                if (remainingPages <= 16)
                {
                    System.Threading.Tasks.Task.Run(() => form._cbzManager.PreloadNextCbx());
                }
            }

            form.RememberCurrentPlaybackPosition();
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

        public static async void NavigateForward(Form1 form, int pageCount)
        {
            if (pageCount <= 0) return;

            int maxIndex = NavigationHandler.ComputeMaxPageIndex(
                form._imagePaths.Count, form._displayManager.DisplayCount);

            bool atEnd = form._imagePaths.Count == 0 || form._currentIndex >= maxIndex;

            // 巻末での前進は次巻への切替となり、CBZ展開I/Oが発生し得る。
            // UIスレッドを塞がないよう、境界遷移だけ非同期コマンドに委譲する。
            if (atEnd && form._cbzManager != null)
            {
                await CrossVolumeAsync(form, forward: true);
                return;
            }

            if (atEnd)
                return;

            form._currentIndex = NavigationHandler.ComputeForwardIndex(
                form._currentIndex, pageCount, form._imagePaths.Count, form._displayManager.DisplayCount);

            DisplayImages(form, form._currentIndex);
        }

        public static async void NavigateBackward(Form1 form, int pageCount)
        {
            if (pageCount <= 0) return;

            bool atStart = form._imagePaths.Count == 0 || form._currentIndex <= 0;

            // 巻頭での後退は前巻への切替となり、CBZ展開I/Oが発生し得る。
            // 境界遷移のみ非同期化し、UIスレッドの停止を防ぐ。
            if (atStart && form._cbzManager != null)
            {
                await CrossVolumeAsync(form, forward: false);
                return;
            }

            if (atStart)
                return;

            form._currentIndex = NavigationHandler.ComputeBackwardIndex(
                form._currentIndex, pageCount, form._imagePaths.Count, form._displayManager.DisplayCount);

            DisplayImages(form, form._currentIndex);
        }

        /// <summary>
        /// 巻境界（次巻/前巻）への遷移を非同期で実行する。
        /// - 重いCBZ展開はワーカースレッドで実行し、UIを塞がない。
        /// - 直列化ゲートで同時遷移を防ぎ、ActiveCbxIndex の競合を回避する。
        /// - 世代トークンで最新要求のみUI反映し、古い結果は破棄する。
        /// - 失敗・隣接巻なしの場合は現在の端で表示を維持する。
        /// </summary>
        private static async Task CrossVolumeAsync(Form1 form, bool forward)
        {
            var mgr = form._cbzManager;
            if (mgr == null) return;

            int generation = form.BeginCbzLoadGeneration();
            form.RememberCurrentPlaybackPosition();

            await form.VolumeTransitionGate.WaitAsync().ConfigureAwait(true);
            try
            {
                // ゲート取得までに新しい要求が来ていれば、この遷移は破棄する。
                if (!form.IsLatestCbzLoadGeneration(generation))
                {
                    StartupHandler.WriteStartupLog($"[NAV-CROSS] discarded(pre) gen={generation} forward={forward}");
                    return;
                }

                int beforeIndex = mgr.ActiveCbxIndex;
                List<string> paths;
                try
                {
                    paths = await Task.Run(() => forward
                        ? mgr.MoveToNextCbxIfEndReached()
                        : mgr.MoveToPreviousCbxIfAtStart()).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteErrorLog($"[FormNavigator] CrossVolume failed forward={forward}: {ex}");
                    return; // 失敗時は現ページ維持
                }

                if (!form.IsLatestCbzLoadGeneration(generation))
                {
                    StartupHandler.WriteStartupLog($"[NAV-CROSS] discarded(post) gen={generation} forward={forward}");
                    return;
                }

                bool advanced = mgr.ActiveCbxIndex != beforeIndex && paths.Count > 0;
                if (advanced)
                {
                    form._imagePaths = paths;
                    form._currentIndex = forward
                        ? 0
                        : NavigationHandler.ComputeMaxPageIndex(paths.Count, form._displayManager.DisplayCount);
                }
                else
                {
                    // 隣接巻がない場合は現在の端に留まる。
                    form._currentIndex = forward
                        ? NavigationHandler.ComputeMaxPageIndex(form._imagePaths.Count, form._displayManager.DisplayCount)
                        : 0;
                }

                StartupHandler.WriteStartupLog($"[NAV-CROSS] applied gen={generation} forward={forward} advanced={advanced} index={form._currentIndex}");
                DisplayImages(form, form._currentIndex);
            }
            finally
            {
                form.VolumeTransitionGate.Release();
            }
        }

        public static async void NavigateFolderBy(Form1 form, int delta)
        {
            if (form._folderList.Count == 0) return;
            int newIndex = form._currentFolderIndex + delta;
            newIndex = Math.Max(0, Math.Min(newIndex, form._folderList.Count - 1));
            if (newIndex == form._currentFolderIndex) return;

            form._currentFolderIndex = newIndex;
            try
            {
                if (!await form.LoadAndSortImagesAsync(form._folderList[newIndex]))
                    return;
            }
            catch (Exception ex)
            {
                StartupHandler.WriteErrorLog($"[FormNavigator] NavigateFolderBy failed: {ex}");
                return;
            }

            DisplayImagesCore(form, form._currentIndex);
            form.ScheduleNavigationCbzPreload();
            form.listBoxFolders.SetSelected(newIndex, true);
        }

        public static async void NavigateToNextUnrated(Form1 form)
        {
            if (form._folderList.Count == 0)
                return;

            int startIndex = form._currentFolderIndex >= 0
                ? form._currentFolderIndex + 1
                : 0;

            for (int i = startIndex; i < form._folderList.Count; i++)
            {
                if (RatingService.ReadRating(form._folderList[i]) == -1)
                {
                    form._currentFolderIndex = i;
                    try
                    {
                        if (!await form.LoadAndSortImagesAsync(form._folderList[i]))
                            return;
                    }
                    catch (Exception ex)
                    {
                        StartupHandler.WriteErrorLog($"[FormNavigator] NavigateToNextUnrated failed: {ex}");
                        return;
                    }

                    DisplayImagesCore(form, form._currentIndex);
                    form.ScheduleNavigationCbzPreload();
                    form.listBoxFolders.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>
        /// 指定フォルダの評価値を現在のDBList表示へ即時反映する。
        /// UI更新処理は RatingDisplayService に委譲する。
        /// </summary>
        public static void RefreshFolderRatingDisplay(Form1 form, string folderPath)
        {
            RatingDisplayService.RefreshFolderRatingDisplay(form, folderPath);
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

        public static async void NavigateCbzNext(Form1 form)
        {
            if (form._cbzManager == null || !NavigationHandler.CanNavigateCbx(form._cbzManager.CbxFiles.Count)) return;

            int beforeIndex = form._cbzManager.ActiveCbxIndex;
            StartupHandler.WriteStartupLog($"[NAV] action=NavigateCbzNext before={beforeIndex} count={form._cbzManager.CbxFiles.Count}");

            try
            {
                if (!await form.SwitchToNextCbxAsync())
                    return;
            }
            catch (Exception ex)
            {
                StartupHandler.WriteErrorLog($"[FormNavigator] NavigateCbzNext failed: {ex}");
                return;
            }

            StartupHandler.WriteStartupLog($"[NAV] after=NavigateCbzNext activeIndex={form._cbzManager.ActiveCbxIndex} file={Path.GetFileName(form._cbzManager.CbxFiles[form._cbzManager.ActiveCbxIndex])} imageCount={form._imagePaths.Count}");
            DisplayImagesCore(form, 0);
        }

        public static async void NavigateCbzPrev(Form1 form)
        {
            if (form._cbzManager == null || !NavigationHandler.CanNavigateCbx(form._cbzManager.CbxFiles.Count)) return;

            int beforeIndex = form._cbzManager.ActiveCbxIndex;
            StartupHandler.WriteStartupLog($"[NAV] action=NavigateCbzPrev before={beforeIndex} count={form._cbzManager.CbxFiles.Count}");

            try
            {
                if (!await form.SwitchToPreviousCbxAsync())
                    return;
            }
            catch (Exception ex)
            {
                StartupHandler.WriteErrorLog($"[FormNavigator] NavigateCbzPrev failed: {ex}");
                return;
            }

            StartupHandler.WriteStartupLog($"[NAV] after=NavigateCbzPrev activeIndex={form._cbzManager.ActiveCbxIndex} file={Path.GetFileName(form._cbzManager.CbxFiles[form._cbzManager.ActiveCbxIndex])} imageCount={form._imagePaths.Count}");
            DisplayImagesCore(form, 0);
        }

        public static void StartSlideshow(Form1 form)
        {
            UiBehaviorService.StartSlideshow(form);
        }

        public static void StopSlideshow(Form1 form)
        {
            UiBehaviorService.StopSlideshow(form);
        }

        public static void CopyCurrentNameToClipboard(Form1 form)
        {
            UiBehaviorService.CopyCurrentNameToClipboard(form);
        }

        // ===== FullScreen info-text refresh =====

        public static void RefreshFullScreenInfo(Form1 form)
        {
            UiBehaviorService.RefreshFullScreenInfo(form);
        }

        // ===== Internal helper (no public caller) =====

        private static void DisplayImagesCore(Form1 form, int index)
        {
            UiBehaviorService.DisplayImagesCore(form, index);
        }
    }
}
