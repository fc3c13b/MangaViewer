using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
#if USE_SKIA
using SkiaSharp;
#endif

namespace MangaViewer
{
    /// <summary>
    /// 画像の読み込み・キャッシュ・リソース管理を担当するサービス。
    /// LRU（Least Recently Used）方式でキャッシュを管理。
    /// </summary>
    public class ImageService : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _imageCache = new();
        private bool _disposed = false;

        private struct CacheEntry
        {
            public Bitmap Bitmap;
            public DateTime LastAccessed;
        }

        /// <summary>
        /// SkiaSharp を優先的に使用する拡張子のリスト（判定用）。
        /// ただし、マジックナンバーで WebP と判明した場合は拡張子に関わらず Skia を優先する。
        /// </summary>
        private static readonly string[] SkiaPreferredExtensions = { ".webp", ".jpg", ".jpeg" };

        /// <summary>
        /// 拡張子の補正用ロック（ファイルリネームの排他制御）。
        /// </summary>
        private static readonly object _renameLock = new();

        /// <summary>
        /// 指定された画像パスから Bitmap を取得（キャッシュ利用）。
        /// 破損・ unreadable なファイルの場合は null を返し、アプリを止めない。
        /// </summary>
        public Bitmap? LoadOrGetCachedImage(string imagePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ImageService));

            // Cache hit: return cloned bitmap, update access time
            if (_imageCache.TryGetValue(imagePath, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                _imageCache[imagePath] = entry;
                try
                {
                    return new Bitmap(entry.Bitmap);
                }
                catch
                {
                    // キャッシュのビットマップが壊れている場合、再読み込みを試みる
                    _ = _imageCache.TryRemove(imagePath, out _);
                }
            }

            string ext = Path.GetExtension(imagePath).ToLowerInvariant();

            // 1) まずファイルを全量メモリに読み込み（オープン中のリネーム失敗を防ぐ）
            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(imagePath);
            }
            catch (IOException)
            {
                try { Console.WriteLine($"[ImageService] *** LOAD FAILED (IO): {imagePath} ***"); } catch { }
                return null;
            }


            // 2) マジックナンバーから実際の形式を判定
            string actualFormat = DetectImageFormatFromBytes(fileBytes);

            bool useSkiaPreferred = SkiaPreferredExtensions.Contains(ext) || actualFormat == "webp";
            Bitmap? bitmap = null;

            // 3) SkiaSharp を使用して読み込みを試みる（USE_SKIA が定義されている場合）
#if USE_SKIA
            if (useSkiaPreferred && bitmap == null)
            {
                try
                {
                    using var skImage = SKImage.FromEncodedData(fileBytes);
                    if (skImage != null)
                    {
                        // Skia でデコード成功 → PNG 経由で Bitmap に変換
                        using var skPm = skImage.Encode(SKEncodedImageFormat.Png, 100);
                        if (skPm != null)
                        {
                            using var ms = new MemoryStream(skPm.ToArray());
                            bitmap = new Bitmap(ms);
                        }
                    }
                }
                catch (TypeLoadException)
                {
                    // SkiaSharp 関連 DLL のバージョン不一致等で型が見つからない（Release で発生しやすい）
                    // → GDI+ にフォールバック
                }
                catch (FileNotFoundException)
                {
                    // 同様に DLL がみつからない場合はフォールバック
                }
            }
#endif

            // 4) Skia で失敗した場合は GDI+（標準）で読み込みを試みる
            if (bitmap == null)
            {
                try
                {
                    using var ms = new MemoryStream(fileBytes);
                    bitmap = new Bitmap(ms);
                }
                catch (ArgumentException)
                {
                    // 形式不正 or 破損 → null（アプリを落とさない）
                }
                catch (OutOfMemoryException)
                {
                    // GDI+ では「サポートされていない形式」も OutOfMemoryException を投げる場合がある
                }
            }

            if (bitmap == null)
            {
                try { Console.WriteLine($"[ImageService] *** DECODE FAILED: {imagePath} (format={actualFormat}) ***"); } catch { }
                return null;
            }


            // 5) 画像読み込み成功後、キャッシュ登録
            AddToCache(imagePath, bitmap);

            return bitmap;
        }

        /// <summary>
        /// キャッシュへ画像を追加。容量超過時は LRU で古いエントリを削除。
        /// </summary>
        private void AddToCache(string imagePath, Bitmap bitmap)
        {
            EnsureCacheSpace();

            _imageCache[imagePath] = new CacheEntry
            {
                Bitmap = new Bitmap(bitmap),
                LastAccessed = DateTime.UtcNow
            };
        }

        /// <summary>
        /// キャッシュが一杯の場合、最も長くアクセスされていない画像を削除。
        /// </summary>
        private void EnsureCacheSpace()
        {
            while (_imageCache.Count >= Constants.MaxCacheSize)
            {
                var oldestKey = _imageCache.Keys
                    .OrderBy(k => _imageCache[k].LastAccessed)
                    .FirstOrDefault();

                if (oldestKey == null) break;

                if (_imageCache.TryRemove(oldestKey, out var oldEntry))
                {
                    oldEntry.Bitmap.Dispose();
                }
            }
        }

        /// <summary>
        /// キャッシュを全クリアし、保持しているリソースを破棄する。
        /// </summary>
        public void ClearCache()
        {
            foreach (var entry in _imageCache.Values)
                entry.Bitmap.Dispose();
            _imageCache.Clear();
        }

        /// <summary>
        /// 既存の Image を安全に破棄する。
        /// </summary>
        public static void DisposeImage(Image? image)
        {
            image?.Dispose();
        }

        /// <summary>
        /// ファイルのマジックナンバーから実際の画像形式を判定する。
        /// </summary>
        private static string DetectImageFormatFromBytes(byte[] data)
        {
            if (data == null || data.Length < 16)
                return "unknown";

            // PNG: 89 50 4E 47 ...
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return "png";

            // JPEG: FF D8 FF
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "jpeg";

            // WebP: RIFF .... WEBP
            if (data.Length >= 12 &&
                data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
            {
                return "webp";
            }

            return "unknown";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                ClearCache();
                _disposed = true;
            }
        }
    }
}