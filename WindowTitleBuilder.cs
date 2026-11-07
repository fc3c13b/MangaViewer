using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MangaViewer
{
    /// <summary>
    /// Builds the window title string including CBZ cache status indicators.
    /// Extracted from Form1 (TASK09.26 Phase A).
    /// </summary>
    public static class WindowTitleBuilder
    {
        private static readonly System.Windows.Forms.Timer s_timer = new() { Interval = 100 };

        public static void ScheduleUpdate(Form1 form)
        {
            if (form.IsDisposed)
                return;

            s_timer.Tick -= OnTimerTick;
            s_timer.Tick += OnTimerTick;

            void OnTimerTick(object? sender, EventArgs e)
            {
                s_timer.Stop();
                if (!form.IsDisposed)
                    Update(form);
            }

            s_timer.Stop();
            s_timer.Start();
        }

        public static void Update(Form1 form)
        {
            string baseTitle = Constants.AppTitle;

            string titleBase;
            if (!string.IsNullOrEmpty(form._activeDbFile))
            {
                string dbName = Path.GetFileName(form._activeDbFile);
                titleBase = $"{baseTitle} - DBList [{dbName}]";
            }
            else
            {
                string label = string.IsNullOrWhiteSpace(form.RootFolder)
                    ? "[<none>]"
                    : $"[{form.RootFolder}]";
                titleBase = $"{baseTitle} - DBList {label}";
            }

            string cbzList = BuildCbzCacheList(form);
            form.Text = string.IsNullOrEmpty(cbzList) ? titleBase : $"{titleBase} {cbzList}";
        }

        private static string BuildCbzCacheList(Form1 form)
        {
            try
            {
                if (form._folderList == null || form._folderList.Count == 0 ||
                    form._currentFolderIndex < 0 || form._currentFolderIndex >= form._folderList.Count)
                    return "";

                string currentFolder = form._folderList[form._currentFolderIndex];

                List<string> cbzFiles;
                if (form._cbzManager != null &&
                    string.Equals(form._cbzManager.CurrentFolder, currentFolder, StringComparison.OrdinalIgnoreCase) &&
                    form._cbzManager.CbxFiles.Count > 0)
                {
                    cbzFiles = form._cbzManager.CbxFiles;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(currentFolder) || !Directory.Exists(currentFolder))
                        return "";

                    cbzFiles = Directory.GetFiles(currentFolder, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(ArchiveExtensions.IsArchive)
                        .ToList();
                }

                if (cbzFiles.Count == 0)
                    return "";

                string? area1Root = CbzManager.GetCacheSearchRoots().FirstOrDefault();
                if (string.IsNullOrEmpty(area1Root))
                    return "";

                var cachedList = new List<string>();

                foreach (var cbz in cbzFiles)
                {
                    try
                    {
                        using var md5 = System.Security.Cryptography.MD5.Create();
                        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cbz));
                        var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        string cachePath = Path.Combine(area1Root, hashStr + ".cache");

                        if (File.Exists(cachePath))
                            cachedList.Add(Path.GetFileNameWithoutExtension(cbz));
                    }
                    catch { /* ignore per-file errors */ }
                }

                return cachedList.Count > 0 ? "[CBZ:" + string.Join(",", cachedList) + "]" : "";
            }
            catch { return ""; }
        }
    }
}
