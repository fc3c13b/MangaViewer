using System.Diagnostics;
using System.IO.Compression;
using MangaViewer.Android.Models;

namespace MangaViewer.Android.Services;

public sealed class ArchiveImageCacheService
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private readonly CbzCacheCoordinator _cacheCoordinator;
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lockGate = new();

    public ArchiveImageCacheService()
    {
        _cacheCoordinator = new CbzCacheCoordinator();
    }

    public async Task<IReadOnlyList<string>> GetImagesFromLocalArchivesAsync(
        IReadOnlyList<string> archiveUris,
        LocalStorageBrowserService localStorage,
        CancellationToken cancellationToken = default,
        Action<string>? statusReporter = null)
    {
        var all = new List<string>();
        int total = archiveUris.Count;
        for (int archiveIndex = 0; archiveIndex < total; archiveIndex++)
        {
            string archiveUri = archiveUris[archiveIndex];
            try
            {
                statusReporter?.Invoke($"CBZ読込中: {Path.GetFileName(archiveUri)} ({archiveIndex + 1}/{total})");
                var paths = await EnsureArchiveExtractedAsync(
                    archiveKey: "local|" + archiveUri,
                    displayName: archiveUri,
                    openStreamAsync: () =>
                    {
                        Stream? stream = localStorage.OpenRead(archiveUri);
                        if (stream == null)
                            throw new IOException("ローカルCBZを開けませんでした。");
                        return Task.FromResult(stream);
                    },
                    cancellationToken,
                    statusReporter: statusReporter);

                all.AddRange(paths);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchiveImageCache] local archive failed: {archiveUri} | {ex.GetType().Name}: {ex.Message}");
            }
        }

        return all;
    }

    public async Task<IReadOnlyList<string>> GetImagesFromSambaArchivesAsync(
        IReadOnlyList<string> archivePaths,
        SambaConnectionConfig config,
        SambaBrowserService sambaBrowser,
        CancellationToken cancellationToken = default,
        Action<string>? statusReporter = null)
    {
        var all = new List<string>();
        int timeoutSeconds = Math.Clamp(config.TimeoutSeconds <= 0 ? 300 : config.TimeoutSeconds, 5, 300);
        int total = archivePaths.Count;
        for (int archiveIndex = 0; archiveIndex < total; archiveIndex++)
        {
            string archivePath = archivePaths[archiveIndex];
            try
            {
                statusReporter?.Invoke($"CBZ読込中: {Path.GetFileName(archivePath)} ({archiveIndex + 1}/{total})");
                var paths = await EnsureArchiveExtractedAsync(
                    archiveKey: "samba|" + archivePath,
                    displayName: archivePath,
                    openStreamAsync: async () => await OpenSambaStreamWithRetryAsync(
                        sambaBrowser,
                        config,
                        archivePath,
                        timeoutSeconds,
                        cancellationToken),
                    cancellationToken,
                    copyTimeout: TimeSpan.FromSeconds(timeoutSeconds),
                    statusReporter: statusReporter);

                all.AddRange(paths);
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"[ArchiveImageCache] samba archive timeout: {archivePath} | {ex.Message}");
            }
            catch (OperationCanceledException ex)
            {
                Debug.WriteLine($"[ArchiveImageCache] samba archive canceled: {archivePath} | {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchiveImageCache] samba archive failed: {archivePath} | {ex.GetType().Name}: {ex.Message}");
            }
        }

        return all;
    }

    private static async Task<Stream> OpenSambaStreamWithRetryAsync(
        SambaBrowserService sambaBrowser,
        SambaConnectionConfig config,
        string archivePath,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await Task
                    .Run(() => (Stream)sambaBrowser.OpenRead(config, archivePath), cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                await Task.Delay(300, cancellationToken);
            }
            catch (OperationCanceledException) when (attempt < maxAttempts)
            {
                await Task.Delay(300, cancellationToken);
            }
        }

        return await Task
            .Run(() => (Stream)sambaBrowser.OpenRead(config, archivePath), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
    }

    private async Task<IReadOnlyList<string>> EnsureArchiveExtractedAsync(
        string archiveKey,
        string displayName,
        Func<Task<Stream>> openStreamAsync,
        CancellationToken cancellationToken,
        TimeSpan? copyTimeout = null,
        Action<string>? statusReporter = null)
    {
        string cacheDir = GetCacheDirectory(archiveKey);
        string completeMarker = Path.Combine(cacheDir, CbzCacheHelper.CacheCompleteMarkerFileName);

        if (Directory.Exists(cacheDir) && File.Exists(completeMarker))
        {
            _cacheCoordinator.ProtectCacheDirectory(cacheDir);
            return ListExtractedImages(cacheDir);
        }

        SemaphoreSlim sem = GetLock(cacheDir);
        await sem.WaitAsync(cancellationToken);
        try
        {
            if (Directory.Exists(cacheDir) && File.Exists(completeMarker))
            {
                _cacheCoordinator.ProtectCacheDirectory(cacheDir);
                return ListExtractedImages(cacheDir);
            }

            string tempDir = cacheDir + ".extracting";
            DeleteDirectoryIfExists(cacheDir);
            DeleteDirectoryIfExists(tempDir);
            Directory.CreateDirectory(tempDir);

            string stepFile = Path.Combine(tempDir, ".step");
            File.WriteAllText(stepFile, "created-temp");

            string archiveFile = Path.Combine(tempDir, "archive.cbz");
            File.WriteAllText(stepFile, "open-source");
            await using (Stream source = await openStreamAsync())
            {
                if (source.CanSeek)
                {
                    if (source.Position != 0)
                        source.Position = 0;

                    File.WriteAllText(stepFile, "extracting-direct");
                    var directImages = await Task.Run(
                        () => ExtractImagesFromArchiveStream(source, tempDir, cancellationToken, statusReporter, displayName),
                        cancellationToken);
                    if (directImages.Count > 0)
                    {
                        File.WriteAllText(Path.Combine(tempDir, ".source"), displayName);
                        File.WriteAllText(stepFile, "finalizing-direct");
                        File.WriteAllText(Path.Combine(tempDir, CbzCacheHelper.CacheCompleteMarkerFileName), string.Empty);

                        DeleteDirectoryIfExists(cacheDir);
                        Directory.Move(tempDir, cacheDir);
                        _cacheCoordinator.ProtectCacheDirectory(cacheDir);
                        _cacheCoordinator.TrimCacheDirectories(cacheDir);
                        return directImages;
                    }

                    if (source.CanSeek)
                        source.Position = 0;
                }

                await using (var output = File.Create(archiveFile))
                {
                    File.WriteAllText(stepFile, "copying-archive");
                    if (copyTimeout.HasValue)
                    {
                        await CopyStreamWithTimeoutAsync(source, output, copyTimeout.Value, cancellationToken, statusReporter, displayName);
                    }
                    else
                    {
                        await CopyStreamWithTimeoutAsync(source, output, TimeSpan.FromMinutes(5), cancellationToken, statusReporter, displayName);
                    }
                }
            }

            File.WriteAllText(stepFile, "opening-zip");
            var extractedImages = await Task.Run(
                () => ExtractImagesFromArchiveFile(archiveFile, tempDir, cancellationToken, statusReporter, displayName).ToList(),
                cancellationToken);

            if (extractedImages.Count == 0)
                throw new InvalidOperationException("CBZ/ZIP に有効な画像が見つかりませんでした。");

            File.WriteAllText(Path.Combine(tempDir, ".source"), displayName);
            File.WriteAllText(stepFile, "finalizing");
            File.WriteAllText(Path.Combine(tempDir, CbzCacheHelper.CacheCompleteMarkerFileName), string.Empty);

            DeleteDirectoryIfExists(cacheDir);
            Directory.Move(tempDir, cacheDir);
            _cacheCoordinator.ProtectCacheDirectory(cacheDir);
            _cacheCoordinator.TrimCacheDirectories(cacheDir);
            return extractedImages;
        }
        catch
        {
            DeleteDirectoryIfExists(cacheDir + ".extracting");
            throw;
        }
        finally
        {
            sem.Release();
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> ListExtractedImages(string cacheDir)
    {
        return Directory
            .EnumerateFiles(cacheDir, "*.*", SearchOption.AllDirectories)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task CopyStreamWithTimeoutAsync(
        Stream source,
        Stream destination,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? statusReporter,
        string displayName)
    {
        byte[] buffer = new byte[1024 * 1024];
        var stopwatch = Stopwatch.StartNew();
        TimeSpan lastReportAt = TimeSpan.Zero;
        long totalBytes = source.CanSeek ? source.Length : -1;
        long copiedBytes = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copiedBytes += read;

                if (stopwatch.Elapsed > timeout)
                    throw new TimeoutException($"archive copy timeout after {timeout.TotalSeconds:F0}s");

                if (statusReporter != null)
                {
                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    double speed = elapsedSeconds > 0.1 ? copiedBytes / elapsedSeconds : 0;
                    double remainingSeconds = 0;
                    if (totalBytes > 0 && speed > 0)
                    {
                        remainingSeconds = Math.Max(0, (totalBytes - copiedBytes) / speed);
                    }

                    if (stopwatch.Elapsed - lastReportAt >= TimeSpan.FromSeconds(1))
                    {
                        lastReportAt = stopwatch.Elapsed;
                        string readableName = Path.GetFileName(displayName);
                        if (totalBytes > 0 && speed > 0)
                        {
                            statusReporter($"CBZ読込中: {readableName} | 残り約 {remainingSeconds:F0}秒");
                        }
                        else
                        {
                            statusReporter($"CBZ読込中: {readableName} | 進行中 ({copiedBytes / 1024.0 / 1024.0:F1}MB)");
                        }
                    }
                }
            }

            await destination.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException($"archive copy failed: {ex.Message}", ex);
        }
    }

    private static IReadOnlyList<string> ExtractImagesFromArchiveStream(
        Stream archiveStream,
        string tempDir,
        CancellationToken cancellationToken,
        Action<string>? statusReporter,
        string displayName)
    {
        try
        {
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            var extractedImages = new List<string>();
            int imageEntryCount = zip.Entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Name) && ImageExtensions.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()));
            int extractedCount = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (!ImageExtensions.Contains(ext))
                    continue;

                string destPath = Path.GetFullPath(Path.Combine(tempDir, entry.FullName));
                string normalizedRoot = Path.GetFullPath(tempDir);
                if (!destPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destPath, overwrite: true);
                extractedImages.Add(destPath);
                extractedCount++;

                if (imageEntryCount > 0)
                {
                    statusReporter?.Invoke($"CBZ先読み展開中: {Path.GetFileName(displayName)} | {extractedCount}/{imageEntryCount}");
                }
                else
                {
                    statusReporter?.Invoke($"CBZ先読み展開中: {Path.GetFileName(displayName)} | {extractedCount}");
                }
            }

            return extractedImages;
        }
        catch (InvalidDataException)
        {
            return Array.Empty<string>();
        }
        catch (NotSupportedException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ExtractImagesFromArchiveFile(
        string archiveFile,
        string tempDir,
        CancellationToken cancellationToken,
        Action<string>? statusReporter,
        string displayName)
    {
        using var source = File.OpenRead(archiveFile);
        return ExtractImagesFromArchiveStream(source, tempDir, cancellationToken, statusReporter, displayName);
    }

    private SemaphoreSlim GetLock(string cacheDir)
    {
        lock (_lockGate)
        {
            if (_locks.TryGetValue(cacheDir, out SemaphoreSlim? existing))
                return existing;

            var created = new SemaphoreSlim(1, 1);
            _locks[cacheDir] = created;
            return created;
        }
    }

    private string GetCacheDirectory(string archiveKey)
    {
        return _cacheCoordinator.GetArea1CacheDirectory(archiveKey);
    }
}
