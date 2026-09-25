using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MangaViewer
{
    public class CbzManager : IDisposable
    {
        private readonly string _cacheRootArea1;
        private readonly string _cacheRootArea2;

        private const int CurrentTitleCacheTargetCount = 3;
        private const int NextTitleCacheTargetCount = 2;
        // Area1のキャッシュ上限（大量のCBZを保持するための大容量キャッシュ）
        private const int MaxCachedCbzCountArea1 = 50;
        private const int MaxCachedCbzCountArea2 = 0;
        internal const string CacheCompleteMarkerFileName = ".complete";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheExtractionLocks = new();
        private static readonly SemaphoreSlim SlowSourceExtractionLock = new(1, 1);

        internal List<string> CbxFiles = new();
        internal int ActiveCbxIndex = 0;
        internal List<string> CurrentImagePaths = new();

        // A cache directory is immutable while the application is running.  Keep the
        // sorted file list in memory so repeated navigation never re-enumerates it.
        private readonly Dictionary<string, List<string>> _imagePathsByCbz =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _cbzByCacheDirectory =
            new(StringComparer.OrdinalIgnoreCase);
        private string? _loadedCbzFile;
        private long _imagePathCacheHits;
        private long _imagePathCacheMisses;
        private readonly object _retentionLock = new();
        private readonly List<string> _priorityCacheDirs = new();
        private const int PriorityCacheDirCount = MaxCachedCbzCountArea1;
        private enum PreloadPriority { Current = 0, NextVolume = 1, NextFolder = 2 }
        private readonly ConcurrentDictionary<string, PreloadPriority> _pendingPreloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _pendingFolderPreloads = new();
        private readonly HashSet<string> _queuedFolderPreloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _folderPreloadQueueLock = new();
        private string? _pendingNextFolderPath; // NextVolume完了後に処理するフォルダ（NAS/低帯域対応）
        private int _navigationPreloadVersion;
        private readonly object _navigationPreloadLock = new();
        private string? _lastNavigationCurrentFolder;
        private string? _lastNavigationNextFolder;

        /// <summary>展開キャッシュの追加・削除後に、UIへ表示更新を通知する。</summary>
        public event Action? CacheChanged;
        /// <summary>CBZ読み出し・展開中の状態をUIへ通知する。</summary>
        public event Action<string>? CacheStatusChanged;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        public CbzManager()
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
                _cacheRootArea1 = primaryRoot;
                _cacheRootArea2 = secondaryRoot;
            }
            catch (UnauthorizedAccessException)
            {
                // キャッシュフォルダ書き込み不可→一時的に temp を使う
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
                _cacheRootArea1 = tmp1;
                _cacheRootArea2 = tmp2;
                try { Directory.CreateDirectory(_cacheRootArea1); } catch { /* ignore */ }
                try { Directory.CreateDirectory(_cacheRootArea2); } catch { /* ignore */ }
            }
            catch
            {
                // その他エラー→一時的に temp に切り替え
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
                _cacheRootArea1 = tmp1;
                _cacheRootArea2 = tmp2;
                try { Directory.CreateDirectory(_cacheRootArea1); } catch { /* ignore */ }
                try { Directory.CreateDirectory(_cacheRootArea2); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// アプリ起動時に全てのCBZ展開キャッシュを削除します。
        /// </summary>
        public static void ClearAllCache()
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
                    // 削除失敗は許容（一部使用中など）
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
                    // 削除失敗は許容（一部使用中など）
                    StartupHandler.WriteStartupLog($"[CACHE-CLEAR] area=area2 failed root={cacheRoot2} error={ex.GetType().Name}");
                }
            }
        }

        internal string? CurrentFolder { get; private set; }

        /// <summary>
        /// Initialize for a folder with .cbz/.zip files. Returns true if CBZ mode is active.
        /// </summary>
        public bool InitializeForFolder(string folderPath, string? preferredCbzFile = null, bool forceReset = true)
        {
            StartupHandler.WriteStartupLog($"[CBZ] InitializeForFolder START folder={folderPath} forceReset={forceReset}");

            if (!forceReset && string.Equals(CurrentFolder, folderPath, StringComparison.OrdinalIgnoreCase) && CbxFiles.Any())
            {
                RefreshCurrentImagePaths(extractEvenIfEmpty: false);
                return CurrentImagePaths.Any();
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Reset();

            try
            {
                if (!Directory.Exists(folderPath))
                    return false;

                CurrentFolder = folderPath;
                long t0 = sw.ElapsedMilliseconds;
                var cbzs = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                                    .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                                                f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(f => f, new CbzVolumeComparer())
                                    .ToList();
                long tScanEnd = sw.ElapsedMilliseconds;

                if (cbzs.Count == 0)
                    return false;

                StartupHandler.WriteStartupLog($"[CBZ] Scan complete: {cbzs.Count} CBZ files found in {folderPath}");
                CbxFiles = cbzs;
                ActiveCbxIndex = 0;

                if (!string.IsNullOrWhiteSpace(preferredCbzFile))
                {
                    int preferredIndex = CbxFiles.FindIndex(x => string.Equals(x, preferredCbzFile, StringComparison.OrdinalIgnoreCase));
                    if (preferredIndex >= 0)
                        ActiveCbxIndex = preferredIndex;
                }

                // 起動時は先頭（現在）巻だけを準備する。次巻の展開は非同期で
                // 先読みし、全巻を起動時に展開しない。
                RefreshCurrentImagePaths(extractEvenIfEmpty: true);

                long tRefreshEnd = sw.ElapsedMilliseconds;
                StartupHandler.WriteStartupLog($"[PROF] CbzManager.InitializeForFolder total={tRefreshEnd}ms scan={tScanEnd - t0}ms refresh={tRefreshEnd - tScanEnd}ms cbzCount={cbzs.Count} folder={folderPath}");

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
            if (CurrentImagePaths.Count == 0 && TryRecoverToPlayableCbx(preferNext: true))
                return CurrentImagePaths;
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
            if (CurrentImagePaths.Count == 0 && TryRecoverToPlayableCbx(preferNext: false))
                return CurrentImagePaths;
            return CurrentImagePaths;
        }

        /// <summary>
        /// 次のCBZに強制的に切り替えます。先頭画像のパスを返します（失敗時はnull）。
        /// </summary>
        public string? SwitchToNextCbx()
        {
            if (CbxFiles.Count <= 1) return null;

            int beforeIndex = ActiveCbxIndex;
            StartupHandler.WriteStartupLog($"[CBZ] switchToNext before={beforeIndex} count={CbxFiles.Count} current={Path.GetFileName(CbxFiles[beforeIndex])}");

            ActiveCbxIndex++;
            if (ActiveCbxIndex >= CbxFiles.Count)
            {
                ActiveCbxIndex = CbxFiles.Count - 1;
                StartupHandler.WriteStartupLog($"[CBZ] switchToNext clamp after={ActiveCbxIndex} file={Path.GetFileName(CbxFiles[ActiveCbxIndex])}");
                return null;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: true);
            if (CurrentImagePaths.Count == 0 && TryRecoverToPlayableCbx(preferNext: true))
                return CurrentImagePaths.FirstOrDefault();
            StartupHandler.WriteStartupLog($"[CBZ] switchToNext result after={ActiveCbxIndex} file={Path.GetFileName(CbxFiles[ActiveCbxIndex])} images={CurrentImagePaths.Count}");
            return CurrentImagePaths.FirstOrDefault();
        }

        /// <summary>
        /// 前のCBZに強制的に切り替えます。先頭画像のパスを返します（失敗時はnull）。
        /// </summary>
        public string? SwitchToPreviousCbx()
        {
            if (CbxFiles.Count <= 1) return null;

            int beforeIndex = ActiveCbxIndex;
            StartupHandler.WriteStartupLog($"[CBZ] switchToPrev before={beforeIndex} count={CbxFiles.Count} current={Path.GetFileName(CbxFiles[beforeIndex])}");

            ActiveCbxIndex--;
            if (ActiveCbxIndex < 0)
            {
                ActiveCbxIndex = 0;
                StartupHandler.WriteStartupLog($"[CBZ] switchToPrev clamp after={ActiveCbxIndex} file={Path.GetFileName(CbxFiles[ActiveCbxIndex])}");
                return null;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: true);
            if (CurrentImagePaths.Count == 0 && TryRecoverToPlayableCbx(preferNext: false))
                return CurrentImagePaths.FirstOrDefault();
            StartupHandler.WriteStartupLog($"[CBZ] switchToPrev result after={ActiveCbxIndex} file={Path.GetFileName(CbxFiles[ActiveCbxIndex])} images={CurrentImagePaths.Count}");
            return CurrentImagePaths.FirstOrDefault();
        }

        /// <summary>
        /// 次のCBZファイルをバックグラウンドで展開（プレロード）します。
        /// 既に展開済みの場合は何もしません。次のCBZのパスを返します（存在しない場合はnull）。
        /// </summary>
        public string? PreloadNextCbx()
        {
            if (ActiveCbxIndex + 1 >= CbxFiles.Count)
                return null;

            string nextCbz = CbxFiles[ActiveCbxIndex + 1];
            if (ActiveCbxIndex >= 0 && ActiveCbxIndex < CbxFiles.Count)
                ProtectCacheDirectory(GetPreferredCacheDirectory(CbxFiles[ActiveCbxIndex]));
            RequestPreload(nextCbz, PreloadPriority.NextVolume);
            return nextCbz;
        }

        /// <summary>
        /// 次のリスト項目用に、指定フォルダの先頭CBZだけを低優先度で展開する。
        /// 現在選択中のCBZ状態は変更しない。
        /// </summary>
        public void PreloadFirstCbxForFolder(string folderPath)
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

            // NextVolume/NextFolderの先読み中は待機し、完了後に連鎖して1件ずつ処理する。
            if (!_pendingPreloads.Values.Any(p => p == PreloadPriority.NextVolume || p == PreloadPriority.NextFolder))
                StartFolderPreloadIfPending();
        }

        /// <summary>
        /// フォルダ移動時の先読みを実行する。
        /// 1) 現在タイトル: 先頭3件をキャッシュ作成
        /// 2) 次タイトル: 先頭2件をキャッシュ作成
        /// すべてエリア1に保存する。
        /// </summary>
        public void PreloadForNavigationContext(string currentFolderPath, int area1Count, string? nextFolderPath)
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
            int currentTargetCount = Math.Max(1, Math.Min(CurrentTitleCacheTargetCount, area1Count));

            Task.Run(() =>
            {
                try
                {
                    var currentTargets = GetSortedCbzFiles(currentFolderPath).Take(currentTargetCount).ToList();
                    var nextTargets = new List<string>();

                    if (!string.IsNullOrWhiteSpace(nextFolderPath) && Directory.Exists(nextFolderPath))
                    {
                        nextTargets = GetSortedCbzFiles(nextFolderPath)
                            .Take(NextTitleCacheTargetCount)
                            .ToList();
                    }

                    EnsureTargetsCached(version, currentTargets, nextTargets);

                    if (version != Volatile.Read(ref _navigationPreloadVersion))
                        return;

                    // Fallback path: if expected caches are still missing, retry once.
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

        private void EnsureTargetsCached(int version, List<string> currentTargets, List<string> nextTargets)
        {
            foreach (var cbz in currentTargets)
            {
                if (version != Volatile.Read(ref _navigationPreloadVersion))
                    return;
                ProtectCacheDirectory(GetArea1CacheDirectory(cbz));
                EnsurePreloadedForArea(cbz);
            }

            foreach (var cbz in nextTargets)
            {
                if (version != Volatile.Read(ref _navigationPreloadVersion))
                    return;
                ProtectCacheDirectory(GetArea1CacheDirectory(cbz));
                EnsurePreloadedForArea(cbz);
            }
        }

        private int CountReadyCaches(List<string> cbzTargets)
        {
            int ready = 0;
            foreach (var cbz in cbzTargets)
            {
                if (IsCacheReady(GetArea1CacheDirectory(cbz)))
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
            if (TryAcquireCache(cbzPath, "navigation"))
            {
                StartupHandler.WriteStartupLog($"[PROF] CbzManager.NavigationPreload cbz={Path.GetFileName(cbzPath)} area=area1 source=extracted");
            }
        }

        /// <summary>
        /// キャッシュ取得の唯一の入口。
        /// - 既存キャッシュはそのまま利用
        /// - 未作成なら展開して .complete を確定
        /// - 作成成功時のみ作成ログ/trim/更新イベントを一括発火
        /// </summary>
        private bool TryAcquireCache(string cbzPath, string trigger, params string?[] protectedCacheDirs)
        {
            string cacheDir = GetArea1CacheDirectory(cbzPath);
            if (IsCacheReady(cacheDir))
                return false;

            if (!EnsureCacheExtracted(cacheDir, cbzPath, NotifyCacheStatus))
                return false;

            WriteCacheCreateLog(cbzPath, cacheDir, trigger);
            var allProtected = new List<string?> { cacheDir };
            if (protectedCacheDirs != null && protectedCacheDirs.Length > 0)
                allProtected.AddRange(protectedCacheDirs);
            TrimCacheDirectories(allProtected.ToArray());
            NotifyCacheChanged();
            return true;
        }

        private void StartFolderPreloadIfPending()
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
                        RequestPreload(firstCbz, PreloadPriority.NextFolder);
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[PROF] CbzManager.StartFolderPreloadIfPending failed folder={folderPath} error={ex.GetType().Name}");
                }
            });
        }

        /// <summary>
        /// 指定されたインデックスのCBZに切り替え、画像リストを更新します（TASK09.30用）。
        /// </summary>
        public void SwitchToCbx(int index)
        {
            if (index < 0 || index >= CbxFiles.Count) return;
            ActiveCbxIndex = index;
            RefreshCurrentImagePaths(extractEvenIfEmpty: true);
        }

        private void Reset()
        {
            CurrentFolder = null;
            CbxFiles.Clear();
            ActiveCbxIndex = 0;
            // CurrentImagePaths can reference an entry in _imagePathsByCbz.
            // Do not clear that shared list when changing folders; only detach
            // the current-view reference so the cached image paths remain valid.
            CurrentImagePaths = new List<string>();
            _loadedCbzFile = null;
        }

        private void RefreshCurrentImagePaths(bool extractEvenIfEmpty, bool preloadNext = true, bool showWarnings = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!CbxFiles.Any())
            {
                CurrentImagePaths.Clear();
                _loadedCbzFile = null;
                return;
            }

            if (ActiveCbxIndex < 0 || ActiveCbxIndex >= CbxFiles.Count)
            {
                // Clamp index
                ActiveCbxIndex = Math.Clamp(ActiveCbxIndex, 0, CbxFiles.Count - 1);
            }

            var cbzFile = CbxFiles[ActiveCbxIndex];

            // The common path: callers frequently ask for the already-selected
            // volume. Avoid both disk enumeration and list allocation.
            if (_loadedCbzFile == cbzFile && _imagePathsByCbz.TryGetValue(cbzFile, out var cached))
            {
                CurrentImagePaths = cached;
                StartupHandler.WriteStartupLog($"[CBZ-STARTUP] Cache HIT: {cbzFile}");
                return;
            }


            string cacheDir;
            string primaryCacheDir;

            try
            {
                primaryCacheDir = GetArea1CacheDirectory(cbzFile);
                cacheDir = GetPreferredCacheDirectory(cbzFile);
                ProtectCacheDirectory(primaryCacheDir);
            }
            catch (UnauthorizedAccessException)
            {
                SetCurrentImagePaths(cbzFile, new List<string>());
                return;
            }
            catch
            {
                SetCurrentImagePaths(cbzFile, new List<string>());
                return;
            }

            if (_imagePathsByCbz.TryGetValue(cbzFile, out var cachedPaths) && IsCacheReady(cacheDir))
            {
                if (cachedPaths.Count > 0)
                {
                    _imagePathCacheHits++;
                    NotifyCacheStatus($"Cache読込中: {Path.GetFileName(cbzFile)}");
                    SetCurrentImagePaths(cbzFile, cachedPaths);
                    _cbzByCacheDirectory[cacheDir] = cbzFile;
                    StartupHandler.WriteStartupLog($"[PROF] CbzManager.ImagePaths cbz={Path.GetFileName(cbzFile)} source=memory images={cachedPaths.Count} total={sw.ElapsedMilliseconds}ms");
                    if (preloadNext) _ = PreloadNextCbzIfAvailableAsync();
                    return;
                }

                StartupHandler.WriteStartupLog($"[CBZ-STARTUP] Empty cached list detected; reloading cbz={Path.GetFileName(cbzFile)} cacheDir={cacheDir}");
                if (showWarnings)
                    ShowRecoveryWarning("画像データが空でした。次の巻へ移動します。");
                _imagePathsByCbz.Remove(cbzFile);
                _cbzByCacheDirectory.Remove(cacheDir);
                _cbzByCacheDirectory.Remove(primaryCacheDir);
                _cbzByCacheDirectory.Remove(GetArea2CacheDirectory(cbzFile));
            }

            // The disk cache may have been evicted while this manager was kept
            // alive. Do not return file paths for a directory that no longer exists.
            _imagePathsByCbz.Remove(cbzFile);
            _cbzByCacheDirectory.Remove(cacheDir);
            _cbzByCacheDirectory.Remove(primaryCacheDir);
            _cbzByCacheDirectory.Remove(GetArea2CacheDirectory(cbzFile));

            _imagePathCacheMisses++;
            bool cacheDirectoryExisted = IsCacheReady(cacheDir);
            long extractMs = 0;

            if (!cacheDirectoryExisted)
            {
                cacheDir = primaryCacheDir;
                // キャッシュミス→展開
                StartupHandler.WriteStartupLog($"[CBZ-STARTUP] Cache MISS: {cbzFile}");
                try
                {
                    NotifyCacheStatus($"{(RequiresSerializedDownload(cbzFile) ? "DL中" : "Cache作成中")}: {Path.GetFileName(cbzFile)}");
                    var extractSw = System.Diagnostics.Stopwatch.StartNew();
                    StartupHandler.WriteStartupLog($"[CBZ-STARTUP] Extracting: {cbzFile} → {cacheDir}");
                    bool extractedNow = TryAcquireCache(cbzFile, "current");
                    extractMs = extractSw.ElapsedMilliseconds;
                    StartupHandler.WriteStartupLog($"[CBZ] Extract {Path.GetFileName(cbzFile)}: {extractMs}ms, result={(extractedNow ? "success" : "already-existed")}");
                    if (extractedNow)
                    {
                        cacheDir = primaryCacheDir;
                    }
                    else
                    {
                        cacheDirectoryExisted = true;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // 展開不可→空扱い
                    SetCurrentImagePaths(cbzFile, new List<string>());
                    if (TryRecoverToPlayableCbx(preferNext: true))
                        return;
                    return;
                }
                catch
                {
                    // その他エラー→空扱い
                    SetCurrentImagePaths(cbzFile, new List<string>());
                    if (TryRecoverToPlayableCbx(preferNext: true))
                        return;
                    return;
                }
            }
            else
            {
                // キャッシュヒット
                System.Diagnostics.Debug.WriteLine($"[CBZ] Cache HIT: {Path.GetFileName(cbzFile)}");
                NotifyCacheStatus($"Cache読込中: {Path.GetFileName(cbzFile)}");
            }

            if (!IsCacheReady(cacheDir))
            {
                if (showWarnings)
                    ShowRecoveryWarning("画像データを再取得できませんでした。次の巻へ移動します。");
                SetCurrentImagePaths(cbzFile, new List<string>());
                if (TryRecoverToPlayableCbx(preferNext: true))
                    return;
                return;
            }

            try
            {
                var enumerateSw = System.Diagnostics.Stopwatch.StartNew();
                var imagePaths = ImageExtensions
                    .SelectMany(extension => Directory.EnumerateFiles(cacheDir, "*" + extension, SearchOption.AllDirectories))
                    .OrderBy(Path.GetFileName)
                    .ToList();
                long enumerateMs = enumerateSw.ElapsedMilliseconds;
                _imagePathsByCbz[cbzFile] = imagePaths;
                _cbzByCacheDirectory[cacheDir] = cbzFile;
                SetCurrentImagePaths(cbzFile, imagePaths);
                string source = cacheDirectoryExisted ? "disk-cache" : "extracted";
                StartupHandler.WriteStartupLog($"[CBZ-STARTUP] Refresh result: count={imagePaths.Count}, cacheDir={cacheDir}");
                StartupHandler.WriteStartupLog($"[PROF] CbzManager.ImagePaths cbz={Path.GetFileName(cbzFile)} source={source} images={imagePaths.Count} extract={extractMs}ms enumerate={enumerateMs}ms total={sw.ElapsedMilliseconds}ms");
            }
            catch (UnauthorizedAccessException)
            {
                // 権限不足→このCBZは表示不可として扱う
                if (showWarnings)
                    ShowRecoveryWarning("画像データの再取得に失敗しました。次の巻へ移動します。");
                SetCurrentImagePaths(cbzFile, new List<string>());
                if (TryRecoverToPlayableCbx(preferNext: true))
                    return;
            }
            catch
            {
                // その他エラー→空扱い
                if (showWarnings)
                    ShowRecoveryWarning("画像データの再取得に失敗しました。次の巻へ移動します。");
                SetCurrentImagePaths(cbzFile, new List<string>());
                if (TryRecoverToPlayableCbx(preferNext: true))
                    return;
            }

            // 次のCBZがあれば自動的に展開（連続プリロード）
            if (preloadNext) _ = PreloadNextCbzIfAvailableAsync();
        }

        private static void ShowRecoveryWarning(string message)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    message,
                    "注意",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
            catch
            {
                // UI が不可能な環境では通知を抑止する
            }
        }

        private void SetCurrentImagePaths(string cbzFile, List<string> imagePaths)
        {
            CurrentImagePaths = imagePaths;
            _loadedCbzFile = cbzFile;
            StartupHandler.WriteStartupLog($"[CBZ] set-current file={Path.GetFileName(cbzFile)} activeIndex={ActiveCbxIndex} imageCount={imagePaths.Count}");
        }

        private bool TryRecoverToPlayableCbx(bool preferNext)
        {
            if (CbxFiles.Count <= 1)
                return false;

            int originalIndex = ActiveCbxIndex;
            IEnumerable<int> candidateIndexes = preferNext
                ? Enumerable.Range(originalIndex + 1, CbxFiles.Count - originalIndex - 1)
                    .Concat(Enumerable.Range(0, originalIndex).Reverse())
                : Enumerable.Range(0, originalIndex).Reverse()
                    .Concat(Enumerable.Range(originalIndex + 1, CbxFiles.Count - originalIndex - 1));

            foreach (int candidateIndex in candidateIndexes)
            {
                if (candidateIndex < 0 || candidateIndex >= CbxFiles.Count)
                    continue;

                ActiveCbxIndex = candidateIndex;
                RefreshCurrentImagePaths(extractEvenIfEmpty: true, preloadNext: false, showWarnings: false);
                if (CurrentImagePaths.Count > 0)
                {
                    StartupHandler.WriteStartupLog($"[CBZ] recovery skip from={originalIndex} to={candidateIndex} file={Path.GetFileName(CbxFiles[candidateIndex])} images={CurrentImagePaths.Count}");
                    return true;
                }
            }

            ActiveCbxIndex = originalIndex;
            RefreshCurrentImagePaths(extractEvenIfEmpty: false, preloadNext: false, showWarnings: false);
            return false;
        }

        /// <summary>
        /// キャッシュはアクセス順ではなく、展開された順（作成時刻）で保持する。
        /// エリア1は最大4件、エリア2は最大2件を維持する。
        /// 現在表示中のCBZだけは削除対象から除外する。
        /// 新規作成キャッシュ（30秒以内）も削除対象から除外する（先読み直後の削除を防ぐ）。
        /// </summary>
        private void TrimCacheDirectories(params string?[] protectedCacheDirs)
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
                    .Where(IsCacheReady)
                    .OrderBy(Directory.GetCreationTimeUtc)
                    .ToList();
                int remainingCount = cacheDirs.Count;
                var now = DateTime.UtcNow;
                int deletedCount = 0;
                int skippedProtected = 0;
                int skippedRecent = 0;
                int deleteFailed = 0;

                string area = string.Equals(targetRoot, _cacheRootArea2, StringComparison.OrdinalIgnoreCase)
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

                    // 新規作成キャッシュ（30秒以内）は削除しない（先読み直後の削除を防ぐ）
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
                        // 時刻取得失敗時は削除対象とする
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
                        // 削除失敗は許容（使用中など）
                        deleteFailed++;
                        StartupHandler.WriteStartupLog($"[CACHE-DELETE] area={area} failed dir={oldest} error={ex.GetType().Name}");
                    }
                }

                StartupHandler.WriteStartupLog($"[CACHE-TRIM] phase=end area={area} deleted={deletedCount} skippedProtected={skippedProtected} skippedRecent={skippedRecent} deleteFailed={deleteFailed} remaining={remainingCount}");
            }
            catch
            {
                // キャッシュ整理に失敗しても現在巻の表示は継続する。
            }
        }

        private bool TryResolveCacheArea(string cacheDir, out string rootPath, out int maxCount)
        {
            if (cacheDir.StartsWith(_cacheRootArea1, StringComparison.OrdinalIgnoreCase))
            {
                rootPath = _cacheRootArea1;
                maxCount = MaxCachedCbzCountArea1;
                return true;
            }

            if (cacheDir.StartsWith(_cacheRootArea2, StringComparison.OrdinalIgnoreCase))
            {
                rootPath = _cacheRootArea2;
                maxCount = MaxCachedCbzCountArea2;
                return true;
            }

            rootPath = string.Empty;
            maxCount = 0;
            return false;
        }

        /// <summary>
        /// 現在巻・次巻・次リスト項目の先頭巻を、FIFO整理から保護する。
        /// 新しい候補を優先し、保持対象は対象エリアの上限件数までに限定する。
        /// </summary>
        private void ProtectCacheDirectory(string cacheDir)
        {
            if (!TryResolveCacheArea(cacheDir, out var areaRoot, out var maxCount))
                return;

            lock (_retentionLock)
            {
                _priorityCacheDirs.RemoveAll(dir =>
                    string.Equals(dir, cacheDir, StringComparison.OrdinalIgnoreCase));
                _priorityCacheDirs.Add(cacheDir);

                // Keep only recent entries for this area within its max capacity.
                int areaCount = _priorityCacheDirs.Count(dir =>
                    dir.StartsWith(areaRoot, StringComparison.OrdinalIgnoreCase));

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

                // Global guardrail against unbounded growth.
                while (_priorityCacheDirs.Count > PriorityCacheDirCount + MaxCachedCbzCountArea2)
                    _priorityCacheDirs.RemoveAt(0);
            }
        }

        /// <summary>先読みを一元管理する。完了済み・展開中の重複要求はスキップ。</summary>
        private void RequestPreload(string cbzPath, PreloadPriority priority)
        {
            if (string.IsNullOrEmpty(cbzPath)) return;

            string cacheDir;
            try { cacheDir = GetPreloadCacheDirectory(cbzPath, priority); }
            catch { return; }

            if (IsCacheReady(cacheDir))
            {
                System.Diagnostics.Debug.WriteLine($"[CBZ] Preload skip(cached) pri={priority}: {Path.GetFileName(cbzPath)}");
                return;
            }

            if (!_pendingPreloads.TryAdd(cacheDir, priority))
            {
                System.Diagnostics.Debug.WriteLine($"[CBZ] Preload skip(in-progress) pri={priority}: {Path.GetFileName(cbzPath)}");
                return;
            }

            // 現在巻と対象をどちらも保護してからTask起動
            string? currentCacheDir = ActiveCbxIndex >= 0 && ActiveCbxIndex < CbxFiles.Count
                ? GetPreferredCacheDirectory(CbxFiles[ActiveCbxIndex])
                : null;
            if (currentCacheDir != null) ProtectCacheDirectory(currentCacheDir);
            ProtectCacheDirectory(cacheDir);

            if (priority == PreloadPriority.NextVolume || priority == PreloadPriority.NextFolder)
                NotifyCacheStatus($"{(RequiresSerializedDownload(cbzPath) ? "DL中" : "Cache作成中")}: {Path.GetFileName(cbzPath)}");

            Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (IsCacheReady(cacheDir))
                        NotifyCacheStatus($"Cache読込中: {Path.GetFileName(cbzPath)}");
                    if (TryAcquireCache(cbzPath, $"preload-{priority}", currentCacheDir))
                    {
                        NotifyCacheStatus($"表示準備完了: {Path.GetFileName(cbzPath)}");
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
                    // NextVolume/NextFolder完了後に次のフォルダ先読みを連鎖させる。
                    if (priority == PreloadPriority.NextVolume || priority == PreloadPriority.NextFolder)
                        StartFolderPreloadIfPending();
                }
            });
        }

        private async Task PreloadNextCbzIfAvailableAsync()
        {
            if (ActiveCbxIndex + 1 >= CbxFiles.Count) return;
            await Task.Run(() => RequestPreload(CbxFiles[ActiveCbxIndex + 1], PreloadPriority.NextVolume));
        }

        private static string ComputeCacheHash(string cbzFile)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(cbzFile));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private string GetArea1CacheDirectory(string cbzFile) =>
            Path.Combine(_cacheRootArea1, ComputeCacheHash(cbzFile));

        private string GetArea2CacheDirectory(string cbzFile) =>
            Path.Combine(_cacheRootArea2, ComputeCacheHash(cbzFile));

        private string GetPreferredCacheDirectory(string cbzFile)
        {
            string area1 = GetArea1CacheDirectory(cbzFile);
            if (IsCacheReady(area1))
                return area1;

            return area1;
        }

        private string GetPreloadCacheDirectory(string cbzFile, PreloadPriority priority)
        {
            return GetArea1CacheDirectory(cbzFile);
        }

        public static IEnumerable<string> GetCacheSearchRoots()
        {
            string baseRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MangaViewer");

            yield return Path.Combine(baseRoot, "CBZCache");
        }

        private static bool IsCacheReady(string cacheDir)
        {
            if (!Directory.Exists(cacheDir))
                return false;

            string markerPath = Path.Combine(cacheDir, CacheCompleteMarkerFileName);
            if (!File.Exists(markerPath))
                return false;

            // 空のキャッシュは不正状態として扱い、再展開させる。
            return Directory.EnumerateFiles(cacheDir, "*.*", SearchOption.AllDirectories)
                .Any(path => !string.Equals(Path.GetFileName(path), CacheCompleteMarkerFileName, StringComparison.OrdinalIgnoreCase));
        }

        private void WriteCacheCreateLog(string cbzFile, string cacheDir, string trigger)
        {
            string area = cacheDir.StartsWith(_cacheRootArea2, StringComparison.OrdinalIgnoreCase)
                ? "area2"
                : "area1";
            StartupHandler.WriteStartupLog($"[CACHE-CREATE] trigger={trigger} area={area} cbz={Path.GetFileName(cbzFile)} dir={cacheDir}");
        }

        private void NotifyCacheStatus(string message)
        {
            try
            {
                CacheStatusChanged?.Invoke(message);
            }
            catch
            {
                // UI通知失敗は無視する
            }
        }

        /// <summary>
        /// 展開先とは別の一時ディレクトリに展開し、完了後に原子的に確定する。
        /// 同じCBZへの同時アクセスは待機させ、不完全なキャッシュを公開しない。
        /// </summary>
        private static bool EnsureCacheExtracted(string cacheDir, string cbzFile, Action<string>? statusCallback = null)
        {
            var extractionLock = CacheExtractionLocks.GetOrAdd(cacheDir, _ => new SemaphoreSlim(1, 1));
            extractionLock.Wait();
            bool usesSlowSourceLock = RequiresSerializedDownload(cbzFile);
            var startedAt = System.Diagnostics.Stopwatch.StartNew();
            var nextPromptAt = TimeSpan.FromMinutes(1);
            try
            {
                if (IsCacheReady(cacheDir))
                    return false;

                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, recursive: true);

                string temporaryDirectory = cacheDir + ".extracting";
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);

                if (usesSlowSourceLock)
                    SlowSourceExtractionLock.Wait();

                ExtractCbzTo(temporaryDirectory, cbzFile, startedAt, ref nextPromptAt, statusCallback);
                File.WriteAllText(Path.Combine(temporaryDirectory, CacheCompleteMarkerFileName), string.Empty);
                Directory.Move(temporaryDirectory, cacheDir);
                return true;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (Directory.Exists(cacheDir + ".extracting"))
                        Directory.Delete(cacheDir + ".extracting", recursive: true);
                }
                catch
                {
                    // ignore cleanup failure
                }
                return false;
            }
            finally
            {
                if (usesSlowSourceLock)
                    SlowSourceExtractionLock.Release();
                extractionLock.Release();
            }
        }

        private static bool RequiresSerializedDownload(string cbzFile)
        {
            if (string.IsNullOrWhiteSpace(cbzFile))
                return false;

            return cbzFile.StartsWith("O:\\", StringComparison.OrdinalIgnoreCase);
        }

        private static void ExtractCbzTo(string cacheDir, string cbzFile, System.Diagnostics.Stopwatch startedAt, ref TimeSpan nextPromptAt, Action<string>? statusCallback = null)
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
                int totalEntries = archive.Entries.Count(entry => !string.IsNullOrEmpty(entry.Name));
                int processedEntries = 0;
                long lastStatusMs = 0;

                foreach (var entry in archive.Entries)
                {
                    if (startedAt.Elapsed >= nextPromptAt)
                    {
                        int minutes = (int)Math.Floor(startedAt.Elapsed.TotalMinutes);
                        if (!PromptContinueDownload(minutes))
                            throw new OperationCanceledException();
                        nextPromptAt = TimeSpan.FromMinutes(minutes + 1);
                    }

                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    processedEntries++;

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

                    if (statusCallback != null)
                    {
                        long elapsedMs = (long)startedAt.Elapsed.TotalMilliseconds;
                        if (processedEntries == 1 || processedEntries == totalEntries || elapsedMs - lastStatusMs >= 2000)
                        {
                            lastStatusMs = elapsedMs;
                            statusCallback(BuildDownloadStatus(cbzFile, processedEntries, totalEntries, startedAt.Elapsed));
                        }
                    }
                }

                statusCallback?.Invoke($"表示準備完了: {Path.GetFileName(cbzFile)}");
            }
            catch (OperationCanceledException)
            {
                throw;
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

        private static string BuildDownloadStatus(string cbzFile, int processedEntries, int totalEntries, TimeSpan elapsed)
        {
            string fileName = Path.GetFileName(cbzFile);
            if (totalEntries <= 0 || processedEntries <= 0)
                return $"DL中: {fileName}";

            if (processedEntries >= totalEntries)
                return $"DL中: {fileName} ({processedEntries}/{totalEntries})";

            double avgMsPerEntry = elapsed.TotalMilliseconds / processedEntries;
            double remainingMs = avgMsPerEntry * Math.Max(0, totalEntries - processedEntries);
            string eta = FormatApproxDuration(TimeSpan.FromMilliseconds(remainingMs));
            return $"DL中: {fileName} ({processedEntries}/{totalEntries}) 残り約{eta}";
        }

        private static string FormatApproxDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}時間{duration.Minutes}分";

            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}分{duration.Seconds}秒";

            return $"{Math.Max(1, duration.Seconds)}秒";
        }

        private static bool PromptContinueDownload(int elapsedMinutes)
        {
            try
            {
                var result = System.Windows.Forms.MessageBox.Show(
                    $"ダウンロードが継続していますが、まだ終わっていません。\n{elapsedMinutes}分経過しました。継続しますか？",
                    "ダウンロード継続確認",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return result == System.Windows.Forms.DialogResult.Yes;
            }
            catch
            {
                // UI が不可能な環境では継続扱いにする
                return true;
            }
        }

        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        private static readonly Regex VolumeKanPattern = new(@"第?\s*(\d+)\s*巻", RegexOptions.Compiled);
        private static readonly Regex VolumeLatinPattern = new(@"(?:vol(?:ume)?\.?\s*)(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VolumeParenPattern = new(@"[\(（]\s*(\d+)\s*[\)）]", RegexOptions.Compiled);
        private static readonly Regex VolumeTailNumberPattern = new(@"(\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex VolumeHeadNumberPattern = new(@"^(\d+)\b", RegexOptions.Compiled);

        internal static string NormalizeFullWidthDigits(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c >= '０' && c <= '９')
                    sb.Append((char)(c - '０' + '0'));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        internal static int ExtractVolumeNumber(string filePath)
        {
            string rawName = Path.GetFileNameWithoutExtension(filePath);
            string name = NormalizeFullWidthDigits(rawName);

            // 1. 第?(\d+)巻 (例: 第01巻, 1巻, 第１巻, １０巻)
            Match m1 = VolumeKanPattern.Match(name);
            if (m1.Success && int.TryParse(m1.Groups[1].Value, out var v1))
                return v1;

            // 2. Vol.?\s*(\d+) または Volume\s*(\d+) (例: Vol1, VOl2, Vol10, vol.11)
            Match m2 = VolumeLatinPattern.Match(name);
            if (m2.Success && int.TryParse(m2.Groups[1].Value, out var v2))
                return v2;

            // 3. 括弧内の数字 (例: "タイトル (1)", "タイトル（2）")
            Match m3 = VolumeParenPattern.Match(name);
            if (m3.Success && int.TryParse(m3.Groups[1].Value, out var v3))
                return v3;

            // 4. タイトル末尾の数字 (例: "タイトル 01", "タイトル_10")
            Match m4 = VolumeTailNumberPattern.Match(name);
            if (m4.Success && int.TryParse(m4.Groups[1].Value, out var v4))
                return v4;

            // 5. 先頭の数字 (例: "01 タイトル")
            Match m5 = VolumeHeadNumberPattern.Match(name);
            if (m5.Success && int.TryParse(m5.Groups[1].Value, out var v5))
                return v5;

            return int.MaxValue; // 数値なしは末尾に配置
        }

        internal class CbzVolumeComparer : IComparer<string>
        {
            public int Compare(string? xFile, string? yFile)
            {
                if (xFile == null && yFile == null) return 0;
                if (xFile == null) return -1;
                if (yFile == null) return 1;

                int vx = ExtractVolumeNumber(xFile);
                int vy = ExtractVolumeNumber(yFile);
                bool hasVx = vx != int.MaxValue;
                bool hasVy = vy != int.MaxValue;

                if (hasVx && hasVy && vx != vy)
                    return vx.CompareTo(vy);

                // 巻番号が抽出できるファイルを優先して前方に寄せる。
                if (hasVx != hasVy)
                    return hasVx ? -1 : 1;

                // 自然順比較 (StrCmpLogicalW: 1 -> 2 -> 10 -> 11 -> 100)
                string nx = NormalizeFullWidthDigits(Path.GetFileName(xFile));
                string ny = NormalizeFullWidthDigits(Path.GetFileName(yFile));
                try
                {
                    int res = StrCmpLogicalW(nx, ny);
                    if (res != 0) return res;
                }
                catch
                {
                    // フォールバック
                }

                return string.Compare(nx, ny, StringComparison.OrdinalIgnoreCase);
            }
        }

        public void Dispose()
        {
            CbxFiles.Clear();
            CurrentImagePaths.Clear();
            _imagePathsByCbz.Clear();
            _cbzByCacheDirectory.Clear();
        }

        private void NotifyCacheChanged()
        {
            try
            {
                CacheChanged?.Invoke();
            }
            catch
            {
                // タイトル更新の失敗でCBZ処理を中断しない。
            }
        }

        public void LoadCbx(string cbzFile)
        {
            if (string.IsNullOrEmpty(cbzFile)) return;

            int index = CbxFiles.FindIndex(x => string.Equals(x, cbzFile, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                ActiveCbxIndex = index;
                RefreshCurrentImagePaths(extractEvenIfEmpty: true);
            }
        }
    }
}
