using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace MangaViewer
{
    /// <summary>
    /// 画像の読み込み・キャッシュ・リソース管理を担当するサービス。
    /// SkiaSharp を使ったデコードは本クラスのみに限定する。
    /// </summary>
    public class ImageService : IDisposable
    {
        private readonly ConcurrentDictionary<string, Bitmap> _imageCache = new();
        private bool _disposed = false;

        /// <summary>
        /// 指定された画像パスから Bitmap を取得（キャッシュ利用）。
        /// </summary>
        public Bitmap LoadOrGetCachedImage(string imagePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ImageService));

            // Cache hit: return cloned bitmap for display
            if (_imageCache.TryGetValue(imagePath, out var cached))
            {
                return new Bitmap(cached);
            }

            // Decode image using SkiaSharp
            var bytes = File.ReadAllBytes(imagePath);
            using var skImage = SKImage.FromEncodedData(bytes);
            using var skPm = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new System.IO.MemoryStream(skPm.ToArray());
            var bitmap = new Bitmap(ms);

            // Store in cache (evict if full)
            if (_imageCache.Count >= Constants.MaxCacheSize)
            {
                string? oldestKey = _imageCache.Keys.FirstOrDefault();
                if (oldestKey != null && _imageCache.TryRemove(oldestKey, out var oldBitmap))
                    oldBitmap.Dispose();
            }
            _imageCache[imagePath] = new Bitmap(bitmap);

            return bitmap;
        }

        /// <summary>
        /// キャッシュを全クリアし、保持しているリソースを破棄する。
        /// </summary>
        public void ClearCache()
        {
            foreach (var bmp in _imageCache.Values)
                bmp.Dispose();
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