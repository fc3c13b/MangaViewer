using System;
using System.IO;
using System.Linq;

namespace MangaViewer
{
    /// <summary>
    /// Encapsulates image loading + CBZ fallback logic (extracted from Form1).
    /// </summary>
    public static class ImageLoader
    {
        /// <summary>
        /// Load and sort images for the given folder. If no images, try CBZ as fallback.
        /// This mirrors the previous behavior in Form1.LoadAndSortImages exactly.
        /// </summary>
        public static void Load(Form1 form)
        {
            string folderPath = form._currentFolder;

            if (form._folderService == null)
                form._folderService = new FolderService(form._settings);

            form._imagePaths = form._folderService.LoadAndSortImages(folderPath);

            // If no images found, try CBZ via CbzManager as fallback
            if ((form._imagePaths == null || form._imagePaths.Count == 0) && Directory.Exists(folderPath))
            {
                string[] cbzFiles;
                try
                {
                    cbzFiles = Directory.GetFiles(folderPath, "*.cbz", SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException)
                {
                    // Permission issue: skip CBZ scan
                    cbzFiles = Array.Empty<string>();
                }
                catch
                {
                    // Other errors: also skip to avoid crashes
                    cbzFiles = Array.Empty<string>();
                }

                if (cbzFiles.Length > 0)
                {
                    if (form._cbzManager == null)
                        form._cbzManager = new CbzManager();

                    bool ok = form._cbzManager.InitializeForFolder(folderPath);

                    if (ok && form._cbzManager.CurrentImagePaths != null && form._cbzManager.CurrentImagePaths.Count > 0)
                    {
                        form._imagePaths = form._cbzManager.CurrentImagePaths;
                    }
                }
            }

            // Ensure DisplayManager is in sync.
            form._displayManager.ImagePaths = form._imagePaths;
        }
    }
}
