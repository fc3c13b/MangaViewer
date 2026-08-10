using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MangaViewer
{
    /// <summary>
    /// Manages CBZ (ZIP) files: extraction to temp cache, ordering by filename,
    /// and switching to the next/previous CBZ when current is exhausted/reached start.
    /// </summary>
    public class CbzManager : IDisposable
    {
        private readonly string _cacheRoot;

        // Current folder being viewed
        internal List<string> CbxFiles = new List<string>();
        internal int ActiveCbxIndex = 0;

        // Extracted paths for current active CBZ (kept in memory)
        internal List<string> CurrentImagePaths = new List<string>();

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        public CbzManager()
        {
            // Default cache root: AppData/Local/MangaViewer/CBZCache
            _cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache"
            );
            Directory.CreateDirectory(_cacheRoot);
        }

        /// <summary>
        /// Initialize for a given folder: if there are .cbz files, sort by name.
        /// Returns true if CBZ mode is active.
        /// </summary>
        public bool InitializeForFolder(string folderPath)
        {
            Reset();

            var cbzs = Directory.GetFiles(folderPath, "*.cbz", SearchOption.TopDirectoryOnly)
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            if (!cbzs.Any())
                return false;

            CbxFiles = cbzs;
            ActiveCbxIndex = 0;
            RefreshCurrentImagePaths(extractEvenIfEmpty: true);
            return CurrentImagePaths.Any();
        }

        /// <summary>
        /// Get all image paths for the current active CBZ.
        /// Extracts if needed.
        /// </summary>
        public List<string> GetCurrentImagePaths()
        {
            RefreshCurrentImagePaths(extractEvenIfEmpty: false);
            return CurrentImagePaths;
        }

        /// <summary>
        /// When user reaches the end of current images, try to switch to next CBZ.
        /// Returns new image list (possibly same as before if no more).
        /// </summary>
        public List<string> MoveToNextCbxIfEndReached()
        {
            if (CbxFiles.Count <= 1) return CurrentImagePaths;

            ActiveCbxIndex++;
            if (ActiveCbxIndex >= CbxFiles.Count)
            {
                // No more CBZs; revert back to last one.
                ActiveCbxIndex = CbxFiles.Count - 1;
                RefreshCurrentImagePaths(extractEvenIfEmpty: false);
                return CurrentImagePaths;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: false);
            return CurrentImagePaths;
        }

        /// <summary>
        /// When user reaches the start of current images, try to switch to previous CBZ.
        /// Returns new image list (possibly same as before if already at first).
        /// </summary>
        public List<string> MoveToPreviousCbxIfAtStart()
        {
            if (CbxFiles.Count <= 1) return CurrentImagePaths;

            ActiveCbxIndex--;
            if (ActiveCbxIndex < 0)
            {
                // Already at first CBZ; revert.
                ActiveCbxIndex = 0;
                RefreshCurrentImagePaths(extractEvenIfEmpty: false);
                return CurrentImagePaths;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: false);
            return CurrentImagePaths;
        }

        private void Reset()
        {
            // Do not aggressively delete cache; keep for performance.
            CbxFiles.Clear();
            ActiveCbxIndex = 0;
            CurrentImagePaths.Clear();
        }

        private void RefreshCurrentImagePaths(bool extractEvenIfEmpty)
        {
            if (!CbxFiles.Any() || ActiveCbxIndex < 0 || ActiveCbxIndex >= CbxFiles.Count)
            {
                CurrentImagePaths.Clear();
                return;
            }

            var cbzFile = CbxFiles[ActiveCbxIndex];
            var cacheDir = GetCacheDirectory(cbzFile);

            // If cache does not exist, extract from CBZ.
            if (!Directory.Exists(cacheDir))
            {
                ExtractCbzTo(cacheDir, cbzFile);
            }

            // Scan images from cache directory.
            if (Directory.Exists(cacheDir))
            {
                var images = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
                    .Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLower()))
                    .OrderBy(p => Path.GetFileName(p))
                    .ToList();

                CurrentImagePaths = images;
            }
            else
            {
                // Extraction failed or directory unavailable.
                CurrentImagePaths.Clear();
            }
        }

        private string GetCacheDirectory(string cbzFile)
        {
            // Use a stable, unique folder name based on the CBZ absolute path.
            var hash = System.Security.Cryptography.MD5.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(cbzFile));
            var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLower();
            return Path.Combine(_cacheRoot, hashStr);
        }

        private static void ExtractCbzTo(string cacheDir, string cbzFile)
        {
            Directory.CreateDirectory(cacheDir);

            try
            {
                using var archive = ZipFile.OpenRead(cbzFile);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var dest = Path.Combine(cacheDir, entry.FullName);

                    // Security: prevent path traversal.
                    if (!Path.GetFullPath(dest).StartsWith(Path.GetFullPath(cacheDir), StringComparison.Ordinal))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    try
                    {
                        entry.ExtractToFile(dest, overwrite: true);
                    }
                    catch
                    {
                        // Skip problematic entries (e.g., directories or invalid files).
                    }
                }
            }
            catch
            {
                // If not a valid ZIP, directory will remain empty and treated as no images.
            }
        }

        public void Dispose()
        {
            // Optionally: cleanup cache here in the future.
            CbxFiles.Clear();
            CurrentImagePaths.Clear();
        }
    }
}