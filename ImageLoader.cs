using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MangaViewer
{
    /// <summary>
    /// Encapsulates image loading + CBZ fallback logic (extracted from Form1).
    /// </summary>
    public static class ImageLoader
    {
        private const string DebugLogFile = "/tmp/MangaViewer_debug.log";

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        /// <summary>
        /// Load and sort images for the given folder.
        /// Now supports mixing direct images + CBZ contents as one unified list (TASK09.30).
        /// </summary>
        public static void Load(Form1 form)
        {
            string folderPath = form._currentFolder;

            if (form._folderService == null)
                form._folderService = new FolderService(form._settings);

            // 1) Get direct images in the folder
            List<string> baseImages = form._folderService.LoadAndSortImages(folderPath);
            if (baseImages == null) baseImages = new List<string>();

            // 2) Collect CBZ-based images (all volumes, in order)
            List<string> cbzImages = GetCbzImagesForFolder(form, folderPath);

            // 3) Build unified list: direct images first, then CBZ contents
            form._imagePaths = new List<string>(baseImages.Count + cbzImages.Count);
            form._imagePaths.AddRange(baseImages);
            form._imagePaths.AddRange(cbzImages);

            // Ensure DisplayManager is in sync.
            form._displayManager.ImagePaths = form._imagePaths;

            Log($"[ImageLoader] {folderPath}: baseImages={baseImages.Count} cbzImages={cbzImages.Count} total={form._imagePaths.Count}");
        }

        private static List<string> GetCbzImagesForFolder(Form1 form, string folderPath)
        {
            Log($"[CBZ] Start: {folderPath}");

            if (!Directory.Exists(folderPath))
            {
                Log($"[CBZ] Folder does not exist: {folderPath}");
                return new List<string>();
            }

            // Scan for .cbz files in the folder.
            string[] cbzFiles;
            try
            {
                cbzFiles = Directory.GetFiles(folderPath, "*.cbz", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                Log($"[CBZ] Access denied: {folderPath}");
                return new List<string>();
            }
            catch (Exception ex)
            {
                Log($"[CBZ] Scan error: {folderPath} -> {ex.GetType().Name}: {ex.Message}");
                return new List<string>();
            }

            Log($"[CBZ] Found {cbzFiles.Length} CBZ files on disk");

            if (cbzFiles.Length == 0)
            {
                Log($"[CBZ] No CBZ files found");
                return new List<string>();
            }

            // Initialize or reuse CbzManager for this folder.
            if (form._cbzManager == null)
                form._cbzManager = new CbzManager();

            Log($"[CBZ] Calling InitializeForFolder...");
            bool ok = form._cbzManager.InitializeForFolder(folderPath);
            Log($"[CBZ] InitializeForFolder: ok={ok} CbxFiles.Count={form._cbzManager.CbxFiles.Count}");

            if (!ok || !form._cbzManager.CbxFiles.Any())
            {
                Log($"[CBZ] Skip: ok={ok} cbxAny={form._cbzManager.CbxFiles.Any()}");
                return new List<string>();
            }

            // Collect all images from all CBZ volumes in order.
            var result = new List<string>();

            for (int i = 0; i < form._cbzManager.CbxFiles.Count; i++)
            {
                var cbx = form._cbzManager.CbxFiles[i];
                form._cbzManager.SwitchToCbx(i);
                int imgCount = form._cbzManager.CurrentImagePaths?.Count ?? 0;
                Log($"[CBZ] Vol {i} ({cbx}): images={imgCount}");
                if (form._cbzManager.CurrentImagePaths != null)
                {
                    result.AddRange(form._cbzManager.CurrentImagePaths);
                }
            }

            // Reset to first CBZ for normal navigation.
            form._cbzManager.SwitchToCbx(0);

            Log($"[CBZ] Done: total={result.Count}");
            return result;
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(DebugLogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
                // Ignore logging failures.
            }
        }
    }
}