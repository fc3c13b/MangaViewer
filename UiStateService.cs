using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    internal static class UiStateService
    {
        internal static void UpdateInfoLabelBase(Form1 form, string text, string source = "base")
        {
            form._baseInfoLabelText = text;
            RefreshInfoLabelText(form, source + ":set");
        }

        internal static void RefreshInfoLabelText(Form1 form, string source = "refresh")
        {
            if (form.labelInfo == null)
                return;

            // 上段は常にページ・フォルダ情報を表示する（キャッシュ状態で上書きしない）
            string nextText = form._baseInfoLabelText;

            if (!string.Equals(form._lastRenderedInfoLabelText, nextText, StringComparison.Ordinal))
            {
                form._lastRenderedInfoLabelText = nextText;
                StartupHandler.WriteStartupLog($"[INFO-LABEL] source={source} mode=base text={TrimForLog(nextText)}");
            }

            form.labelInfo.Text = nextText;
        }

        internal static void RefreshCacheStatusLabel(Form1 form)
        {
            if (form.labelCacheStatus == null)
                return;

            bool active = !string.IsNullOrEmpty(form._cacheStatusLabelText)
                          && DateTime.UtcNow < form._cacheStatusExpiresAtUtc;
            form.labelCacheStatus.Text = active ? form._cacheStatusLabelText! : "";
        }

        internal static void UpdateCacheStatusLabel(Form1 form, string cbzPath, string status)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return;

            form._cacheStatusLabelText = status;

            if (status.StartsWith("DL中:", StringComparison.Ordinal))
                form._cacheStatusExpiresAtUtc = DateTime.UtcNow.AddSeconds(5);
            else if (status.StartsWith("Cache読込中:", StringComparison.Ordinal) || status.StartsWith("Cache作成中:", StringComparison.Ordinal))
                form._cacheStatusExpiresAtUtc = DateTime.UtcNow.AddSeconds(3);
            else if (status.StartsWith("先読み確認(", StringComparison.Ordinal))
                form._cacheStatusExpiresAtUtc = DateTime.UtcNow.AddSeconds(1.6);
            else if (status.StartsWith("表示準備完了:", StringComparison.Ordinal))
                form._cacheStatusExpiresAtUtc = DateTime.UtcNow.AddSeconds(1.2);
            else
                form._cacheStatusExpiresAtUtc = DateTime.UtcNow.AddSeconds(2);

            EnsureCacheStatusTimer(form);
            RefreshCacheStatusLabel(form);
        }

        internal static void EnsureCacheStatusTimer(Form1 form)
        {
            if (form._cacheStatusTimer != null)
                return;

            form._cacheStatusTimer = new Timer { Interval = 200 };
            form._cacheStatusTimer.Tick += (s, e) =>
            {
                if (form.IsDisposed)
                    return;

                if (string.IsNullOrEmpty(form._cacheStatusLabelText))
                    return;

                if (DateTime.UtcNow >= form._cacheStatusExpiresAtUtc)
                {
                    form._cacheStatusLabelText = null;
                    form._cacheStatusExpiresAtUtc = DateTime.MinValue;
                    RefreshCacheStatusLabel(form);
                }
            };
            form._cacheStatusTimer.Start();
        }

        internal static void UpdateInfoLabelAfterToggle(Form1 form)
        {
            UpdateInfoLabelBase(form, BuildDisplayInfoText(form), "toggle");
        }

        internal static string BuildDisplayInfoText(Form1 form)
        {
            string folderDisplay = BuildFolderDisplayWithCbz(form);
            string infoText = form._displayManager.GetInfoText(form._currentFolder, form._currentFolderIndex, form._folderService!.entries);
            if (!string.IsNullOrEmpty(infoText))
                return $"[{folderDisplay}] {infoText.TrimStart()}";

            return $"[{folderDisplay}] 表示可能な画像がありません。";
        }

        internal static string BuildFolderDisplayWithCbz(Form1 form)
        {
            string folderName = Path.GetFileName(form._currentFolder);
            string cbzInfo = "";
            if (form._cbzManager != null && form._cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(form._cbzManager.ActiveCbxIndex, 0, form._cbzManager.CbxFiles.Count - 1);
                string name = Path.GetFileNameWithoutExtension(form._cbzManager.CbxFiles[idx]);
                if (FolderService.ExtractNumberFromFileName(name) > 0)
                    cbzInfo = $" [{name}]";
            }
            return $"{folderName}{cbzInfo}";
        }

        private static bool IsStatusForCurrentContext(Form1 form, string cbzPath)
        {
            if (form._cbzManager == null || form._cbzManager.CbxFiles.Count == 0)
                return IsPathUnderCurrentFolder(form, cbzPath);

            int idx = form._cbzManager.ActiveCbxIndex;
            if (idx < 0 || idx >= form._cbzManager.CbxFiles.Count)
                return IsPathUnderCurrentFolder(form, cbzPath);

            string? activePath = form._cbzManager.CbxFiles[idx];
            if (!string.IsNullOrWhiteSpace(activePath) && !string.IsNullOrWhiteSpace(cbzPath))
            {
                try
                {
                    return string.Equals(Path.GetFullPath(activePath), Path.GetFullPath(cbzPath), StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return true;
                }
            }

            return IsPathUnderCurrentFolder(form, cbzPath);
        }

        private static bool IsPathUnderCurrentFolder(Form1 form, string cbzPath)
        {
            if (string.IsNullOrWhiteSpace(cbzPath) || string.IsNullOrWhiteSpace(form._currentFolder))
                return true;

            try
            {
                string folder = Path.GetFullPath(form._currentFolder);
                if (!folder.EndsWith(Path.DirectorySeparatorChar))
                    folder += Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(cbzPath);
                return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        private static string GetActiveCbzFileNameForLog(Form1 form)
        {
            if (form._cbzManager == null || form._cbzManager.CbxFiles.Count == 0)
                return "<none>";

            int idx = form._cbzManager.ActiveCbxIndex;
            if (idx < 0 || idx >= form._cbzManager.CbxFiles.Count)
                return "<out-of-range>";

            return Path.GetFileName(form._cbzManager.CbxFiles[idx]);
        }

        private static string TrimForLog(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string singleLine = text.Replace("\r", " ").Replace("\n", " ");
            return singleLine.Length <= 160 ? singleLine : singleLine.Substring(0, 160) + "...";
        }
    }
}
