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
            List<string>? completedVols = null;

            if (form._cbzManager != null && form._cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(form._cbzManager.ActiveCbxIndex, 0, form._cbzManager.CbxFiles.Count - 1);
                lastCbzFile = form._cbzManager.CbxFiles[idx] ?? string.Empty;
                completedVols = CollectCompletedVols(form);
            }

            // playback-history.json に保存
            PlaybackHistoryService.EnsureLoaded(form._settings);
            PlaybackHistoryService.OnFolderLeave(key, index, lastCbzFile, completedVols, totalImages);

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

            PlaybackHistoryService.EnsureLoaded(form._settings);
            PlaybackHistoryService.OnPageDisplayed(
                key,
                displayedIndex,
                form._settings.DisplayCount,
                form._imagePaths.Count);
        }

        // ===== ヘルパー =====

        private static List<string>? CollectCompletedVols(Form1 form)
        {
            if (form._cbzManager == null || form._cbzManager.CbxFiles.Count == 0)
                return null;

            // 現在表示中の巻が末尾ページ到達済みかを maxReachedIndex で判定する
            // ここでは簡易実装：最後にいた巻の「末尾画像インデックス」と比較する
            string key = GetTitleKey(form);
            var entry = PlaybackHistoryService.Get(key);
            if (entry == null) return null;

            var completedVols = new List<string>();
            foreach (var cbzPath in form._cbzManager.CbxFiles)
            {
                string fileName = Path.GetFileName(cbzPath);
                if (entry.CompletedVols != null
                    && entry.CompletedVols.Exists(v => string.Equals(v, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    completedVols.Add(fileName);
                }
            }

            // アクティブ巻が totalImages 到達済みなら既読巻に追加
            int activeIdx = Math.Clamp(form._cbzManager.ActiveCbxIndex, 0, form._cbzManager.CbxFiles.Count - 1);
            string activeName = Path.GetFileName(form._cbzManager.CbxFiles[activeIdx]);
            if (entry.TotalImages > 0 && entry.MaxReachedIndex >= entry.TotalImages - 1
                && !completedVols.Exists(v => string.Equals(v, activeName, StringComparison.OrdinalIgnoreCase)))
                completedVols.Add(activeName);

            return completedVols.Count > 0 ? completedVols : null;
        }
    }
}
