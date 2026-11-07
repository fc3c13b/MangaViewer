using System.IO;

namespace MangaViewer.Android.Services;

internal static class CbzStatusHelper
{
	internal static void NotifyCacheStatus(Action<string, string>? callback, string cbzPath, string message)
	{
		try
		{
			callback?.Invoke(cbzPath, message);
		}
		catch
		{
		}
	}

	internal static string BuildDownloadStatus(string cbzFile, int processedEntries, int totalEntries, TimeSpan elapsed)
	{
		string fileName = Path.GetFileName(cbzFile);
		if (totalEntries <= 0 || processedEntries <= 0)
		{
			int elapsedSeconds = Math.Max(1, (int)Math.Ceiling(Math.Max(1.0, elapsed.TotalSeconds)));
			return $"DL中: {fileName} 残り時間算出中 (経過{elapsedSeconds}秒)";
		}

		if (processedEntries >= totalEntries)
			return $"DL中: {fileName} ({processedEntries}/{totalEntries}) 残り約1秒";

		double avgMsPerEntry = elapsed.TotalMilliseconds / processedEntries;
		double remainingMs = avgMsPerEntry * Math.Max(0, totalEntries - processedEntries);
		int remainingSeconds = Math.Max(1, (int)Math.Ceiling(remainingMs / 1000.0));
		return $"DL中: {fileName} ({processedEntries}/{totalEntries}) 残り約{remainingSeconds}秒";
	}
}
