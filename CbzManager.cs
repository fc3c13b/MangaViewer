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
        private readonly CbzRuntimeState _state = new();
        private readonly CbzCacheCoordinator _cacheCoordinator = new();

        private string _cacheRootArea1 => _cacheCoordinator.CacheRootArea1;
        private string _cacheRootArea2 => _cacheCoordinator.CacheRootArea2;

        private const int CurrentTitleCacheTargetCount = 3;
        private const int NextTitleCacheTargetCount = 2;
        internal const string CacheCompleteMarkerFileName = ".complete";
        private static readonly SemaphoreSlim SlowSourceExtractionLock = new(1, 1);

        internal List<string> CbxFiles
        {
            get => _state.CbxFiles;
            set => _state.CbxFiles = value;
        }

        internal int ActiveCbxIndex
        {
            get => _state.ActiveCbxIndex;
            set => _state.ActiveCbxIndex = value;
        }

        internal List<string> CurrentImagePaths
        {
            get => _state.CurrentImagePaths;
            set => _state.CurrentImagePaths = value;
        }

        internal string? CurrentFolder
        {
            get => _state.CurrentFolder;
            private set => _state.CurrentFolder = value;
        }

        // A cache directory is immutable while the application is running.  Keep the
        // sorted file list in memory so repeated navigation never re-enumerates it.
        private readonly Dictionary<string, List<string>> _imagePathsByCbz =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _cbzByCacheDirectory =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly CbzPreloadCoordinator _preloadCoordinator = new();
        private long _imagePathCacheHits;
        private long _imagePathCacheMisses;

        /// <summary>展開キャッシュの追加・削除後に、UIへ表示更新を通知する。</summary>
        public event Action? CacheChanged;
        /// <summary>CBZ読み出し・展開中の状態をUIへ通知する。ファイル名とメッセージをセットする。</summary>
        public event Action<string, string>? CacheStatusChanged;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        public CbzManager()
        {
            // cache root selection is owned by CbzCacheCoordinator.
        }

        /// <summary>
        /// アプリ起動時に全てのCBZ展開キャッシュを削除します。
        /// </summary>
        public static void ClearAllCache()
        {
            CbzCacheCoordinator.ClearAllCache();
        }

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
            return _preloadCoordinator.PreloadNextCbx(this);
        }

        /// <summary>
        /// 次のリスト項目用に、指定フォルダの先頭CBZだけを低優先度で展開する。
        /// 現在選択中のCBZ状態は変更しない。
        /// </summary>
        public void PreloadFirstCbxForFolder(string folderPath)
        {
            _preloadCoordinator.PreloadFirstCbxForFolder(this, folderPath);
        }

        /// <summary>
        /// フォルダ移動時の先読みを実行する。
        /// 1) 現在タイトル: 先頭3件をキャッシュ作成
        /// 2) 次タイトル: 先頭2件をキャッシュ作成
        /// すべてエリア1に保存する。
        /// </summary>
        public void PreloadForNavigationContext(string currentFolderPath, int area1Count, string? nextFolderPath)
        {
            _preloadCoordinator.PreloadForNavigationContext(this, currentFolderPath, area1Count, nextFolderPath);
        }

        /// <summary>
        /// キャッシュ取得の唯一の入口。
        /// - 既存キャッシュはそのまま利用
        /// - 未作成なら展開して .complete を確定
        /// - 作成成功時のみ作成ログ/trim/更新イベントを一括発火
        /// </summary>
        internal bool TryAcquireCache(string cbzPath, string trigger, params string?[] protectedCacheDirs)
        {
            string cacheDir = GetArea1CacheDirectory(cbzPath);
            if (CbzCacheHelper.IsCacheReady(cacheDir))
                return false;

            Action<string>? statusCallback = string.Equals(trigger, "current", StringComparison.OrdinalIgnoreCase)
                ? message => NotifyCacheStatus(cbzPath, message)
                : null;

            if (!CbzCacheHelper.EnsureCacheExtracted(cacheDir, cbzPath, statusCallback))
                return false;

            CbzStatusHelper.WriteCacheCreateLog(_cacheRootArea2, cbzPath, cacheDir, trigger);
            var allProtected = new List<string?> { cacheDir };
            if (protectedCacheDirs != null && protectedCacheDirs.Length > 0)
                allProtected.AddRange(protectedCacheDirs);
            TrimCacheDirectories(allProtected.ToArray());
            NotifyCacheChanged();
            return true;
        }

        internal void NotifyCacheStatus(string cbzPath, string message)
        {
            CbzStatusHelper.NotifyCacheStatus(CacheStatusChanged, cbzPath, message);
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
            _state.Reset();
            // CurrentImagePaths can reference an entry in _imagePathsByCbz.
            // Do not clear that shared list when changing folders; only detach
            // the current-view reference so the cached image paths remain valid.
            CurrentImagePaths = new List<string>();
        }

        internal void RefreshCurrentImagePaths(bool extractEvenIfEmpty, bool preloadNext = true, bool showWarnings = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!CbxFiles.Any())
            {
                CurrentImagePaths.Clear();
                _state.LoadedCbzFile = null;
                return;
            }

            if (ActiveCbxIndex < 0 || ActiveCbxIndex >= CbxFiles.Count)
            {
                // Clamp index
                ActiveCbxIndex = Math.Clamp(ActiveCbxIndex, 0, CbxFiles.Count - 1);
            }

            var cbzFile = CbxFiles[ActiveCbxIndex];

            // Never block the current-volume display behind neighbor preloads.
            // Once the required cache is ready, the current image list should be
            // unveiled immediately; +1/+2 preloads are opportunistic and should
            // start only after the active volume has been served.

            // The common path: callers frequently ask for the already-selected
            // volume. Avoid both disk enumeration and list allocation.
            if (_state.LoadedCbzFile == cbzFile && _imagePathsByCbz.TryGetValue(cbzFile, out var cached))
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

            if (_imagePathsByCbz.TryGetValue(cbzFile, out var cachedPaths) && CbzCacheHelper.IsCacheReady(cacheDir))
            {
                if (cachedPaths.Count > 0)
                {
                    _imagePathCacheHits++;
                    CbzStatusHelper.NotifyCacheStatus(CacheStatusChanged, cbzFile, $"Cache読込中: {Path.GetFileName(cbzFile)}");
                    SetCurrentImagePaths(cbzFile, cachedPaths);
                    _cbzByCacheDirectory[cacheDir] = cbzFile;
                    StartupHandler.WriteStartupLog($"[PROF] CbzManager.ImagePaths cbz={Path.GetFileName(cbzFile)} source=memory images={cachedPaths.Count} total={sw.ElapsedMilliseconds}ms");
                    if (preloadNext)
                    {
                        ScheduleNeighborPreloads(ActiveCbxIndex);
                        _ = PreloadNextCbzIfAvailableAsync();
                    }
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
            bool cacheDirectoryExisted = CbzCacheHelper.IsCacheReady(cacheDir);
            long extractMs = 0;

            if (!cacheDirectoryExisted)
            {
                cacheDir = primaryCacheDir;
                // キャッシュミス→展開
                StartupHandler.WriteStartupLog($"[CBZ-STARTUP] Cache MISS: {cbzFile}");
                try
                {
                    CbzStatusHelper.NotifyCacheStatus(CacheStatusChanged, cbzFile, CbzStatusHelper.BuildDownloadStatus(cbzFile, 0, 0, TimeSpan.Zero));
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
                CbzStatusHelper.NotifyCacheStatus(CacheStatusChanged, cbzFile, $"Cache読込中: {Path.GetFileName(cbzFile)}");
            }

            if (!CbzCacheHelper.IsCacheReady(cacheDir))
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
                if (preloadNext)
                {
                    ScheduleNeighborPreloads(ActiveCbxIndex);
                    _ = PreloadNextCbzIfAvailableAsync();
                }
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
            if (preloadNext)
            {
                ScheduleNeighborPreloads(ActiveCbxIndex);
                _ = PreloadNextCbzIfAvailableAsync();
            }
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

        private void ScheduleNeighborPreloads(int activeIndex)
        {
            _preloadCoordinator.ScheduleNeighborPreloads(this, activeIndex);
        }

        private void SetCurrentImagePaths(string cbzFile, List<string> imagePaths)
        {
            _state.SetCurrentImagePaths(cbzFile, imagePaths);
            StartupHandler.WriteStartupLog($"[CBZ] set-current file={Path.GetFileName(cbzFile)} activeIndex={ActiveCbxIndex} imageCount={imagePaths.Count}");
        }

        private bool TryRecoverToPlayableCbx(bool preferNext)
        {
            return CbzRecoveryHelper.TryRecoverToPlayableCbx(this, preferNext);
        }

        /// <summary>
        /// キャッシュはアクセス順ではなく、展開された順（作成時刻）で保持する。
        /// エリア1は最大4件、エリア2は最大2件を維持する。
        /// 現在表示中のCBZだけは削除対象から除外する。
        /// 新規作成キャッシュ（30秒以内）も削除対象から除外する（先読み直後の削除を防ぐ）。
        /// </summary>
        private void TrimCacheDirectories(params string?[] protectedCacheDirs)
        {
            _cacheCoordinator.TrimCacheDirectories(protectedCacheDirs);
        }

        private bool TryResolveCacheArea(string cacheDir, out string rootPath, out int maxCount)
        {
            return _cacheCoordinator.TryResolveCacheArea(cacheDir, out rootPath, out maxCount);
        }

        /// <summary>
        /// 現在巻・次巻・次リスト項目の先頭巻を、FIFO整理から保護する。
        /// 新しい候補を優先し、保持対象は対象エリアの上限件数までに限定する。
        /// </summary>
        internal void ProtectCacheDirectory(string cacheDir)
        {
            _cacheCoordinator.ProtectCacheDirectory(cacheDir);
        }

        /// <summary>先読みを一元管理する。完了済み・展開中の重複要求はスキップ。</summary>
        private void RequestPreload(string cbzPath, CbzPreloadCoordinator.PreloadPriority priority)
        {
            _preloadCoordinator.RequestPreload(this, cbzPath, priority);
        }

        private async Task PreloadNextCbzIfAvailableAsync()
        {
            await _preloadCoordinator.PreloadNextCbzIfAvailableAsync(this);
        }

        internal string GetArea1CacheDirectory(string cbzFile) =>
            CbzCacheHelper.GetArea1CacheDirectory(_cacheRootArea1, cbzFile);

        internal string GetArea2CacheDirectory(string cbzFile) =>
            CbzCacheHelper.GetArea2CacheDirectory(_cacheRootArea2, cbzFile);

        internal string GetPreferredCacheDirectory(string cbzFile)
        {
            return CbzCacheHelper.GetPreferredCacheDirectory(_cacheRootArea1, cbzFile);
        }

        internal string GetPreloadCacheDirectory(string cbzFile, CbzPreloadCoordinator.PreloadPriority priority)
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

        private static bool RequiresSerializedDownload(string cbzFile)
        {
            if (string.IsNullOrWhiteSpace(cbzFile))
                return false;

            return cbzFile.StartsWith("O:\\", StringComparison.OrdinalIgnoreCase);
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
