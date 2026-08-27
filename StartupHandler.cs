using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// Form1_Load の初期化フローを整理し、Form1 を短く保つ。
    /// 依存関係の構築・初期設定・エラーハンドリングを一元管理。
    /// TASK09.29: DB(JSON) は起動時に「1回だけ」読み込み、以降は再読込しない。
    /// </summary>
    public static class StartupHandler
    {
        private static readonly string LoadLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        public static void Initialize(Form1 form)
        {
            try
            {
                // 1. Settings ロード
                var (settings, errors) = SettingsManager.LoadWithValidation();
                form._settings = settings;

                if (errors.Count > 0)
                {
                    MessageBox.Show(
                        form,
                        "設定に不正な値が含まれているため、該当項目はデフォルト値を使用します。" + Environment.NewLine +
                            Environment.NewLine + string.Join(Environment.NewLine, errors),
                        "設定エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                // 2. DisplayManager / ImageService 初期化
                form._displayManager = new DisplayManager(form, settings, form._imageService);
                form._displayManager.ImagePaths = form._imagePaths;

                // 3. 基本レイアウト確保＋表示（ローディング中表示）
                form.EnsureBasicLayout();
                form.labelInfo.Text = "起動中... DBを読み込みます";
                form.UpdateLayout();
                Application.DoEvents();

                // 4. バックグラウンド初期処理（CJ/DBList）
                RunBackgroundInit(form, settings);
            }
            catch (Exception ex)
            {
                form.EnsureBasicLayout();
                LogError($"Form1_Load error: {ex}");
                form.labelInfo.Text = "初期化エラーが発生しました。";
                MessageBox.Show(
                    form,
                    "起動中にエラーが発生しました。\n\n" + ex.Message,
                    "起動エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void RunBackgroundInit(Form1 form, Settings settings)
        {
            string lastRoot = settings.LastRootFolder;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Yield(); // UI描画を優先

                    Log($"[Startup] LastRootFolder={lastRoot}, Exists={Directory.Exists(lastRoot)}");

                    bool loadedCj = false;
                    string? failReason = null;

                    // ===== 1. LastRootFolder の DB を試す（1回のみ） =====
                    if (!string.IsNullOrEmpty(lastRoot) && Directory.Exists(lastRoot))
                    {
                        Log("[Startup] LoadCjForParent start");
                        form.Invoke((Action)(() => form.LoadCjForParent(lastRoot)));

                        var (ok, reason) = EvaluateLoadResult(form);
                        if (ok)
                        {
                            loadedCj = true;
                            // TASK09.29: DB読み込み完了フラグ（起動時1回のみ）
                            form.Invoke((Action)(() => { form._dbLoadedOnce = true; }));
                        }
                        else
                            failReason = $"[LastRootFolder] {reason ?? "原因不明"}";
                    }
                    else
                    {
                        failReason = string.IsNullOrEmpty(lastRoot)
                            ? "[LastRootFolder] 設定されたフォルダが空です。"
                            : $"[LastRootFolder] 指定フォルダが見つかりません: {lastRoot}";
                    }

                    // ===== 2. キャッシュDBから「最新1つ」のみ試す（TASK09.29） =====
                    if (!loadedCj)
                    {
                        Log("LastRootFolder failed/empty. Trying latest cache DB only...");
                        var (cacheLoaded, cacheReason) = TryRestoreFromLatestCache(form);
                        loadedCj = cacheLoaded;
                        failReason ??= cacheReason;
                    }

                    form._displayManager.UpdateSettings(settings);

                    // ===== 3. 結果処理 =====
                    if (loadedCj && form._folderList.Count > 0)
                    {
                        Log($"[Startup] Final OK path: DBList={form._folderList.Count}");

                        if (form._currentFolderIndex < 0)
                            form._currentFolderIndex = 0;

                        form.Invoke((Action)(() =>
                        {
                            string selectedFolder = EnsureValidFolderIndex(form);
                            form.LoadAndSortImages(selectedFolder);
                            form._currentIndex = 0;
                            form._displayManager.ImagePaths = form._imagePaths;
                            form._displayManager.InitializePictureBoxes();

                            form.PerformLayout();
                            form.UpdateLayout();

                            form._displayManager.DisplayImages(0);
                            if (listBoxFoldersSafe(form, out var lb))
                                lb.SelectedIndex = form._currentFolderIndex;
                            form.labelInfo.Text = "初期化完了";
                            form.UpdateWindowTitle();
                        }));
                    }
                    else
                    {
                        Log($"[Startup] Final EMPTY: loadedCj={loadedCj}, DBListCount={(int)(form._folderList?.Count ?? 0)}");

                        form.Invoke((Action)(() =>
                        {
                            form._displayManager.InitializePictureBoxes();
                            form.UpdateLayout();
                            form._displayManager.DisplayImages(0);
                            form.labelInfo.Text = "DBList が空です。キー3でフォルダを選択してください。";
                            if (!string.IsNullOrEmpty(failReason))
                                LogError($"[Startup] DB load failed: {failReason}");
                        }));
                    }

                    Log("Form1_Load complete");
                }
                catch (Exception ex)
                {
                    form.Invoke((Action)(() => form.EnsureBasicLayout()));
                    LogError($"Background init error: {ex}");
                    form.Invoke((Action)(() => { form.labelInfo.Text = "初期化エラーが発生しました。"; }));
                }
            });
        }

        private static bool listBoxFoldersSafe(Form1 form, out ListBox lb)
        {
            lb = form.listBoxFolders;
            return lb != null;
        }

        /// <summary>
        /// フォルダインデックスが有効ならそのパスを返し、無効なら0番目のフォルダに修正して返す。
        /// </summary>
        private static string EnsureValidFolderIndex(Form1 form)
        {
            if (form._folderList.Count == 0) return "";

            // インデックス補正
            if (form._currentFolderIndex < 0 || form._currentFolderIndex >= form._folderList.Count)
                form._currentFolderIndex = 0;

            return form._folderList[form._currentFolderIndex];
        }

        /// <summary>
        /// DB読み込み結果を評価し、失敗時は具体的な理由を返す。
        /// </summary>
        private static (bool ok, string? reason) EvaluateLoadResult(Form1 form)
        {
            // CJデータ自体がない → ファイルが見つからない／読み込めない
            if (form._activeCjData == null)
                return (false, "DBファイルが見つかりませんでした。");

            // DB内部のフォルダマップが空 → 空DB
            if (form._activeCjData.Folders.Count == 0)
                return (false,
                    "DBは読み込まれましたが、内部にフォルダ情報がありません（空DB）。\n" +
                    "DB: " + form._activeDbFile);

            // フォルダリストが空 → パス不一致か結果空かの判定
            if (form._folderList.Count == 0)
            {
                // DB内のパスと実際のファイルシステムを比較（サンプリング）
                var mismatchPaths = new List<string>();
                int checkedCount = 0;
                foreach (var folderPath in form._activeCjData.Folders.Keys)
                {
                    if (!Directory.Exists(folderPath))
                    {
                        mismatchPaths.Add(folderPath);
                    }
                    checkedCount++;
                    if (checkedCount >= 10) break; // サンプリング
                }

                if (mismatchPaths.Count > 0 && mismatchPaths.Count == checkedCount)
                {
                    // 全サンプルが不一致 → パス不一致判定（DB内部のファイルパスを表示）
                    var example = string.Join(", ", mismatchPaths.Take(3));
                    return (false,
                        "DB内のフォルダパスが見つからないため使えません。\n" +
                        "DB: " + form._activeDbFile + "\n" +
                        "(例) " + example);
                }

                // それ以外 → 表示用リストを生成したが結果が空
                return (false,
                    "DBは読み込まれましたが、表示用リストを生成した結果が空でした。\n" +
                    "DB: " + form._activeDbFile);
            }

            return (true, null);
        }

        // DBが使えない理由を1回だけ通知（起動時用）
        private static void ShowDbUnavailableNotice(Form1 form, List<string> reasons)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("DBの読み込みに失敗したため、フォルダ一覧は空になっています。");
                sb.AppendLine();
                foreach (var r in reasons)
                    sb.AppendLine(r);

                MessageBox.Show(
                    form,
                    sb.ToString().Trim(),
                    "DB 読み込みエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch { /* UIスレッド外からの呼び出しでも失敗しないよう無視 */ }
        }

        /// <summary>
        /// キャッシュDBのうち「最新1つ」のみを試す（TASK09.29）。
        /// ダイアログなし・再試行なし。起動時のフォールバックとして1回だけ使う。
        /// </summary>
        private static (bool loaded, string? reason) TryRestoreFromLatestCache(Form1 form)
        {
            try
            {
                var cacheDir = AppPaths.CacheDir;
                Log($"[TryRestoreFromLatestCache] CacheDir={cacheDir}, Exists={Directory.Exists(cacheDir)}");

                if (!Directory.Exists(cacheDir))
                    return (false, "[キャッシュ] キャッシュディレクトリが見つかりません。");

                // Find all ratings_cache_*.json files
                var cacheFiles = Directory.GetFiles(cacheDir, "ratings_cache_*.json");
                Log($"[TryRestoreFromLatestCache] CacheFilesCount={cacheFiles.Length}");

                if (cacheFiles.Length == 0)
                    return (false, "[キャッシュ] キャッシュDBが見つかりませんでした。");

                // Sort by last write time descending; take only the latest.
                Array.Sort(cacheFiles, (a, b) =>
                    DateTime.Compare(File.GetLastWriteTimeUtc(b), File.GetLastWriteTimeUtc(a)));

                var cacheFile = cacheFiles[0];
                Log($"[TryRestoreFromLatestCache] Using latest: {cacheFile}");

                // Load DB
                var cjData = CjService.LoadCjFromFile(cacheFile);
                if (cjData == null || cjData.Folders.Count == 0)
                    return (false, "[キャッシュ] DBの読み込みに失敗または空DBです。");

                // Determine root folder
                string? restoredRoot = (!string.IsNullOrEmpty(cjData.ParentFolder) && Directory.Exists(cjData.ParentFolder))
                    ? cjData.ParentFolder
                    : InferParentFolderFromCacheName(Path.GetFileNameWithoutExtension(cacheFile));

                if (string.IsNullOrEmpty(restoredRoot) || !Directory.Exists(restoredRoot))
                    return (false, "[キャッシュ] DB内のフォルダパスが見つかりません。");

                // Apply in-memory only; no extra file I/O.
                form.Invoke((Action)(() =>
                {
                    // TASK09.29: キャッシュ復元も「起動時1回のみ」に含める
                    if (form._dbLoadedOnce) return;

                    form._activeCjData = cjData;
                    form._activeDbFile = cacheFile;
                    form._activeCjParentFolder = restoredRoot;
                    Log($"[TryRestoreFromLatestCache] Before BuildRankFiltered: DBListCount={form._folderList.Count}");
                    form.BuildRankFilteredListFromActiveCj();
                    Log($"[TryRestoreFromLatestCache] After BuildRankFiltered: DBListCount={form._folderList.Count}");
                }));

                var (ok, evalReason) = EvaluateLoadResult(form);
                if (!ok)
                    return (false, "[キャッシュ] " + (evalReason ?? "原因不明"));

                // TASK09.29: キャッシュ復元成功 → DB読み込み完了として固定
                form.Invoke((Action)(() => { form._dbLoadedOnce = true; }));

                Log($"Restored from latest cache: {cacheFile} -> root={restoredRoot}, DBListCount={form._folderList.Count}");
                return (true, null);
            }
            catch (Exception ex)
            {
                LogError($"Cache fallback failed: {ex}");
                return (false, "[キャッシュ] 復元に失敗しました: " + ex.Message);
            }
        }

        /// <summary>
        /// Infers parent folder path from cache filename.
        /// e.g., "O__NEW3" -> "O:\NEW3", "F__MangaDL_hitomi.la_Doujinshi" -> "F:\MangaDL\hitomi.la\Doujinshi"
        /// </summary>
        private static string? InferParentFolderFromCacheName(string cacheName)
        {
            if (cacheName.StartsWith("ratings_cache_"))
            {
                var escaped = cacheName.Substring("ratings_cache_".Length);

                // Simple heuristic: first char is drive letter, second underscore is root separator
                if (escaped.Length > 2 && escaped[1] == '_')
                {
                    string drive = escaped.Substring(0, 1);
                    string rest = escaped.Substring(2);

                    string reconstructedPath = drive + ":" + System.IO.Path.DirectorySeparatorChar +
                        rest.Replace('_', '\\');

                    if (Directory.Exists(reconstructedPath))
                        return reconstructedPath;
                }
            }

            return null;
        }

        public static void Log(string msg)
        {
            try { File.AppendAllText(LoadLogFile, $"{DateTime.Now}: {msg}{Environment.NewLine}"); } catch { }
        }

        public static void LogError(string message)
        {
            try { File.AppendAllText(LoadLogFile, $"{DateTime.Now}: ERROR: {message}{Environment.NewLine}"); } catch { }
        }
    }
}