using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public static void Initialize(Form1 form)
        {
            LogWriter.Init();

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
                form.UpdateInfoLabelBase("起動中... DBを読み込みます", "startup:init");
                form.UpdateLayout();

                // 4. バックグラウンド初期処理（CJ/DBList）→ TaskをForm1に保持させる
                form._initTask = RunBackgroundInit(form, settings);
            }
            catch (Exception ex)
            {
                form.EnsureBasicLayout();
                WriteErrorLog($"Form1_Load error: {ex}");
                form.UpdateInfoLabelBase("初期化エラーが発生しました。", "startup:init-error");
                MessageBox.Show(
                    form,
                    "起動中にエラーが発生しました。\n\n" + ex.Message,
                    "起動エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // Public for Form1 to track completion during shutdown.
        public static Task RunBackgroundInit(Form1 form, Settings settings)
        {
            string lastRoot = settings.LastRootFolder;

            return Task.Run(async () =>
            {
                try
                {
                    WriteStartupLog($"[Startup] LastRootFolder={lastRoot}, Exists={Directory.Exists(lastRoot)}");

                    bool loadedCj = false;
                    string? failReason = null;

                    // ===== 1. LastRootFolder の DB を試す（1回のみ） =====
                    if (!string.IsNullOrEmpty(lastRoot) && Directory.Exists(lastRoot))
                    {
                        WriteStartupLog("[Startup] LoadCjForParent start");
                        InvokeIfSafeSync(form, () => form.LoadCjForParent(lastRoot));

                        var (ok, reason) = EvaluateLoadResult(form);
                        if (ok)
                        {
                            loadedCj = true;
                            // TASK09.29: DB読み込み完了フラグ（起動時1回のみ）
                            InvokeIfSafeSync(form, () => { form._dbLoadedOnce = true; });
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
                        WriteStartupLog("LastRootFolder failed/empty. Trying latest cache DB only...");
                        var (cacheLoaded, cacheReason) = TryRestoreFromLatestCache(form);
                        loadedCj = cacheLoaded;
                        failReason ??= cacheReason;
                    }

                    // Safe update settings if still alive.
                    InvokeIfSafe(form, () => form._displayManager.UpdateSettings(settings));

                    // ===== 3. 結果処理 =====
                    if (loadedCj && form._folderList.Count > 0)
                    {
                        WriteStartupLog($"[Startup] Final OK path: DBList={form._folderList.Count}");

                        if (form._currentFolderIndex < 0)
                            form._currentFolderIndex = 0;

                        if (!string.IsNullOrWhiteSpace(settings.LastViewedFolderPath))
                        {
                            int rememberedIndex = form._folderList.IndexOf(settings.LastViewedFolderPath);
                            if (rememberedIndex >= 0)
                                form._currentFolderIndex = rememberedIndex;
                        }

                        string selectedFolder = EnsureValidFolderIndex(form);

                        await form.LoadAndSortImagesAsync(selectedFolder).ConfigureAwait(false);

                        InvokeIfSafe(form, () =>
                        {
                            form._displayManager.ImagePaths = form._imagePaths;
                            form._displayManager.InitializePictureBoxes();

                            form.PerformLayout();
                            form.UpdateLayout();

                            form._displayManager.DisplayImages(form._currentIndex);
                            if (listBoxFoldersSafe(form, out var lb))
                                lb.SelectedIndex = form._currentFolderIndex;
                            form.ScheduleNavigationCbzPreload();
                            form.UpdateInfoLabelBase("初期化完了", "startup:done");
                            form.ScheduleWindowTitleUpdate();
                        });
                    }
                    else
                    {
                        WriteStartupLog($"[Startup] Final EMPTY: loadedCj={loadedCj}, DBListCount={(int)(form._folderList?.Count ?? 0)}");

                        InvokeIfSafe(form, () =>
                        {
                            form._displayManager.InitializePictureBoxes();
                            form.UpdateLayout();
                            form._displayManager.DisplayImages(form._currentIndex);
                            form.UpdateInfoLabelBase("DBList が空です。キー3でフォルダを選択してください。", "startup:empty");
                            if (!string.IsNullOrEmpty(failReason))
                                WriteErrorLog($"[Startup] DB load failed: {failReason}");
                        });
                    }

                    WriteStartupLog("Form1_Load complete");
                }
                catch (Exception ex)
                {
                    InvokeIfSafe(form, () => form.EnsureBasicLayout());
                    WriteErrorLog($"Background init error: {ex}");
                    InvokeIfSafe(form, () => { form.UpdateInfoLabelBase("初期化エラーが発生しました。", "startup:bg-error"); });
                }
            });
        }

        /// <summary>
        /// Form が破棄されていない場合のみ安全に同期 Invoke を実行する。
        /// </summary>
        private static void InvokeIfSafeSync(Form1 form, Action action)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return;

            try
            {
                if (form.InvokeRequired)
                    form.Invoke(action);
                else
                    action();
            }
            catch
            {
                // Form が閉じている可能性を考慮して無視
            }
        }

        /// <summary>
        /// Form が破棄されていない場合のみ安全にInvokeを実行する。
        /// </summary>
        private static void InvokeIfSafe(Form1 form, Action action)
        {
            if (form.IsDisposed || form.IsHandleCreated == false)
                return;

            try
            {
                // 例外がスプラッシュ/閉じるフローで発生しないようキャッチ
                form.BeginInvoke(action);
            }
            catch
            {
                // Form が閉じている可能性を考慮して無視
            }
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
                WriteStartupLog($"[TryRestoreFromLatestCache] CacheDir={cacheDir}, Exists={Directory.Exists(cacheDir)}");

                if (!Directory.Exists(cacheDir))
                    return (false, "[キャッシュ] キャッシュディレクトリが見つかりません。");

                // Find all ratings_cache_*.json files
                var cacheFiles = Directory.GetFiles(cacheDir, "ratings_cache_*.json");
                WriteStartupLog($"[TryRestoreFromLatestCache] CacheFilesCount={cacheFiles.Length}");

                if (cacheFiles.Length == 0)
                    return (false, "[キャッシュ] キャッシュDBが見つかりませんでした。");

                // Sort by last write time descending; take only the latest.
                Array.Sort(cacheFiles, (a, b) =>
                    DateTime.Compare(File.GetLastWriteTimeUtc(b), File.GetLastWriteTimeUtc(a)));

                var cacheFile = cacheFiles[0];
                WriteStartupLog($"[TryRestoreFromLatestCache] Using latest: {cacheFile}");

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
                InvokeIfSafeSync(form, () =>
                {
                    // TASK09.29: キャッシュ復元も「起動時1回のみ」に含める
                    if (form._dbLoadedOnce) return;

                    form._activeCjData = cjData;
                    form._activeDbFile = cacheFile;
                    form._activeCjParentFolder = restoredRoot;
                    WriteStartupLog($"[TryRestoreFromLatestCache] Before BuildRankFiltered: DBListCount={form._folderList.Count}");
                    form.BuildRankFilteredListFromActiveCj();
                    WriteStartupLog($"[TryRestoreFromLatestCache] After BuildRankFiltered: DBListCount={form._folderList.Count}");
                });

                var (ok, evalReason) = EvaluateLoadResult(form);
                if (!ok)
                    return (false, "[キャッシュ] " + (evalReason ?? "原因不明"));

                // TASK09.29: キャッシュ復元成功 → DB読み込み完了として固定
                InvokeIfSafeSync(form, () =>
                {
                    form._dbLoadedOnce = true;
                    if (!string.IsNullOrEmpty(restoredRoot))
                    {
                        form._settings.LastRootFolder = restoredRoot;
                        SettingsManager.Save(form._settings);
                    }
                });

                WriteStartupLog($"Restored from latest cache: {cacheFile} -> root={restoredRoot}, DBListCount={form._folderList.Count}");
                return (true, null);
            }
            catch (Exception ex)
            {
                WriteErrorLog($"Cache fallback failed: {ex}");
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

        public static void WriteStartupLog(string message) => LogWriter.WriteStartupLog(message);

        public static void WriteErrorLog(string message) => LogWriter.WriteErrorLog(message);
    }
}
