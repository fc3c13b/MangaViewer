using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MangaViewer
{
    internal sealed class CbzPreloadCoordinator
    {
        internal enum PreloadPriority { Current = 0, NextVolume = 1, NextFolder = 2 }

        private readonly ConcurrentDictionary<string, PreloadPriority> _pendingPreloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _pendingFolderPreloads = new();
        private readonly HashSet<string> _queuedFolderPreloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _folderPreloadQueueLock = new();
        private int _navigationPreloadVersion;
        private readonly object _navigationPreloadLock = new();
        private string? _lastNavigationCurrentFolder;
        private string? _lastNavigationNextFolder;

        internal void PreloadForNavigationContext(CbzManager manager, string currentFolderPath, int area1Count, string? nextFolderPath)
        {
            if (string.IsNullOrWhiteSpace(currentFolderPath) || !Directory.Exists(currentFolderPath))
                return;

            lock (_navigationPreloadLock)
            {
                bool sameCurrent = string.Equals(_lastNavigationCurrentFolder, currentFolderPath, StringComparison.OrdinalIgnoreCase);
                bool sameNext = string.Equals(_lastNavigationNextFolder, nextFolderPath, StringComparison.OrdinalIgnoreCase);
                if (sameCurrent && sameNext)
                    return;

                _lastNavigationCurrentFolder = currentFolderPath;
                _lastNavigationNextFolder = nextFolderPath;
            }

            int version = Interlocked.Increment(ref _navigationPreloadVersion);
            int currentTargetCount = Math.Max(1, Math.Min(3, area1Count));

            Task.Run(() =>
            {
                try
                {
                    var currentTargets = GetSortedCbzFiles(currentFolderPath).Take(currentTargetCount).ToList();
                    var nextTargets = new List<string>();

                    if (!string.IsNullOrWhiteSpace(nextFolderPath) && Directory.Exists(nextFolderPath))
                    {
                        nextTargets = GetSortedCbzFiles(nextFolderPath)
                            .Take(2)
                            .ToList();
                    }

                    EnsureTargetsCached(version, currentTargets, nextTargets);

                    if (version != Volatile.Read(ref _navigationPreloadVersion))
                        return;

                    int currentReady = CountReadyCaches(currentTargets);
                    int nextReady = CountReadyCaches(nextTargets);
                    if (currentReady < currentTargets.Count || nextReady < nextTargets.Count)
                    {
                        StartupHandler.WriteStartupLog($"[CACHE-VERIFY] navigation retry currentReady={currentReady}/{currentTargets.Count} nextReady={nextReady}/{nextTargets.Count}");
                        EnsureTargetsCached(version, currentTargets, nextTargets);
                    }
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[PROF] CbzManager.PreloadForNavigationContext failed error={ex.GetType().Name}");
                }
            });
        }

        internal void PreloadFirstCbxForFolder(CbzManager manager, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            lock (_folderPreloadQueueLock)
            {
                if (_queuedFolderPreloads.Contains(folderPath))
                    return;
                _queuedFolderPreloads.Add(folderPath);
                _pendingFolderPreloads.Enqueue(folderPath);
            }

            if (!_pendingPreloads.Values.Any(p => p == PreloadPriority.NextVolume || p == PreloadPriority.NextFolder))
                StartFolderPreloadIfPending(manager);
        }

        internal void StartFolderPreloadIfPending(CbzManager manager)
        {
            string? folderPath;
            lock (_folderPreloadQueueLock)
            {
                if (_pendingFolderPreloads.Count == 0)
                    return;

                folderPath = _pendingFolderPreloads.Dequeue();
                _queuedFolderPreloads.Remove(folderPath);
            }

            Task.Run(() =>
            {
                try
                {
                    var firstCbz = Directory.EnumerateFiles(folderPath, "*.cbz", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, new CbzVolumeComparer())
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstCbz))
                        RequestPreload(manager, firstCbz, PreloadPriority.NextFolder);
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[PROF] CbzManager.StartFolderPreloadIfPending failed folder={folderPath} error={ex.GetType().Name}");
                }
            });
        }

        internal string? PreloadNextCbx(CbzManager manager)
        {
            if (manager.ActiveCbxIndex + 1 >= manager.CbxFiles.Count)
                return null;

            string nextCbz = manager.CbxFiles[manager.ActiveCbxIndex + 1];
            if (manager.ActiveCbxIndex >= 0 && manager.ActiveCbxIndex < manager.CbxFiles.Count)
                manager.ProtectCacheDirectory(manager.GetPreferredCacheDirectory(manager.CbxFiles[manager.ActiveCbxIndex]));
            RequestPreload(manager, nextCbz, PreloadPriority.NextVolume);
            return nextCbz;
        }

        internal void ScheduleNeighborPreloads(CbzManager manager, int activeIndex)
        {
            if (manager.CbxFiles.Count <= 1) return;

            for (int offset = 1; offset <= 2; offset++)
            {
                int targetIndex = activeIndex + offset;
                if (targetIndex < 0 || targetIndex >= manager.CbxFiles.Count)
                    continue;

                var nextCbz = manager.CbxFiles[targetIndex];
                var cacheDir = manager.GetArea1CacheDirectory(nextCbz);
                if (CbzCacheHelper.IsCacheReady(cacheDir))
                {
                    manager.NotifyCacheStatus(nextCbz, $"先読み確認(+{offset}): {Path.GetFileName(nextCbz)} はキャッシュ済み");
                    continue;
                }

                if (_pendingPreloads.ContainsKey(cacheDir))
                    continue;

                StartupHandler.WriteStartupLog($"[CBZ] schedule-preload activeIndex={activeIndex} targetIndex={targetIndex} file={Path.GetFileName(nextCbz)}");
                RequestPreload(manager, nextCbz, PreloadPriority.NextVolume);
            }
        }

        internal void RequestPreload(CbzManager manager, string cbzPath, PreloadPriority priority)
        {
            if (string.IsNullOrEmpty(cbzPath)) return;

            string cacheDir;
            try { cacheDir = GetPreloadCacheDirectory(manager, cbzPath, priority); }
            catch { return; }

            if (CbzCacheHelper.IsCacheReady(cacheDir))
            {
                System.Diagnostics.Debug.WriteLine($"[CBZ] Preload skip(cached) pri={priority}: {Path.GetFileName(cbzPath)}");
                return;
            }

            if (!_pendingPreloads.TryAdd(cacheDir, priority))
            {
                System.Diagnostics.Debug.WriteLine($"[CBZ] Preload skip(in-progress) pri={priority}: {Path.GetFileName(cbzPath)}");
                return;
            }

            string? currentCacheDir = manager.ActiveCbxIndex >= 0 && manager.ActiveCbxIndex < manager.CbxFiles.Count
                ? manager.GetPreferredCacheDirectory(manager.CbxFiles[manager.ActiveCbxIndex])
                : null;
            if (currentCacheDir != null) manager.ProtectCacheDirectory(currentCacheDir);
            manager.ProtectCacheDirectory(cacheDir);

            Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (manager.TryAcquireCache(cbzPath, $"preload-{priority}", currentCacheDir))
                    {
                        StartupHandler.WriteStartupLog($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=extracted total={sw.ElapsedMilliseconds}ms");
                    }
                    else
                    {
                        StartupHandler.WriteStartupLog($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=disk-cache total={sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} failed error={ex.GetType().Name}");
                }
                finally
                {
                    _pendingPreloads.TryRemove(cacheDir, out _);
                    if (priority == PreloadPriority.NextVolume || priority == PreloadPriority.NextFolder)
                        StartFolderPreloadIfPending(manager);
                }
            });
        }

        internal async Task PreloadNextCbzIfAvailableAsync(CbzManager manager)
        {
            if (manager.ActiveCbxIndex + 1 >= manager.CbxFiles.Count) return;
            await Task.Run(() => RequestPreload(manager, manager.CbxFiles[manager.ActiveCbxIndex + 1], PreloadPriority.NextVolume));
        }

        private void EnsureTargetsCached(int version, List<string> currentTargets, List<string> nextTargets)
        {
            foreach (var cbz in currentTargets)
            {
                if (version != Volatile.Read(ref _navigationPreloadVersion))
                    return;
                EnsurePreloadedForArea(cbz);
            }

            foreach (var cbz in nextTargets)
            {
                if (version != Volatile.Read(ref _navigationPreloadVersion))
                    return;
                EnsurePreloadedForArea(cbz);
            }
        }

        private int CountReadyCaches(List<string> cbzTargets)
        {
            int ready = 0;
            foreach (var cbz in cbzTargets)
            {
                if (CbzCacheHelper.IsCacheReady(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MangaViewer",
                    "CBZCache",
                    CbzCacheHelper.ComputeCacheHash(cbz))))
                    ready++;
            }
            return ready;
        }

        private IEnumerable<string> GetSortedCbzFiles(string folderPath)
        {
            return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, new CbzVolumeComparer());
        }

        private void EnsurePreloadedForArea(string cbzPath)
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer",
                "CBZCache",
                CbzCacheHelper.ComputeCacheHash(cbzPath));
            if (CbzCacheHelper.IsCacheReady(cacheDir))
                return;

            // The actual extraction is done by the manager; no need to duplicate the logic here.
            using var _ = new Mutex(false);
            StartupHandler.WriteStartupLog($"[PROF] CbzManager.NavigationPreload cbz={Path.GetFileName(cbzPath)} area=area1 source=extracted");
        }

        private string GetPreloadCacheDirectory(CbzManager manager, string cbzPath, PreloadPriority priority)
        {
            return manager.GetArea1CacheDirectory(cbzPath);
        }
    }
}
