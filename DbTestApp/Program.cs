using System;
using System.IO;
using System.Linq;
using MangaViewer;

namespace DbTestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== DB Load Test App (using real MangaViewer classes) ===");

            // 1. Load settings via SettingsManager
            var settings = SettingsManager.Load();
            Console.WriteLine("[SETTINGS]");
            Console.WriteLine($"  LastRootFolder: {settings.LastRootFolder}");
            Console.WriteLine($"  DbMinEvaluation: {settings.DbMinEvaluation}");
            Console.WriteLine($"  DbFilterGreaterOrEqual: {settings.DbFilterGreaterOrEqual}");
            Console.WriteLine($"  DbMinDisplayCount: {settings.DbMinDisplayCount}");
            Console.WriteLine($"  DbMaxDisplayCount: {settings.DbMaxDisplayCount}");

            // Try to load CJ from LastRootFolder using CjService.FindAndLoadCj
            if (!string.IsNullOrEmpty(settings.LastRootFolder))
            {
                var result = CjService.FindAndLoadCj(settings.LastRootFolder);
                if (result.HasValue)
                {
                    Console.WriteLine($"[INFO] Using CJ file: {result.Value.cjFile}");
                    RunDbTest(result.Value.data, settings);
                    return;
                }
            }

            // Fallback: try cache DBs via CjService.LoadCjFromFile
            Console.WriteLine("[INFO] No CJ in LastRootFolder. Trying cache DBs...");
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MangaViewer", "cache");

            if (!Directory.Exists(cacheDir))
            {
                Console.WriteLine($"[ERROR] Cache dir not found: {cacheDir}");
                return;
            }

            var cacheFiles = Directory.GetFiles(cacheDir, "ratings_cache_*.json")
                                       .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                       .ToList();

            if (cacheFiles.Count == 0)
            {
                Console.WriteLine("[INFO] No cache DB files found.");
                return;
            }

            Console.WriteLine($"[INFO] Found {cacheFiles.Count} cache file(s).");

            foreach (var cf in cacheFiles)
            {
                var cjData = CjService.LoadCjFromFile(cf);
                if (cjData == null || cjData.Folders.Count == 0)
                {
                    Console.WriteLine($"[SKIP] {Path.GetFileName(cf)}: no folders or load failed.");
                    continue;
                }

                Console.WriteLine($"[INFO] Using cache DB: {cf}");
                RunDbTest(cjData, settings);
                return;
            }

            Console.WriteLine("[ERROR] No usable cache DB found.");
        }

        static void RunDbTest(CjRoot cjData, Settings settings)
        {
            Console.WriteLine($"\n[DBTEST] Folders in CJ: {cjData.Folders.Count}");

            // Show first 15 entries for inspection
            int shown = 0;
            foreach (var kvp in cjData.Folders.Take(15))
            {
                var e = kvp.Value;
                Console.WriteLine($"[DBTEST] - Folder: {Path.GetFileName(kvp.Key)}, " +
                                  $"Rating={e.Rating}, ImageCount={e.ImageCount ?? -1}, CbzZipCount={e.CbzZipCount ?? -1}");
                shown++;
            }

            // Use DbListBuilder.BuildFromActiveCj (same logic as main app)
            var filtered = DbListBuilder.BuildFromActiveCj(cjData, null, settings);
            Console.WriteLine($"\n[DBTEST] FoldersAfterFilter: {filtered.Count}");

            if (filtered.Count == 0)
            {
                Console.WriteLine("[DBTEST] >>> DB returned 0 folders after filter.");
                Console.WriteLine("[DBTEST] >>> This means either:");
                Console.WriteLine("   - All folders were excluded by rating/imageCount filters, or");
                Console.WriteLine("   - No valid entries in CJ.");

                // Show why some are excluded (first 30)
                foreach (var kvp in cjData.Folders.Take(30))
                {
                    var e = kvp.Value;
                    bool included = ShouldIncludeFolderInline(
                        e.Rating,
                        e.ImageCount ?? 0,
                        e.CbzZipCount ?? 0,
                        settings.DbMinEvaluation,
                        settings.DbFilterGreaterOrEqual,
                        settings.DbMinDisplayCount,
                        settings.DbMaxDisplayCount);

                    Console.WriteLine($"[DBTEST] - {Path.GetFileName(kvp.Key)}: included={included}, " +
                                      $"rating={e.Rating}, imageCount={e.ImageCount ?? -1}");
                }
            }
            else
            {
                Console.WriteLine("[DBTEST] >>> DB load succeeded. First 5 folders in filtered list:");
                foreach (var p in filtered.Take(5))
                {
                    Console.WriteLine($"[DBTEST] - {Path.GetFileName(p)}");
                }
            }
        }

        // Inline copy of DbListBuilder.ShouldIncludeFolder logic for diagnostics.
        static bool ShouldIncludeFolderInline(
            int rating,
            int imageCount,
            int cbzZipCount,
            int minEvaluation,
            bool greaterOrEqual,
            int dbMinImageCount,
            int dbMaxImageCount)
        {
            if (minEvaluation > 0 && rating != -1)
            {
                if (greaterOrEqual)
                {
                    if (rating < minEvaluation) return false;
                }
                else
                {
                    if (rating != minEvaluation) return false;
                }
            }

            // Key fix: only enforce dbMinImageCount when cbzZipCount < 1
            if (cbzZipCount < 1 && dbMinImageCount > 0 && imageCount < dbMinImageCount) return false;
            if (dbMaxImageCount > 0 && imageCount > dbMaxImageCount) return false;
            return true;
        }
    }
}