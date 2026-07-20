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
        /// SkiaSharp を優先的に使用する拡張子のリスト。
        /// </summary>
        private static readonly string[] SkiaPreferredExtensions = { ".webp", ".jpg", ".jpeg" };

        /// <summary>
        /// 指定された画像パスから Bitmap を取得（キャッシュ利用）。
        /// Release/Debug の違いで DLL が正しく見つからない場合でも、
        /// 可能な限りフォールバックして読み込むようにする。
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

            string ext = Path.GetExtension(imagePath).ToLowerInvariant();
            bool useSkiaPreferred = SkiaPreferredExtensions.Contains(ext);
            Bitmap bitmap;

            // 1) SkiaSharp を使用して読み込みを試みる（USE_SKIA が定義されている場合）
#if USE_SKIA
            if (useSkiaPreferred)
            {
                try
                {
                    var bytes = File.ReadAllBytes(imagePath);
                    using var skImage = SKImage.FromEncodedData(bytes);
                    if (skImage != null)
                    {
                        // Skia でデコード成功 → PNG 経由で Bitmap に変換
                        using var skPm = skImage.Encode(SKEncodedImageFormat.Png, 100);
                        if (skPm != null)
                        {
                            using var ms = new MemoryStream(skPm.ToArray());
                            bitmap = new Bitmap(ms);
                            AddToCache(imagePath, bitmap);
                            return bitmap;
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
                    // 同様に DLL が見つからない場合はフォールバック
                }
                // それ以外の例外は try/catch を抜けて GDI+ のパスに回る
            }
#endif

            // 2) GDI+（標準）での読み込み（Skia 未使用または失敗したときのフォールバック）
            try
            {
                var bytes = File.ReadAllBytes(imagePath);
                using var ms = new MemoryStream(bytes);
                bitmap = new Bitmap(ms);
                AddToCache(imagePath, bitmap);
                return bitmap;
            }
            catch (ArgumentException ex)
            {
                throw new IOException(
                    $"画像の読み込みに失敗しました（ファイルが破損しているか、サポートされていない形式です）: {imagePath} ({ex.Message})",
                    ex);
            }
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