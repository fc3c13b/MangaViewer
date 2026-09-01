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
        private readonly string _cacheRoot;

        /// <summary>キャッシュに保持する最大CBZ数</summary>
        private const int MaxCachedCbzCount = 5;
        internal const string CacheCompleteMarkerFileName = ".complete";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheExtractionLocks = new();

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
        private const int PriorityCacheDirCount = 5;
        private enum PreloadPriority { Current = 0, NextVolume = 1, NextFolder = 2 }
        private readonly ConcurrentDictionary<string, PreloadPriority> _pendingPreloads = new(StringComparer.OrdinalIgnoreCase);
        private string? _pendingNextFolderPath; // NextVolume完了後に処理するフォルダ（NAS/低帯域対応）

        /// <summary>展開キャッシュの追加・削除後に、UIへ表示更新を通知する。</summary>
        public event Action? CacheChanged;

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

        internal string? CurrentFolder { get; private set; }

        /// <summary>
        /// Initialize for a folder with .cbz/.zip files. Returns true if CBZ mode is active.
        /// </summary>
        public bool InitializeForFolder(string folderPath, bool forceReset = true)
        {
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

                CbxFiles = cbzs;
                ActiveCbxIndex = 0;
                // 起動時は先頭（現在）巻だけを準備する。次巻の展開は非同期で
                // 先読みし、全巻を起動時に展開しない。
                RefreshCurrentImagePaths(extractEvenIfEmpty: true);

                long tRefreshEnd = sw.ElapsedMilliseconds;
                StartupHandler.Log($"[PROF] CbzManager.InitializeForFolder total={tRefreshEnd}ms scan={tScanEnd - t0}ms refresh={tRefreshEnd - tScanEnd}ms cbzCount={cbzs.Count} folder={folderPath}");

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

        /// <summary>
        /// 次のCBZに強制的に切り替えます。先頭画像のパスを返します（失敗時はnull）。
        /// </summary>
        public string? SwitchToNextCbx()
        {
            if (CbxFiles.Count <= 1) return null;

            ActiveCbxIndex++;
            if (ActiveCbxIndex >= CbxFiles.Count)
            {
                ActiveCbxIndex = CbxFiles.Count - 1;
                return null;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: true);
            return CurrentImagePaths.FirstOrDefault();
        }

        /// <summary>
        /// 前のCBZに強制的に切り替えます。先頭画像のパスを返します（失敗時はnull）。
        /// </summary>
        public string? SwitchToPreviousCbx()
        {
            if (CbxFiles.Count <= 1) return null;

            ActiveCbxIndex--;
            if (ActiveCbxIndex < 0)
            {
                ActiveCbxIndex = 0;
                return null;
            }

            RefreshCurrentImagePaths(extractEvenIfEmpty: true);
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
                ProtectCacheDirectory(GetCacheDirectory(CbxFiles[ActiveCbxIndex]));
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

            // NextVolume先読み中は待機し、完了後に連鎖する（並行読み出し禁止）
            _pendingNextFolderPath = folderPath;
            if (!_pendingPreloads.Values.Any(p => p == PreloadPriority.NextVolume))
                StartFolderPreloadIfPending();
        }

        private void StartFolderPreloadIfPending()
        {
            string? folderPath = Interlocked.Exchange(ref _pendingNextFolderPath, null);
            if (string.IsNullOrEmpty(folderPath)) return;

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
                    StartupHandler.Log($"[PROF] CbzManager.StartFolderPreloadIfPending failed folder={folderPath} error={ex.GetType().Name}");
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

        private void RefreshCurrentImagePaths(bool extractEvenIfEmpty, bool preloadNext = true)
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
            if (string.Equals(_loadedCbzFile, cbzFile, StringComparison.OrdinalIgnoreCase))
            {
                _imagePathCacheHits++;
                return;
            }

            string cacheDir;

            try
            {
                cacheDir = GetCacheDirectory(cbzFile);
                ProtectCacheDirectory(cacheDir);
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
                _imagePathCacheHits++;
                SetCurrentImagePaths(cbzFile, cachedPaths);
                _cbzByCacheDirectory[cacheDir] = cbzFile;
                StartupHandler.Log($"[PROF] CbzManager.ImagePaths cbz={Path.GetFileName(cbzFile)} source=memory images={cachedPaths.Count} total={sw.ElapsedMilliseconds}ms");
                if (preloadNext) PreloadNextCbzIfAvailable();
                return;
            }

            // The disk cache may have been evicted while this manager was kept
            // alive. Do not return file paths for a directory that no longer exists.
            _imagePathsByCbz.Remove(cbzFile);
            _cbzByCacheDirectory.Remove(cacheDir);

            _imagePathCacheMisses++;
            bool cacheDirectoryExisted = IsCacheReady(cacheDir);
            long extractMs = 0;

            if (!cacheDirectoryExisted)
            {
                // キャッシュミス→展開
                System.Diagnostics.Debug.WriteLine($"[CBZ] Cache MISS: {Path.GetFileName(cbzFile)}");
                try
                {
                    var extractSw = System.Diagnostics.Stopwatch.StartNew();
                    bool extractedNow = EnsureCacheExtracted(cacheDir, cbzFile);
                    extractMs = extractSw.ElapsedMilliseconds;
                    if (extractedNow)
                    {
                        TrimCacheDirectories(cacheDir);
                        NotifyCacheChanged();
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
                    return;
                }
                catch
                {
                    // その他エラー→空扱い
                    SetCurrentImagePaths(cbzFile, new List<string>());
                    return;
                }
            }
            else
            {
                // キャッシュヒット
                System.Diagnostics.Debug.WriteLine($"[CBZ] Cache HIT: {Path.GetFileName(cbzFile)}");
            }

            if (!IsCacheReady(cacheDir))
            {
                SetCurrentImagePaths(cbzFile, new List<string>());
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
                StartupHandler.Log($"[PROF] CbzManager.ImagePaths cbz={Path.GetFileName(cbzFile)} source={source} images={imagePaths.Count} extract={extractMs}ms enumerate={enumerateMs}ms total={sw.ElapsedMilliseconds}ms");
            }
            catch (UnauthorizedAccessException)
            {
                // 権限不足→このCBZは表示不可として扱う
                SetCurrentImagePaths(cbzFile, new List<string>());
            }
            catch
            {
                // その他エラー→空扱い
                SetCurrentImagePaths(cbzFile, new List<string>());
            }

            // 次のCBZがあれば自動的に展開（連続プリロード）
            if (preloadNext) PreloadNextCbzIfAvailable();
        }

        private void SetCurrentImagePaths(string cbzFile, List<string> imagePaths)
        {
            CurrentImagePaths = imagePaths;
            _loadedCbzFile = cbzFile;
        }

        /// <summary>
        /// キャッシュはアクセス順ではなく、展開された順（作成時刻）で最大5件を保持する。
        /// 現在表示中のCBZだけは削除対象から除外する。
        /// 新規作成キャッシュ（30秒以内）も削除対象から除外する（先読み直後の削除を防ぐ）。
        /// </summary>
        private void TrimCacheDirectories(params string?[] protectedCacheDirs)
        {
            try
            {
                HashSet<string> retainedCacheDirs;
                lock (_retentionLock)
                {
                    retainedCacheDirs = new HashSet<string>(_priorityCacheDirs, StringComparer.OrdinalIgnoreCase);
                }
                foreach (var protectedDir in protectedCacheDirs)
                {
                    if (!string.IsNullOrEmpty(protectedDir))
                        retainedCacheDirs.Add(protectedDir);
                }

                var cacheDirs = Directory.GetDirectories(_cacheRoot)
                    .Where(IsCacheReady)
                    .OrderBy(Directory.GetCreationTimeUtc)
                    .ToList();
                int remainingCount = cacheDirs.Count;
                var now = DateTime.UtcNow;

                foreach (var oldest in cacheDirs)
                {
                    if (remainingCount <= MaxCachedCbzCount)
                        break;
                    if (retainedCacheDirs.Contains(oldest))
                        continue;

                    // 新規作成キャッシュ（30秒以内）は削除しない（先読み直後の削除を防ぐ）
                    try
                    {
                        var creationTime = Directory.GetCreationTimeUtc(oldest);
                        if ((now - creationTime).TotalSeconds < 30)
                            continue;
                    }
                    catch
                    {
                        // 時刻取得失敗時は削除対象とする
                    }

                    try
                    {
                        Directory.Delete(oldest, recursive: true);
                        remainingCount--;
                    }
                    catch
                    {
                        // 削除失敗は許容（使用中など）
                    }
                }
            }
            catch
            {
                // キャッシュ整理に失敗しても現在巻の表示は継続する。
            }
        }

        /// <summary>
        /// 現在巻・次巻・次リスト項目の先頭巻を、FIFO整理から保護する。
        /// 新しい候補を優先し、保持対象は最大5件に限定する。
        /// </summary>
        private void ProtectCacheDirectory(string cacheDir)
        {
            lock (_retentionLock)
            {
                _priorityCacheDirs.RemoveAll(dir =>
                    string.Equals(dir, cacheDir, StringComparison.OrdinalIgnoreCase));
                _priorityCacheDirs.Add(cacheDir);
                while (_priorityCacheDirs.Count > PriorityCacheDirCount)
                    _priorityCacheDirs.RemoveAt(0);
            }
        }

        /// <summary>先読みを一元管理する。完了済み・展開中の重複要求はスキップ。</summary>
        private void RequestPreload(string cbzPath, PreloadPriority priority)
        {
            if (string.IsNullOrEmpty(cbzPath)) return;

            string cacheDir;
            try { cacheDir = GetCacheDirectory(cbzPath); }
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
                ? GetCacheDirectory(CbxFiles[ActiveCbxIndex])
                : null;
            if (currentCacheDir != null) ProtectCacheDirectory(currentCacheDir);
            ProtectCacheDirectory(cacheDir);

            Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (EnsureCacheExtracted(cacheDir, cbzPath))
                    {
                        TrimCacheDirectories(cacheDir, currentCacheDir);
                        NotifyCacheChanged();
                        StartupHandler.Log($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=extracted total={sw.ElapsedMilliseconds}ms");
                    }
                    else
                    {
                        StartupHandler.Log($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} source=disk-cache total={sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    StartupHandler.Log($"[PROF] CbzManager.Preload cbz={Path.GetFileName(cbzPath)} pri={priority} failed error={ex.GetType().Name}");
                }
                finally
                {
                    _pendingPreloads.TryRemove(cacheDir, out _);
                    // NextVolume完了後にフォルダ先読みを連鎖（順次実行）
                    if (priority == PreloadPriority.NextVolume)
                        StartFolderPreloadIfPending();
                }
            });
        }

        private void PreloadNextCbzIfAvailable()
        {
            if (ActiveCbxIndex + 1 >= CbxFiles.Count) return;
            RequestPreload(CbxFiles[ActiveCbxIndex + 1], PreloadPriority.NextVolume);
        }

        private string GetCacheDirectory(string cbzFile)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(cbzFile));
            var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLower();
            return Path.Combine(_cacheRoot, hashStr);
        }

        private static bool IsCacheReady(string cacheDir) =>
            Directory.Exists(cacheDir) && File.Exists(Path.Combine(cacheDir, CacheCompleteMarkerFileName));

        /// <summary>
        /// 展開先とは別の一時ディレクトリに展開し、完了後に原子的に確定する。
        /// 同じCBZへの同時アクセスは待機させ、不完全なキャッシュを公開しない。
        /// </summary>
        private static bool EnsureCacheExtracted(string cacheDir, string cbzFile)
        {
            var extractionLock = CacheExtractionLocks.GetOrAdd(cacheDir, _ => new SemaphoreSlim(1, 1));
            extractionLock.Wait();
            try
            {
                if (IsCacheReady(cacheDir))
                    return false;

                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, recursive: true);

                string temporaryDirectory = cacheDir + ".extracting";
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);

                ExtractCbzTo(temporaryDirectory, cbzFile);
                File.WriteAllText(Path.Combine(temporaryDirectory, CacheCompleteMarkerFileName), string.Empty);
                Directory.Move(temporaryDirectory, cacheDir);
                return true;
            }
            finally
            {
                extractionLock.Release();
            }
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

        private static int ExtractVolumeNumber(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);

            // 第(\d+)巻
            Match m1 = Regex.Match(name, @"第(\d+)巻");
            if (m1.Success && int.TryParse(m1.Groups[1].Value, out var v1))
                return v1;

            // Vol.?\s*(\d+)
            Match m2 = Regex.Match(name, @"Vol\.?\s*(\d+)", RegexOptions.IgnoreCase);
            if (m2.Success && int.TryParse(m2.Groups[1].Value, out var v2))
                return v2;

            // 先頭が数字の場合 ^(\d+)
            Match m3 = Regex.Match(name, @"^(\d+)");
            if (m3.Success && int.TryParse(m3.Groups[1].Value, out var v3))
                return v3;

            return int.MaxValue; // 数値なしは末尾に配置
        }

        private class CbzVolumeComparer : IComparer<string>
        {
            public int Compare(string? xFile, string? yFile)
            {
                if (xFile == null || yFile == null)
                    return 0;

                int vx = ExtractVolumeNumber(xFile);
                int vy = ExtractVolumeNumber(yFile);

                if (vx != vy && vx != int.MaxValue && vy != int.MaxValue)
                    return vx.CompareTo(vy);

                // 片方または両方が数値なしの場合は元の文字列順序で安定化
                return string.Compare(Path.GetFileName(xFile), Path.GetFileName(yFile), StringComparison.Ordinal);
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
    }
}
