using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MangaViewer.Android.Services;

internal static class CbzCacheHelper
{
	internal const string CacheCompleteMarkerFileName = ".complete";
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheExtractionLocks = new();

	internal static string ComputeCacheHash(string cbzFile)
	{
		using var md5 = MD5.Create();
		var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(cbzFile));
		return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
	}

	internal static string GetArea1CacheDirectory(string cacheRootArea1, string cbzFile) =>
		Path.Combine(cacheRootArea1, ComputeCacheHash(cbzFile));

	internal static string GetArea2CacheDirectory(string cacheRootArea2, string cbzFile) =>
		Path.Combine(cacheRootArea2, ComputeCacheHash(cbzFile));

	internal static string GetPreferredCacheDirectory(string cacheRootArea1, string cbzFile)
	{
		string area1 = GetArea1CacheDirectory(cacheRootArea1, cbzFile);
		if (IsCacheReady(area1))
			return area1;

		return area1;
	}

	internal static bool IsCacheReady(string cacheDir)
	{
		if (!Directory.Exists(cacheDir))
			return false;

		string markerPath = Path.Combine(cacheDir, CacheCompleteMarkerFileName);
		if (!File.Exists(markerPath))
			return false;

		return Directory.EnumerateFiles(cacheDir, "*.*", SearchOption.AllDirectories)
			.Any(path => !string.Equals(Path.GetFileName(path), CacheCompleteMarkerFileName, StringComparison.OrdinalIgnoreCase));
	}

	internal static bool EnsureCacheExtracted(string cacheDir, string cbzFile, Func<Stream> openReadStream, Action<string>? statusCallback = null)
	{
		var extractionLock = CacheExtractionLocks.GetOrAdd(cacheDir, _ => new SemaphoreSlim(1, 1));
		var waitStartedAt = Stopwatch.StartNew();
		while (!extractionLock.Wait(2000))
		{
			statusCallback?.Invoke(CbzStatusHelper.BuildDownloadStatus(cbzFile, 0, 0, waitStartedAt.Elapsed));
		}

		if (waitStartedAt.ElapsedMilliseconds >= 2000)
			statusCallback?.Invoke(CbzStatusHelper.BuildDownloadStatus(cbzFile, 0, 0, waitStartedAt.Elapsed));

		var startedAt = Stopwatch.StartNew();
		try
		{
			if (IsCacheReady(cacheDir))
				return false;

			if (Directory.Exists(cacheDir))
				Directory.Delete(cacheDir, recursive: true);

			string temporaryDirectory = cacheDir + ".extracting";
			if (Directory.Exists(temporaryDirectory))
				Directory.Delete(temporaryDirectory, recursive: true);

			ExtractCbzTo(temporaryDirectory, cbzFile, openReadStream, startedAt, statusCallback);
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
			}
			return false;
		}
		finally
		{
			extractionLock.Release();
		}
	}

	private static void ExtractCbzTo(string cacheDir, string cbzFile, Func<Stream> openReadStream, Stopwatch startedAt, Action<string>? statusCallback = null)
	{
		Directory.CreateDirectory(cacheDir);

		using var source = openReadStream();
		using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
		int totalEntries = archive.Entries.Count(entry => !string.IsNullOrEmpty(entry.Name));
		int processedEntries = 0;
		long lastStatusMs = 0;
		int extractionCompleted = 0;

		using var periodicStatusTimer = statusCallback == null
			? null
			: new Timer(_ =>
			{
				if (Volatile.Read(ref extractionCompleted) != 0)
					return;

				int processedSnapshot = Volatile.Read(ref processedEntries);
				try
				{
					statusCallback(CbzStatusHelper.BuildDownloadStatus(cbzFile, processedSnapshot, totalEntries, startedAt.Elapsed));
				}
				catch
				{
				}
			}, null, 2000, 2000);

		foreach (var entry in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry.Name)) continue;

			processedEntries++;

			var dest = Path.Combine(cacheDir, entry.FullName);
			if (!Path.GetFullPath(dest).StartsWith(Path.GetFullPath(cacheDir), StringComparison.Ordinal))
				continue;

			Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
			try
			{
				entry.ExtractToFile(dest, overwrite: true);
			}
			catch (UnauthorizedAccessException)
			{
			}
			catch
			{
			}

			if (statusCallback != null)
			{
				long elapsedMs = (long)startedAt.Elapsed.TotalMilliseconds;
				if (processedEntries == 1 || processedEntries == totalEntries || elapsedMs - lastStatusMs >= 2000)
				{
					lastStatusMs = elapsedMs;
					statusCallback(CbzStatusHelper.BuildDownloadStatus(cbzFile, processedEntries, totalEntries, startedAt.Elapsed));
				}
			}
		}

		Volatile.Write(ref extractionCompleted, 1);
		statusCallback?.Invoke($"表示準備完了: {Path.GetFileName(cbzFile)}");
	}
}
