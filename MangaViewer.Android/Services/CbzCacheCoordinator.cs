using System.Diagnostics;

namespace MangaViewer.Android.Services;

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
		string primaryRoot = GetPreferredRoot("cbz-cache-area1", "MangaViewer_Cache");
		string secondaryRoot = GetPreferredRoot("cbz-cache-area2", "MangaViewer_Cache_Area2");

		CacheRootArea1 = primaryRoot;
		CacheRootArea2 = secondaryRoot;
		try { Directory.CreateDirectory(CacheRootArea1); } catch { }
		try { Directory.CreateDirectory(CacheRootArea2); } catch { }
	}

	internal static void ClearAllCache()
	{
		string root1 = GetPreferredRoot("cbz-cache-area1", "MangaViewer_Cache");
		string root2 = GetPreferredRoot("cbz-cache-area2", "MangaViewer_Cache_Area2");

		if (Directory.Exists(root1))
		{
			try
			{
				int count = Directory.GetDirectories(root1).Length;
				Directory.Delete(root1, recursive: true);
				Debug.WriteLine($"[CACHE-CLEAR] area=area1 deletedDirs={count} root={root1}");
			}
			catch
			{
			}
		}

		if (Directory.Exists(root2))
		{
			try
			{
				int count = Directory.GetDirectories(root2).Length;
				Directory.Delete(root2, recursive: true);
				Debug.WriteLine($"[CACHE-CLEAR] area=area2 deletedDirs={count} root={root2}");
			}
			catch
			{
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
					Debug.WriteLine($"[CACHE-DELETE] area={(string.Equals(targetRoot, CacheRootArea2, StringComparison.OrdinalIgnoreCase) ? "area2" : "area1")} dir={oldest}");
				}
				catch
				{
					deleteFailed++;
				}
			}
		}
		catch
		{
		}
	}

	private static string GetPreferredRoot(string externalFolder, string fallbackName)
	{
		try
		{
			var externalDir = global::Android.App.Application.Context.GetExternalFilesDir(externalFolder);
			if (externalDir != null && !string.IsNullOrWhiteSpace(externalDir.AbsolutePath))
				return externalDir.AbsolutePath;
		}
		catch
		{
		}

		string fallback = Path.Combine(FileSystem.CacheDirectory, fallbackName);
		Directory.CreateDirectory(fallback);
		return fallback;
	}
}
