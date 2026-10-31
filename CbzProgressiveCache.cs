using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace MangaViewer
{
    /// <summary>
    /// 表示中の巻を「表示順に cacheDir へ直接展開」しつつ、未展開ページは
    /// 画像ロード時に当該ページだけ zip からオンデマンド展開する（案A'）。
    /// .complete は全ページ展開が完了したときにのみ書き込み、
    /// CBZ単位のキャッシュ完全性（all-or-nothing）を維持する。
    /// </summary>
    internal static class CbzProgressiveCache
    {
        private static readonly HashSet<string> ImageExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

        private sealed class Context
        {
            public string CbzFile = string.Empty;
            // 展開先フルパス -> zip エントリ名（FullName）
            public Dictionary<string, string> PathToEntry = new(StringComparer.OrdinalIgnoreCase);
        }

        // cacheDir -> 進行中コンテキスト
        private static readonly ConcurrentDictionary<string, Context> _active =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// zip の中央ディレクトリのみ読み、表示順の予測パス一覧を返す。
        /// あわせてオンデマンド展開用のマッピングを登録する。失敗時は空リスト。
        /// </summary>
        internal static List<string> Begin(string cacheDir, string cbzFile)
        {
            try
            {
                var ctx = new Context { CbzFile = cbzFile };
                var paths = new List<string>();
                string cacheFull = Path.GetFullPath(cacheDir);

                using (var archive = ZipFile.OpenRead(cbzFile))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        if (!ImageExtensions.Contains(Path.GetExtension(entry.Name))) continue;

                        var dest = Path.Combine(cacheDir, entry.FullName);
                        if (!Path.GetFullPath(dest).StartsWith(cacheFull, StringComparison.Ordinal)) continue;

                        ctx.PathToEntry[dest] = entry.FullName;
                        paths.Add(dest);
                    }
                }

                // 既存の enumerate と同じ基準（ファイル名昇順）で並べる
                paths = paths.OrderBy(Path.GetFileName).ToList();
                if (paths.Count == 0) return paths;

                _active[cacheDir] = ctx;
                return paths;
            }
            catch
            {
                return new List<string>();
            }
        }

        internal static void End(string cacheDir) => _active.TryRemove(cacheDir, out _);

        internal static bool IsActive(string cacheDir) => _active.ContainsKey(cacheDir);

        /// <summary>
        /// 指定パスが進捗展開中の巻のページ（オンデマンド展開可能）かどうか。
        /// </summary>
        internal static bool IsPendingPage(string imagePath)
        {
            foreach (var kv in _active)
            {
                if (kv.Value.PathToEntry.ContainsKey(imagePath))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 未展開ページを要求されたとき、そのページだけ zip から取り出す。
        /// 既に存在すれば true。
        /// </summary>
        internal static bool TryMaterializePage(string imagePath)
        {
            if (File.Exists(imagePath)) return true;

            foreach (var kv in _active)
            {
                if (kv.Value.PathToEntry.TryGetValue(imagePath, out var entryName))
                    return ExtractSingle(kv.Value.CbzFile, imagePath, entryName);
            }
            return false;
        }

        /// <summary>
        /// 表示順に全ページを cacheDir へ直接展開し、完了時に .complete を書く。
        /// 各ページ展開後に progress を呼ぶ。キャンセル時は .complete を書かない。
        /// 呼び出し側で cacheDir の展開ロックを保持していること。
        /// </summary>
        internal static void RunFullExtraction(string cacheDir, string cbzFile,
            Action<int, int>? progress, CancellationToken ct)
        {
            try
            {
                if (CbzCacheHelper.IsCacheReady(cacheDir)) return;
                if (!_active.TryGetValue(cacheDir, out var ctx)) return;

                var orderedDest = ctx.PathToEntry.Keys.OrderBy(Path.GetFileName).ToList();
                int total = orderedDest.Count;
                int done = 0;

                using (var archive = ZipFile.OpenRead(cbzFile))
                {
                    foreach (var dest in orderedDest)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!File.Exists(dest))
                        {
                            var entryName = ctx.PathToEntry[dest];
                            var entry = archive.GetEntry(entryName);
                            if (entry != null)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                                try { WriteEntryAtomic(entry, dest); } catch { }
                            }
                        }

                        done++;
                        progress?.Invoke(done, total);
                    }
                }

                ct.ThrowIfCancellationRequested();

                // 全ページ完了 → .complete（CBZ単位の完全性を確定）
                File.WriteAllText(Path.Combine(cacheDir, CbzManager.CacheCompleteMarkerFileName), string.Empty);
            }
            catch (OperationCanceledException)
            {
                // 中断: .complete は書かない。部分ファイルは次回展開で再生成される。
            }
            catch
            {
                // その他失敗も .complete は書かない。
            }
            finally
            {
                End(cacheDir);
            }
        }

        private static bool ExtractSingle(string cbzFile, string dest, string entryName)
        {
            try
            {
                if (File.Exists(dest)) return true;

                using var archive = ZipFile.OpenRead(cbzFile);
                var entry = archive.GetEntry(entryName);
                if (entry == null) return false;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                WriteEntryAtomic(entry, dest);
                return File.Exists(dest);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// エントリを一時ファイルへ展開し、未作成なら原子的に移動する。
        /// 競合（他スレッドが先に作成）時は破棄する。
        /// </summary>
        private static void WriteEntryAtomic(ZipArchiveEntry entry, string dest)
        {
            if (File.Exists(dest)) return;

            string tmp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                entry.ExtractToFile(tmp, overwrite: true);
                try
                {
                    if (!File.Exists(dest))
                        File.Move(tmp, dest);
                }
                catch (IOException)
                {
                    // 競合で既に存在 → tmp を破棄
                }
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
        }
    }
}
