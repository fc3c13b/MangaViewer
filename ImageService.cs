using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        /// WebP 拡張子のリスト（SkiaSharp でデコードが必要な形式）。
        /// </summary>
        private static readonly string[] WebpExtensions = { ".webp" };

        /// <summary>
        /// 指定された画像パスから Bitmap を取得（キャッシュ利用）。
        /// </summary>
        public Bitmap LoadOrGetCachedImage(string imagePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ImageService));

            // Cache hit: return cloned bitmap, update access time
            if (_imageCache.TryGetValue(imagePath, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                _imageCache[imagePath] = entry;
                return new Bitmap(entry.Bitmap);
            }

            Bitmap bitmap;
            string ext = Path.GetExtension(imagePath).ToLowerInvariant();

            if (WebpExtensions.Contains(ext))
            {
#if USE_SKIA
                try
                {
                    var bytes = File.ReadAllBytes(imagePath);
                    using var skImage = SKImage.FromEncodedData(bytes);
                    if (skImage == null)
                        throw new InvalidDataException($"WebP デコードに失敗しました: {imagePath}");
                    using var skPm = skImage.Encode(SKEncodedImageFormat.Png, 100);
                    if (skPm == null)
                        throw new InvalidDataException($"WebP→PNGエンコードに失敗しました: {imagePath}");
                    using var ms = new System.IO.MemoryStream(skPm.ToArray());
                    bitmap = new Bitmap(ms);
                }
                catch (Exception ex) when (ex is not InvalidDataException)
                {
                    throw new IOException($"WebP 画像の読み込みに失敗しました: {imagePath} ({ex.Message})", ex);
                }
#else
                throw new NotSupportedException("SkiaSharp が無効化されているため、WebP はサポートされていません。");
#endif
            }
            else
            {
                try
                {
                    var bytes = File.ReadAllBytes(imagePath);
                    using var ms = new MemoryStream(bytes);
                    bitmap = new Bitmap(ms);
                }
                catch (ArgumentException ex)
                {
                    throw new IOException($"画像の読み込みに失敗しました (ファイルが破損しているか、サポートされていない形式です): {imagePath} ({ex.Message})", ex);
                }
            }

            // Evict LRU entry if cache is full
            EnsureCacheSpace();

            _imageCache[imagePath] = new CacheEntry
            {
                Bitmap = new Bitmap(bitmap),
                LastAccessed = DateTime.UtcNow
            };

            return bitmap;
        }

        /// <summary>
        /// キャッシュが一杯の場合、最も長くアクセスされていない画像を削除。
        /// </summary>
        private void EnsureCacheSpace()
        {
            while (_imageCache.Count >= Constants.MaxCacheSize)
            {
                // Find the least recently used entry
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