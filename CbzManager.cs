using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MangaViewer
{
    public class CbzManager : IDisposable
    {
        private readonly string _cacheRoot;

        /// <summary>キャッシュに保持する最大CBZ数</summary>
        private const int MaxCachedCbzCount = 2;

        internal List<string> CbxFiles = new();
        internal int ActiveCbxIndex = 0;
        internal List<string> CurrentImagePaths = new();

        /// <summary>LRU管理用：最近アクセスしたCBZのキャッシュディレクトリリスト（先頭=最新）</summary>
        private readonly List<string> _activeCacheDirs = new();

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        public CbzManager()
        {
            _cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache"
            );

            try
            {
                Directory.CreateDirectory(_cacheRoot);
            }
            catch (UnauthorizedAccessException)
            {
                // キャッシュフォルダ書き込み不可→一時的に temp を使う
                var tmp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp",
                    "MangaViewer_Cache"
                );
                _cacheRoot = tmp;
                try { Directory.CreateDirectory(_cacheRoot); } catch { /* ignore */ }
            }
            catch
            {
                // その他エラー→一時的に temp に切り替え
                var tmp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp",
                    "MangaViewer_Cache"
                );
                _cacheRoot = tmp;
                try { Directory.CreateDirectory(_cacheRoot); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// アプリ起動時に全てのCBZ展開キャッシュを削除します。
        /// </summary>
        public static void ClearAllCache()
        {
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache"
            );

            if (Directory.Exists(cacheRoot))
            {
                try
                {
                    Directory.Delete(cacheRoot, recursive: true);
                }
                catch
                {
                    // 削除失敗は許容（一部使用中など）
                }
            }
        }

        /// <summary>
        /// Initialize for a folder with .cbz files. Returns true if CBZ mode is active.
        /// </summary>
        public bool InitializeForFolder(string folderPath)
        {
            Reset();

            try
            {
                if (!Directory.Exists(folderPath))
                    return false;

                var cbzs = Directory.GetFiles(folderPath, "*.cbz", SearchOption.TopDirectoryOnly)
                                    .OrderBy(Path.GetFileName)
                                    .ToList();

                if (cbzs.Count == 0)
                    return false;

                CbxFiles = cbzs;
                ActiveCbxIndex = 0;
                RefreshCurrentImagePaths(extractEvenIfEmpty: true);

                return CurrentImagePaths.Any();
            }
            catch
            {
                // If we can't read folder or scan files, treat as no CBZ.
                Reset();
                return false;
            }
        }

        /// <summary>
        /// Public accessor for current image paths (used by Form1).
        /// </summary>
        public List<string> GetCurrentImagePaths()
        {
            RefreshCurrentImagePaths(extractEvenIfEmpty: false);
            return CurrentImagePaths;
        }

        /// <summary>
        /// When user reaches the end of current images, try to switch to next CBZ.
        /// </summary>
        public List<string> MoveToNextCbxIfEndReached()
        {
            if (CbxFiles.Count <= 1) return CurrentImagePaths;

            ActiveCbxIndex++;
            if (ActiveCbxIndex >= CbxFiles.Count)
            {
                ActiveCbxIndex = CbxFiles.Count - 1;
                RefreshCurrentImagePaths(extractEvenIfEmpty: false);
                return CurrentImagePaths;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: false);
            return CurrentImagePaths;
        }

        /// <summary>
        /// When user reaches the start of current images, try to switch to previous CBZ.
        /// </summary>
        public List<string> MoveToPreviousCbxIfAtStart()
        {
            if (CbxFiles.Count <= 1) return CurrentImagePaths;

            ActiveCbxIndex--;
            if (ActiveCbxIndex < 0)
            {
                ActiveCbxIndex = 0;
                RefreshCurrentImagePaths(extractEvenIfEmpty: false);
                return CurrentImagePaths;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: false);
            return CurrentImagePaths;
        }

        private void Reset()
        {
            CbxFiles.Clear();
            ActiveCbxIndex = 0;
            CurrentImagePaths.Clear();
        }

        private void RefreshCurrentImagePaths(bool extractEvenIfEmpty)
        {
            CurrentImagePaths.Clear();

            if (!CbxFiles.Any())
            {
                return;
            }

            if (ActiveCbxIndex < 0 || ActiveCbxIndex >= CbxFiles.Count)
            {
                // Clamp index
                ActiveCbxIndex = Math.Clamp(ActiveCbxIndex, 0, CbxFiles.Count - 1);
            }

            var cbzFile = CbxFiles[ActiveCbxIndex];
            string cacheDir;

            try
            {
                cacheDir = GetCacheDirectory(cbzFile);
            }
            catch (UnauthorizedAccessException)
            {
                CurrentImagePaths.Clear();
                return;
            }
            catch
            {
                CurrentImagePaths.Clear();
                return;
            }

            if (!Directory.Exists(cacheDir))
            {
                try
                {
                    ExtractCbzTo(cacheDir, cbzFile);
                }
                catch (UnauthorizedAccessException)
                {
                    // 展開不可→空扱い
                    CurrentImagePaths.Clear();
                    return;
                }
                catch
                {
                    // その他エラー→空扱い
                    CurrentImagePaths.Clear();
                    return;
                }
            }

            if (!Directory.Exists(cacheDir))
            {
                CurrentImagePaths.Clear();
                return;
            }

            // LRU管理：このCBZのキャッシュディレクトリを「最新アクセス」として登録
            _activeCacheDirs.Remove(cacheDir);
            _activeCacheDirs.Insert(0, cacheDir);

            // 最大保持数を超える場合、古いキャッシュを削除
            while (_activeCacheDirs.Count > MaxCachedCbzCount)
            {
                var oldest = _activeCacheDirs[_activeCacheDirs.Count - 1];
                _activeCacheDirs.RemoveAt(_activeCacheDirs.Count - 1);
                try
                {
                    if (Directory.Exists(oldest))
                        Directory.Delete(oldest, recursive: true);
                }
                catch
                {
                    // 削除失敗は許容（使用中など）
                }
            }

            try
            {
                CurrentImagePaths = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
                                              .Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLower()))
                                              .OrderBy(p => Path.GetFileName(p))
                                              .ToList();
            }
            catch (UnauthorizedAccessException)
            {
                // 権限不足→このCBZは表示不可として扱う
                CurrentImagePaths.Clear();
            }
            catch
            {
                // その他エラー→空扱い
                CurrentImagePaths.Clear();
            }
        }

        private string GetCacheDirectory(string cbzFile)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(cbzFile));
            var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLower();
            return Path.Combine(_cacheRoot, hashStr);
        }

        private static void ExtractCbzTo(string cacheDir, string cbzFile)
        {
            try
            {
                Directory.CreateDirectory(cacheDir);
            }
            catch (UnauthorizedAccessException)
            {
                // 書き込み不可→上位でキャッチするようにそのまま投げる
                throw;
            }

            try
            {
                using var archive = ZipFile.OpenRead(cbzFile);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var dest = Path.Combine(cacheDir, entry.FullName);

                    // Prevent path traversal
                    if (!Path.GetFullPath(dest).StartsWith(Path.GetFullPath(cacheDir), StringComparison.Ordinal))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    try
                    {
                        entry.ExtractToFile(dest, overwrite: true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // そのエントリはスキップ
                    }
                    catch
                    {
                        // Skip problematic entries
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 展開不可→上位で扱うためそのまま投げる
                throw;
            }
            catch
            {
                // Not a valid ZIP; leave cacheDir empty.
            }
        }

        public void Dispose()
        {
            CbxFiles.Clear();
            CurrentImagePaths.Clear();
        }
    }
}