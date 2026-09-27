using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    internal static class UiBehaviorService
    {
        public static void StartSlideshow(Form1 form)
        {
            if (form._slideshowTimer == null)
            {
                form._slideshowTimer = new Timer
                {
                    Interval = Math.Max(250, form._settings.SlideshowIntervalMs)
                };

                form._slideshowTimer.Tick += (s, e) =>
                {
                    if (form.IsDisposed)
                    {
                        StopSlideshow(form);
                        return;
                    }

                    if (form._imagePaths.Count == 0)
                    {
                        StopSlideshow(form);
                        form.UpdateInfoLabelBase("[スライドショー] 表示画像がありません。", "slideshow:no-images");
                        return;
                    }

                    int nextIndex = form._currentIndex + form._displayManager.NavigationStep;
                    if (nextIndex >= form._imagePaths.Count)
                    {
                        if (form._currentFolderIndex + 1 < form._folderList.Count)
                        {
                            form._currentFolderIndex++;
                            form.listBoxFolders.SelectedIndex = form._currentFolderIndex;
                            return;
                        }

                        StopSlideshow(form);
                        form.UpdateInfoLabelBase("[スライドショー] 終了", "slideshow:end");
                        return;
                    }

                    form._currentIndex = nextIndex;
                    form.DisplayImages(form._currentIndex);
                };
            }

            form._slideshowTimer.Interval = Math.Max(250, form._settings.SlideshowIntervalMs);
            form._slideshowTimer.Start();
            form.UpdateInfoLabelBase($"[スライドショー] 開始 ({form._settings.SlideshowIntervalMs}ms)", "slideshow:start");
        }

        public static void StopSlideshow(Form1 form)
        {
            form._slideshowTimer?.Stop();
            form.UpdateInfoLabelBase("[スライドショー] 停止", "slideshow:stop");
        }

        public static void CopyCurrentNameToClipboard(Form1 form)
        {
            string value = form._imagePaths.Count > 0 && form._currentIndex >= 0 && form._currentIndex < form._imagePaths.Count
                ? Path.GetFileName(form._imagePaths[form._currentIndex])
                : Path.GetFileName(form._currentFolder);

            if (string.IsNullOrWhiteSpace(value))
                value = form._currentFolder;

            try
            {
                Clipboard.SetText(value);
                form.UpdateInfoLabelBase($"[クリップボード] {value} をコピーしました。", "clipboard:copy");
            }
            catch (Exception ex)
            {
                form.UpdateInfoLabelBase($"[クリップボード] コピー失敗: {ex.GetType().Name}", "clipboard:error");
            }
        }

        public static void RefreshFullScreenInfo(Form1 form)
        {
            string infoText = FormNavigator.BuildInfoText(form, form._currentIndex);
            if (!string.IsNullOrEmpty(infoText))
                form.UpdateInfoLabelBase(infoText, "navigator:refresh-fullscreen");
            else
                form.UpdateInfoLabelBase($"[{FormNavigatorBuildFolderDisplay(form)}] 表示可能な画像がありません。", "navigator:refresh-fullscreen");

            form.UpdateLayout();
        }

        internal static void DisplayImagesCore(Form1 form, int index)
        {
            form._displayManager.ImagePaths = form._imagePaths;
            form._displayManager.DisplayImages(index);
        }

        private static string FormNavigatorBuildFolderDisplay(Form1 form)
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
    }
}
