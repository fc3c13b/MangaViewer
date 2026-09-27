using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MangaViewer
{
    internal sealed class CbzCacheCoordinator
    {
        private const int MaxCachedCbzCountArea1 = 50;
        private const int MaxCachedCbzCountArea2 = 0;
        private const int PriorityCacheDirCount = MaxCachedCbzCountArea1;

        private readonly object _retentionLock = new();
        private readonly List<string> _priorityCacheDirs = new();

        internal string CacheRootArea1 { get; }
        internal string CacheRootArea2 { get; }

        internal CbzCacheCoordinator()
        {
            string primaryRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache"
            );
            string secondaryRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache_Area2"
            );

            try
            {
                Directory.CreateDirectory(primaryRoot);
                Directory.CreateDirectory(secondaryRoot);
                CacheRootArea1 = primaryRoot;
                CacheRootArea2 = secondaryRoot;
            }
            catch (UnauthorizedAccessException)
            {
                var tmp1 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp",
                    "MangaViewer_Cache"
                );
                var tmp2 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp",
                    "MangaViewer_Cache_Area2"
                );
                CacheRootArea1 = tmp1;
                CacheRootArea2 = tmp2;
                try { Directory.CreateDirectory(CacheRootArea1); } catch { }
                try { Directory.CreateDirectory(CacheRootArea2); } catch { }
            }
            catch
            {
                var tmp1 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp",
                    "MangaViewer_Cache"
                );
                var tmp2 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp",
                    "MangaViewer_Cache_Area2"
                );
                CacheRootArea1 = tmp1;
                CacheRootArea2 = tmp2;
                try { Directory.CreateDirectory(CacheRootArea1); } catch { }
                try { Directory.CreateDirectory(CacheRootArea2); } catch { }
            }
        }

        internal static void ClearAllCache()
        {
            var cacheRoot1 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache"
            );
            var cacheRoot2 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache_Area2"
            );

            if (Directory.Exists(cacheRoot1))
            {
                try
                {
                    int count = Directory.GetDirectories(cacheRoot1).Length;
                    Directory.Delete(cacheRoot1, recursive: true);
                    StartupHandler.WriteStartupLog($"[CACHE-CLEAR] area=area1 deletedDirs={count} root={cacheRoot1}");
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[CACHE-CLEAR] area=area1 failed root={cacheRoot1} error={ex.GetType().Name}");
                }
            }

            if (Directory.Exists(cacheRoot2))
            {
                try
                {
                    int count = Directory.GetDirectories(cacheRoot2).Length;
                    Directory.Delete(cacheRoot2, recursive: true);
                    StartupHandler.WriteStartupLog($"[CACHE-CLEAR] area=area2 deletedDirs={count} root={cacheRoot2}");
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[CACHE-CLEAR] area=area2 failed root={cacheRoot2} error={ex.GetType().Name}");
                }
            }
        }

        internal string GetArea1CacheDirectory(string cbzFile) =>
            CbzCacheHelper.GetArea1CacheDirectory(CacheRootArea1, cbzFile);

        internal string GetArea2CacheDirectory(string cbzFile) =>
            CbzCacheHelper.GetArea2CacheDirectory(CacheRootArea2, cbzFile);

        internal string GetPreferredCacheDirectory(string cbzFile) =>
            CbzCacheHelper.GetPreferredCacheDirectory(CacheRootArea1, cbzFile);

        internal bool IsCacheReady(string cacheDir) => CbzCacheHelper.IsCacheReady(cacheDir);

        internal bool TryResolveCacheArea(string cacheDir, out string rootPath, out int maxCount)
        {
            if (cacheDir.StartsWith(CacheRootArea1, StringComparison.OrdinalIgnoreCase))
            {
                rootPath = CacheRootArea1;
                maxCount = MaxCachedCbzCountArea1;
                return true;
            }

            if (cacheDir.StartsWith(CacheRootArea2, StringComparison.OrdinalIgnoreCase))
            {
                rootPath = CacheRootArea2;
                maxCount = MaxCachedCbzCountArea2;
                return true;
            }

            rootPath = string.Empty;
            maxCount = 0;
            return false;
        }

        internal void ProtectCacheDirectory(string cacheDir)
        {
            if (!TryResolveCacheArea(cacheDir, out var areaRoot, out var maxCount))
                return;

            lock (_retentionLock)
            {
                _priorityCacheDirs.RemoveAll(dir => string.Equals(dir, cacheDir, StringComparison.OrdinalIgnoreCase));
                _priorityCacheDirs.Add(cacheDir);

                int areaCount = _priorityCacheDirs.Count(dir => dir.StartsWith(areaRoot, StringComparison.OrdinalIgnoreCase));
                if (areaCount > maxCount)
                {
                    for (int i = 0; i < _priorityCacheDirs.Count && areaCount > maxCount;)
                    {
                        if (_priorityCacheDirs[i].StartsWith(areaRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            _priorityCacheDirs.RemoveAt(i);
                            areaCount--;
                        }
                        else
                        {
                            i++;
                        }
                    }
                }

                while (_priorityCacheDirs.Count > PriorityCacheDirCount + MaxCachedCbzCountArea2)
                    _priorityCacheDirs.RemoveAt(0);
            }
        }

        internal void TrimCacheDirectories(params string?[] protectedCacheDirs)
        {
            try
            {
                string? anchorCacheDir = protectedCacheDirs.FirstOrDefault(dir => !string.IsNullOrEmpty(dir));
                if (string.IsNullOrEmpty(anchorCacheDir))
                    return;

                if (!TryResolveCacheArea(anchorCacheDir, out var targetRoot, out var maxCount))
                    return;

                HashSet<string> retainedCacheDirs;
                lock (_retentionLock)
                {
                    retainedCacheDirs = new HashSet<string>(
                        _priorityCacheDirs.Where(dir => dir.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)),
                        StringComparer.OrdinalIgnoreCase);
                }
                foreach (var protectedDir in protectedCacheDirs)
                {
                    if (!string.IsNullOrEmpty(protectedDir) && protectedDir.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                        retainedCacheDirs.Add(protectedDir);
                }

                var cacheDirs = Directory.GetDirectories(targetRoot)
                    .Where(CbzCacheHelper.IsCacheReady)
                    .OrderBy(Directory.GetCreationTimeUtc)
                    .ToList();
                int remainingCount = cacheDirs.Count;
                var now = DateTime.UtcNow;
                int deletedCount = 0;
                int skippedProtected = 0;
                int skippedRecent = 0;
                int deleteFailed = 0;

                string area = string.Equals(targetRoot, CacheRootArea2, StringComparison.OrdinalIgnoreCase)
                    ? "area2"
                    : "area1";
                StartupHandler.WriteStartupLog($"[CACHE-TRIM] phase=start area={area} ready={cacheDirs.Count} max={maxCount} retained={retainedCacheDirs.Count}");

                foreach (var oldest in cacheDirs)
                {
                    if (remainingCount <= maxCount)
                        break;
                    if (retainedCacheDirs.Contains(oldest))
                    {
                        skippedProtected++;
                        continue;
                    }

                    try
                    {
                        var creationTime = Directory.GetCreationTimeUtc(oldest);
                        if ((now - creationTime).TotalSeconds < 30)
                        {
                            skippedRecent++;
                            continue;
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        Directory.Delete(oldest, recursive: true);
                        remainingCount--;
                        deletedCount++;
                        StartupHandler.WriteStartupLog($"[CACHE-DELETE] area={area} dir={oldest}");
                    }
                    catch (Exception ex)
                    {
                        deleteFailed++;
                        StartupHandler.WriteStartupLog($"[CACHE-DELETE] area={area} failed dir={oldest} error={ex.GetType().Name}");
                    }
                }

                StartupHandler.WriteStartupLog($"[CACHE-TRIM] phase=end area={area} deleted={deletedCount} skippedProtected={skippedProtected} skippedRecent={skippedRecent} deleteFailed={deleteFailed} remaining={remainingCount}");
            }
            catch
            {
            }
        }
    }
}
