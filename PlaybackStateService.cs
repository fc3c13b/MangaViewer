using System;
using System.IO;

namespace MangaViewer
{
    internal static class PlaybackStateService
    {
        internal static string GetTitleKey(Form1 form)
        {
            return form._currentFolder ?? string.Empty;
        }

        internal static int GetRememberedIndex(Form1 form)
        {
            string key = GetTitleKey(form);
            if (!string.IsNullOrWhiteSpace(key) && form._settings.LastViewedImageIndexByTitle.TryGetValue(key, out int savedIndex))
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
            form._settings.LastViewedImageIndexByTitle[key] = index;
            form._settings.LastViewedFolderPath = form._currentFolder ?? string.Empty;

            if (form._cbzManager != null && form._cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(form._cbzManager.ActiveCbxIndex, 0, form._cbzManager.CbxFiles.Count - 1);
                form._settings.LastViewedCbzFile = form._cbzManager.CbxFiles[idx] ?? string.Empty;
                form._settings.LastViewedCbzFileByTitle[key] = form._settings.LastViewedCbzFile;
            }
            else
            {
                form._settings.LastViewedCbzFile = string.Empty;
                form._settings.LastViewedCbzFileByTitle[key] = string.Empty;
            }

            form._settings.LastViewedImageIndex = index;
            SettingsManager.Save(form._settings);
        }
    }
}
