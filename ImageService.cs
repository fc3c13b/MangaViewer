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
    /// </summary>
    public class ImageService : IDisposable
    {
        private readonly ConcurrentDictionary<string, Bitmap> _imageCache = new();
        private bool _disposed = false;

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

            // Cache hit: return cloned bitmap for display (使い回し用コピー)
            if (_imageCache.TryGetValue(imagePath, out var cached))
            {
                return new Bitmap(cached);
            }

            Bitmap bitmap;
            string ext = Path.GetExtension(imagePath).ToLowerInvariant();

            if (WebpExtensions.Contains(ext))
            {
                // WebP は GDI+ でサポートされていないため、SkiaSharp でデコード
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
                // JPEG / PNG など: GDI+ で直接デコード（ファイルロック回避のため MemoryStream 経由）
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