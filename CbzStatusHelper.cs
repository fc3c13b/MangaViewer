using System;
using System.IO;

namespace MangaViewer
{
    internal static class CbzStatusHelper
    {
        internal static void WriteCacheCreateLog(string cacheRootArea2, string cbzFile, string cacheDir, string trigger)
        {
            string area = cacheDir.StartsWith(cacheRootArea2, StringComparison.OrdinalIgnoreCase)
                ? "area2"
                : "area1";
            StartupHandler.WriteStartupLog($"[CACHE-CREATE] trigger={trigger} area={area} cbz={Path.GetFileName(cbzFile)} dir={cacheDir}");
        }

        internal static void NotifyCacheStatus(Action<string, string>? callback, string cbzPath, string message)
        {
            try
            {
                callback?.Invoke(cbzPath, message);
            }
            catch
            {
                // UI通知失敗は無視する
            }
        }

        internal static string BuildDownloadStatus(string cbzFile, int processedEntries, int totalEntries, TimeSpan elapsed)
        {
            string fileName = Path.GetFileName(cbzFile);
            int remainingSeconds;

            if (totalEntries <= 0 || processedEntries <= 0)
            {
                // 進捗が未確定な待機中は、経過秒をそのまま目安表示に使う。
                remainingSeconds = Math.Max(1, (int)Math.Ceiling(Math.Max(1.0, elapsed.TotalSeconds)));
                return $"DL中: {fileName} 残り約{remainingSeconds}秒";
            }

            if (processedEntries >= totalEntries)
                return $"DL中: {fileName} ({processedEntries}/{totalEntries}) 残り約1秒";

            double avgMsPerEntry = elapsed.TotalMilliseconds / processedEntries;
            double remainingMs = avgMsPerEntry * Math.Max(0, totalEntries - processedEntries);
            remainingSeconds = Math.Max(1, (int)Math.Ceiling(remainingMs / 1000.0));
            return $"DL中: {fileName} ({processedEntries}/{totalEntries}) 残り約{remainingSeconds}秒";
        }
    }
}
