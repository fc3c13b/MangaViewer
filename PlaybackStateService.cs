using System;
using System.Collections.Generic;
using System.IO;

namespace MangaViewer
{
    internal static class PlaybackStateService
    {
        internal static string GetTitleKey(Form1 form)
        {
            return form._currentFolder ?? string.Empty;
        }

        internal static void EnsureHistoryLoaded(Form1 form)
        {
            PlaybackHistoryService.EnsureLoaded(form._settings);
        }

        internal static int GetRememberedIndex(Form1 form)
        {
            PlaybackHistoryService.EnsureLoaded(form._settings);
            string key = GetTitleKey(form);
            var entry = PlaybackHistoryService.Get(key);
            if (entry != null)
                return Math.Max(0, entry.LastImageIndex);

            // フォールバック: 旧 setting.json の値
            if (!string.IsNullOrWhiteSpace(key)
                && form._settings.LastViewedImageIndexByTitle.TryGetValue(key, out int savedIndex))
                return Math.Max(0, savedIndex);

            return Math.Max(0, form._settings.LastViewedImageIndex);
        }

        internal static void ApplyStartPosition(Form1 form)
        {
            if (!form._settings.StartFromLastViewedPosition)
            {
                form._currentIndex = 0;
                return;
            }

            string key = GetTitleKey(form);
            if (string.IsNullOrWhiteSpace(key))
            {
                form._currentIndex = 0;
                return;
            }

            PlaybackHistoryService.EnsureLoaded(form._settings);
            var entry = PlaybackHistoryService.Get(key);
            if (entry != null)
            {
                int maxIndex = Math.Max(0, form._imagePaths.Count - 1);
                form._currentIndex = Math.Clamp(Math.Max(0, entry.LastImageIndex), 0, maxIndex);
                return;
            }

            // フォールバック: 旧 setting.json の値
            if (form._settings.LastViewedImageIndexByTitle.TryGetValue(key, out int savedIndex))
            {
                int maxIndex = Math.Max(0, form._imagePaths.Count - 1);
                form._currentIndex = Math.Clamp(Math.Max(0, savedIndex), 0, maxIndex);
                return;
            }

            form._currentIndex = 0;
        }

        internal static void RememberCurrent(Form1 form)
        {
            string key = GetTitleKey(form);
            if (string.IsNullOrWhiteSpace(key))
                return;

            int index = Math.Max(0, form._currentIndex);
            int totalImages = form._imagePaths.Count;

            string lastCbzFile = string.Empty;
            int activeVolIndex = -1;

            if (form._cbzManager != null && form._cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(form._cbzManager.ActiveCbxIndex, 0, form._cbzManager.CbxFiles.Count - 1);
                lastCbzFile = form._cbzManager.CbxFiles[idx] ?? string.Empty;
                activeVolIndex = idx;
            }

            // playback-history.json に保存
            PlaybackHistoryService.EnsureLoaded(form._settings);
            PlaybackHistoryService.OnFolderLeave(key, index, lastCbzFile, activeVolIndex, totalImages);

            // setting.json 側も後方互換のため維持
            form._settings.LastViewedImageIndexByTitle[key] = index;
            form._settings.LastViewedFolderPath = form._currentFolder ?? string.Empty;
            form._settings.LastViewedCbzFile = lastCbzFile;
            form._settings.LastViewedCbzFileByTitle[key] = lastCbzFile;
            form._settings.LastViewedImageIndex = index;
            SettingsManager.Save(form._settings);
        }

        // ===== ページ表示時の既読記録 =====

        /// <summary>
        /// DisplayImages のたびに呼び出し、最大既読インデックスを更新する。
        /// </summary>
        internal static void OnPageDisplayed(Form1 form, int displayedIndex)
        {
            string key = GetTitleKey(form);
            if (string.IsNullOrWhiteSpace(key)) return;

            int activeVolIndex = -1;
            if (form._cbzManager != null && form._cbzManager.CbxFiles.Count > 0)
                activeVolIndex = Math.Clamp(form._cbzManager.ActiveCbxIndex, 0, form._cbzManager.CbxFiles.Count - 1);

            PlaybackHistoryService.EnsureLoaded(form._settings);
            PlaybackHistoryService.OnPageDisplayed(
                key,
                displayedIndex,
                form._settings.DisplayCount,
                form._imagePaths.Count,
                activeVolIndex);
        }
    }
}
