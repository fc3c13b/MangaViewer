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
        internal enum PreloadPriority { Current = 0, NextVolume = 1, NextFolder = 2, BackgroundList = 3 }

        private readonly ConcurrentDictionary<string, PreloadPriority> _pendingPreloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _pendingFolderPreloads = new();
        private readonly HashSet<string> _queuedFolderPreloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _folderPreloadQueueLock = new();
        private int _navigationPreloadVersion;
        private readonly object _navigationPreloadLock = new();
        private string? _lastNavigationCurrentFolder;
        private string? _lastNavigationNextFolder;
        private int _backgroundListVersion;
        private int _lastBgPreloadFolderIndex = -1;

        private CancellationTokenSource _navCts = new();
        private readonly object _navCtsLock = new();

        /// <summary>
        /// 旧フォルダの NextVolume 孤立タスクをキャンセルし、新しいトークンを返す。
        /// フォルダ変更時に呼び出す。
        /// </summary>
        private CancellationToken CancelAndRenewNavCts()
        {
            var newCts = new CancellationTokenSource();
            CancellationTokenSource oldCts;
            lock (_navCtsLock)
            {
                oldCts = _navCts;
                _navCts = newCts;
            }
            try { oldCts.Cancel(); } catch { }
            try { oldCts.Dispose(); } catch { }
            return newCts.Token;
        }

        private CancellationToken GetNavToken()
        {
            lock (_navCtsLock)
                return _navCts.Token;
        }

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

            // ナビゲーショントークンのキャンセルはフォルダ移動開始時
            // (CancelBackgroundListPreload) に一元化した。ここでは行わない
            // （巻切替で本メソッドが呼ばれても現在フォルダの先読みは止めない）。
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

                    // 全プリロードを RequestPreload 経由に統一（_pendingPreloads が重複防止の唯一の窓口）
                    foreach (var cbz in currentTargets)
                    {
                        if (version != Volatile.Read(ref _navigationPreloadVersion)) return;
                        RequestPreload(manager, cbz, PreloadPriority.NextVolume);
                    }
                    foreach (var cbz in nextTargets)
                    {
                        if (version != Volatile.Read(ref _navigationPreloadVersion)) return;
                        RequestPreload(manager, cbz, PreloadPriority.NextFolder);
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

            // 全ての先読み（NextVolume/NextFolder/BackgroundList）はフォルダ移動時に
            // キャンセルされるトークンを使用する。これにより、リストを移動した時点で
            // 旧位置（＝移動後は上位）コンテンツの展開が即座に停止する。
            // ※ 表示用の "current" は RequestPreload を経由せず TryAcquireCache 直呼びのため
            //    このトークンの影響を受けない（キャンセルされない）。
            CancellationToken ct = GetNavToken();

            Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // キャンセル済みなら展開せずスキップ
                    if (ct.IsCancellationRequested)
                    {
                        StartupHandler.WriteStartupLog($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=cancelled(pre-start) total={sw.ElapsedMilliseconds}ms");
                        return;
                    }

                    if (manager.TryAcquireCache(cbzPath, $"preload-{priority}", ct, currentCacheDir))
                    {
                        StartupHandler.WriteStartupLog($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=extracted total={sw.ElapsedMilliseconds}ms");
                    }
                    else
                    {
                        StartupHandler.WriteStartupLog($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=disk-cache total={sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (OperationCanceledException)
                {
                    StartupHandler.WriteStartupLog($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=cancelled total={sw.ElapsedMilliseconds}ms");
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

        private IEnumerable<string> GetSortedCbzFiles(string folderPath)
        {
            return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, new CbzVolumeComparer());
        }

        private string GetPreloadCacheDirectory(CbzManager manager, string cbzPath, PreloadPriority priority)
        {
            return manager.GetArea1CacheDirectory(cbzPath);
        }

        /// <summary>
        /// フォルダ移動開始時に呼び出す。BG-PRELOAD を即キャンセルし、
        /// 古い currentIndex での遅延起動を防ぐ。
        /// 併せて、進行中の全先読み展開（上位コンテンツ含む）をキャンセルし、
        /// 未着手のフォルダ先読みキューをクリアする。
        /// </summary>
        internal void CancelBackgroundListPreload()
        {
            _lastBgPreloadFolderIndex = -1; // 次回の ScheduleNavigationPreload で必ず再起動
            Interlocked.Increment(ref _backgroundListVersion);

            // 進行中の NextVolume/NextFolder/BackgroundList 展開を即キャンセル
            CancelAndRenewNavCts();

            // 未処理のフォルダ先読みキュー（旧位置基準）を破棄
            lock (_folderPreloadQueueLock)
            {
                _pendingFolderPreloads.Clear();
                _queuedFolderPreloads.Clear();
            }
        }

        /// <summary>
        /// 指定フォルダインデックスで既に BG-PRELOAD が起動済みか。
        /// true の場合は再起動不要（巻切替による重複起動を防ぐ）。
        /// </summary>
        internal bool IsBgPreloadRunningFor(int folderIndex) =>
            _lastBgPreloadFolderIndex == folderIndex;
        internal bool HasHighPriorityPendingPreloads
        {
            get
            {
                foreach (var pair in _pendingPreloads)
                    if (pair.Value <= PreloadPriority.NextVolume)
                        return true;
                lock (_folderPreloadQueueLock)
                    return _pendingFolderPreloads.Count > 0;
            }
        }

        /// <summary>指定キャッシュディレクトリの展開が RequestPreload 経由で進行中か。</summary>
        internal bool IsExtractionPending(string cacheDir) =>
            _pendingPreloads.ContainsKey(cacheDir);

        /// <summary>
        /// 現在コンテンツの先読みが完了した後、リスト下位コンテンツで
        /// 「次回表示時に実際に開く巻(LastCbzFile／先頭巻)」を順次バックグラウンド先読みする。
        /// ・対象巻の選定は表示ロジック(ImageLoader.Load)と完全に一致させる。
        /// ・maxFolderScan 件を超えたら停止（I/O 量の上限保証）。
        /// ・コンテンツ切替時は自動キャンセル。空き容量不足時も停止。
        /// </summary>
        internal void StartBackgroundListPreloadAsync(
            CbzManager manager,
            IReadOnlyList<string> folderList,
            int currentIndex,
            string cacheRoot,
            long minFreeSpaceBytes,
            bool startFromLastViewed,
            int maxFolderScan = 50)
        {
            _lastBgPreloadFolderIndex = currentIndex;
            int version = Interlocked.Increment(ref _backgroundListVersion);

            Task.Run(async () =>
            {
                try
                {
                    // 高優先度の先読みが全て終わるまで待つ（最大 60 秒）
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (HasHighPriorityPendingPreloads
                           && version == Volatile.Read(ref _backgroundListVersion)
                           && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(300).ConfigureAwait(false);
                    }

                    if (version != Volatile.Read(ref _backgroundListVersion))
                    {
                        StartupHandler.WriteStartupLog("[BG-PRELOAD] cancelled before start");
                        return;
                    }

                    int remaining = Math.Min(folderList.Count - currentIndex - 1, maxFolderScan);
                    StartupHandler.WriteStartupLog($"[BG-PRELOAD] start: below={remaining} limit={maxFolderScan} currentIndex={currentIndex}");

                    int scanned = 0;
                    for (int i = currentIndex + 1; i < folderList.Count; i++)
                    {
                        if (version != Volatile.Read(ref _backgroundListVersion))
                        {
                            StartupHandler.WriteStartupLog($"[BG-PRELOAD] cancelled at folderIndex={i}");
                            return;
                        }

                        // Option B: スキャン件数の上限（I/O 量の上限保証）
                        if (scanned >= maxFolderScan)
                        {
                            StartupHandler.WriteStartupLog($"[BG-PRELOAD] stopped: reached maxFolderScan={maxFolderScan}");
                            return;
                        }

                        string folderPath = folderList[i];
                        if (!Directory.Exists(folderPath))
                            continue;

                        var entry = PlaybackHistoryService.Get(folderPath);

                        scanned++;
                        List<string> cbzFiles;
                        try
                        {
                            cbzFiles = GetSortedCbzFiles(folderPath).ToList();
                        }
                        catch (Exception ex)
                        {
                            StartupHandler.WriteStartupLog($"[BG-PRELOAD] scan error folder={Path.GetFileName(folderPath)} error={ex.GetType().Name}");
                            continue;
                        }

                        if (cbzFiles.Count == 0) continue;

                        // 表示ロジック(ImageLoader.Load)と完全に同じ基準で対象巻を選ぶ。
                        // ・StartFromLastViewedPosition かつ LastCbzFile がフォルダ内に存在 → その巻
                        // ・それ以外 → 先頭巻(index 0)
                        // ※ MaxReadVolIndex は使わない:
                        //    表示が開くのは「最後に見た巻(LastCbzFile)」であり、
                        //    最大既読巻(MaxReadVolIndex)とは乖離しうるため、ここが乖離すると
                        //    表示時にキャッシュミス→再DLが発生する。
                        int startVol = 0;
                        if (startFromLastViewed && !string.IsNullOrEmpty(entry?.LastCbzFile))
                        {
                            int idx = cbzFiles.FindIndex(f =>
                                string.Equals(f, entry!.LastCbzFile, StringComparison.OrdinalIgnoreCase));
                            if (idx >= 0)
                                startVol = idx;
                        }
                        int endVol = Math.Min(startVol + 1, cbzFiles.Count);

                        for (int v = startVol; v < endVol; v++)
                        {
                            if (version != Volatile.Read(ref _backgroundListVersion))
                                return;

                            // 空き容量チェック（巻ごと）
                            if (!HasSufficientFreeSpace(cacheRoot, minFreeSpaceBytes))
                            {
                                StartupHandler.WriteStartupLog($"[BG-PRELOAD] stop: free space below threshold at folderIndex={i} vol={v}");
                                return;
                            }

                            string cbzPath = cbzFiles[v];
                            string cacheDir = CbzCacheHelper.GetArea1CacheDirectory(cacheRoot, cbzPath);

                            if (CbzCacheHelper.IsCacheReady(cacheDir))
                            {
                                StartupHandler.WriteStartupLog($"[BG-PRELOAD] skip cached: folder={Path.GetFileName(folderPath)} vol={v} file={Path.GetFileName(cbzPath)}");
                                continue;
                            }

                            StartupHandler.WriteStartupLog($"[BG-PRELOAD] preload: folder={Path.GetFileName(folderPath)} vol={v} file={Path.GetFileName(cbzPath)}");
                            RequestPreload(manager, cbzPath, PreloadPriority.BackgroundList);

                            // この巻の展開完了を待ってから次へ（最大 120 秒）
                            var waitDeadline = DateTime.UtcNow.AddSeconds(120);
                            while (_pendingPreloads.ContainsKey(cacheDir)
                                   && version == Volatile.Read(ref _backgroundListVersion)
                                   && DateTime.UtcNow < waitDeadline)
                            {
                                await Task.Delay(300).ConfigureAwait(false);
                            }

                            if (version != Volatile.Read(ref _backgroundListVersion))
                                return;
                        }
                    }

                    StartupHandler.WriteStartupLog($"[BG-PRELOAD] completed scanned={scanned}");
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[BG-PRELOAD] unexpected error: {ex.GetType().Name} {ex.Message}");
                }
            });
        }

        private static bool HasSufficientFreeSpace(string cacheRoot, long minFreeSpaceBytes)
        {
            try
            {
                string? rootPath = Path.GetPathRoot(cacheRoot);
                if (string.IsNullOrEmpty(rootPath))
                    return true;
                var drive = new DriveInfo(rootPath);
                return drive.AvailableFreeSpace >= minFreeSpaceBytes;
            }
            catch
            {
                return true;
            }
        }
    }
}
