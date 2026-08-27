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
                    var allFailReasons = new List<string>();

                    // ===== 1. LastRootFolder の DB を試す（確認ダイアログ付き） =====
                    if (!string.IsNullOrEmpty(lastRoot) && Directory.Exists(lastRoot))
                    {
                        // ステータスに現在読み込んでいるDBを表示
                        form.Invoke((Action)(() =>
                        {
                            form.labelInfo.Text = $"[1] DB読み込み中: {lastRoot}";
                            Application.DoEvents();
                        }));

                        // このDBを読み込むか確認（何を読み込むかを明示）
                        string confirmMsg =
                            "次のDBを読み込みます。よろしいですか？" + Environment.NewLine +
                            Environment.NewLine +
                            "  タイプ: 最後に使用したフォルダのDB" + Environment.NewLine +
                            "  フォルダ: " + lastRoot;

                        bool confirmed = false;
                        form.Invoke((Action)(() =>
                        {
                            DialogResult dr = MessageBox.Show(
                                form,
                                confirmMsg,
                                "DB の読み込み確認",
                                MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Question);
                            confirmed = (dr == DialogResult.OK);
                        }));

                        if (!confirmed)
                        {
                            allFailReasons.Add("[LastRootFolder] ユーザーが読み込みをキャンセルしました。");
                            Log("[Startup] User cancelled loading LastRootFolder DB.");
                        }
                        else
                        {
                            Log("[Startup] LoadCjForParent start");
                            form.Invoke((Action)(() => form.LoadCjForParent(lastRoot)));
                            Log($"[Startup] After LoadCjForParent: _activeCjData={(form._activeCjData != null ? "OK" : "NULL")}, FoldersMapCount={(form._activeCjData?.Folders.Count ?? -1)}, _activeDbFile={form._activeDbFile}");

                            form.Invoke((Action)(() =>
                            {
                                form.BuildRankFilteredListFromActiveCj();
                            }));
                            Log($"[Startup] After BuildRankFiltered: DBList count={form._folderList.Count}");

                            // ロード結果判定（詳細な理由分類）
                            var (ok, reason) = EvaluateLoadResult(form);
                            if (!ok && !string.IsNullOrEmpty(reason))
                                allFailReasons.Add($"[LastRootFolder] {reason}");

                            if (ok)
                            {
                                loadedCj = true;
                            }
                        }
                    }
                    else if (string.IsNullOrEmpty(lastRoot))
                    {
                        allFailReasons.Add("[LastRootFolder] 設定されたフォルダが空です。");
                        Log("[Startup] LastRootFolder is empty.");
                    }
                    else
                    {
                        // フォルダが存在しない場合
                        string reason = Directory.Exists(lastRoot)
                            ? "[LastRootFolder] DBの初期化に失敗しました。"
                            : $"[LastRootFolder] 指定フォルダが見つかりません: {lastRoot}";
                        allFailReasons.Add(reason);
                        Log("[Startup] LastRootFolder invalid or missing.");
                    }

                    // ===== 2. キャッシュDBを順次試す（確認ダイアログ付き） =====
                    if (!loadedCj)
                    {
                        Log("LastRootFolder failed/empty. Trying cache DBs one by one...");
                        var (cacheLoaded, cacheReasons) = TryRestoreFromCacheSequential(form);
                        loadedCj = cacheLoaded;
                        allFailReasons.AddRange(cacheReasons);
                    }

                    form._displayManager.UpdateSettings(settings);

                    // ===== 3. 結果処理 =====
                    if (loadedCj && form._folderList.Count > 0)
                    {
                        Log($"[Startup] Final OK path: DBList={form._folderList.Count}");

                        // フォルダインデックスを適切に設定（未指定なら0）
                        if (form._currentFolderIndex < 0)
                            form._currentFolderIndex = 0;

                        form.Invoke((Action)(() =>
                        {
                            form.labelInfo.Text = "画像読み込み中...";
                            Application.DoEvents();

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

                            // Set initial window title with mode and path info
                            form.UpdateWindowTitle();
                        }));
                    }
                    else
                    {
                        Log($"[Startup] Final EMPTY: loadedCj={loadedCj}, DBListCount={(int)(form._folderList?.Count ?? 0)}");

                        // DBが使えない理由を簡易表示
                        string message = "DBList が空です。キー3でフォルダを選択してください。";
                        if (allFailReasons.Count > 0)
                            message += Environment.NewLine + "原因:" + Environment.NewLine + string.Join(Environment.NewLine, allFailReasons);

                        form.Invoke((Action)(() =>
                        {
                            form._displayManager.InitializePictureBoxes();
                            form.UpdateLayout();
                            form._displayManager.DisplayImages(0);
                            form.labelInfo.Text = "DBList が空です。キー3でフォルダを選択してください。";
                            if (allFailReasons.Count > 0)
                                ShowDbUnavailableNotice(form, allFailReasons);
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
        /// キャッシュDBを順次試す（確認ダイアログ付き）。
        /// 全て試した場合は「最後まで読み込んだ」ことを通知。
        /// </summary>
        private static (bool loaded, List<string> failReasons) TryRestoreFromCacheSequential(Form1 form)
        {
            var failReasons = new List<string>();

            try
            {
                // Get cache directory
                var cacheDir = AppPaths.CacheDir;
                Log($"[TryRestoreFromCache] CacheDir={cacheDir}, Exists={Directory.Exists(cacheDir)}");

                if (!Directory.Exists(cacheDir))
                {
                    failReasons.Add("[キャッシュ] キャッシュディレクトリが見つかりません。");
                    return (false, failReasons);
                }

                // Find all ratings_cache_*.json files
                var cacheFiles = Directory.GetFiles(cacheDir, "ratings_cache_*.json");
                Log($"[TryRestoreFromCache] CacheFilesCount={cacheFiles.Length}");

                if (cacheFiles.Length == 0)
                {
                    failReasons.Add("[キャッシュ] キャッシュDBが見つかりませんでした。");
                    return (false, failReasons);
                }

                // Sort by last write time descending (most recent first)
                Array.Sort(cacheFiles, (a, b) =>
                    DateTime.Compare(
                        File.GetLastWriteTimeUtc(b),
                        File.GetLastWriteTimeUtc(a)));

                Log($"Found {cacheFiles.Length} cache files. Trying sequentially.");

                // Try each cache file until we find one with valid folders
                int dbIndex = 0;
                foreach (var cacheFile in cacheFiles)
                {
                    dbIndex++;
                    try
                    {
                        var cjData = CjService.LoadCjFromFile(cacheFile);
                        Log($"[TryRestoreFromCache #{dbIndex}] File={cacheFile}, cjData={(cjData != null ? "OK" : "NULL")}, ParentFolder={cjData?.ParentFolder}, FoldersCount={(cjData?.Folders.Count ?? -1)}");

                        // DBファイル読み込み失敗 or 空DB → その場で理由を表示
                        if (cjData == null || cjData.Folders.Count == 0)
                        {
                            string reason = cjData == null
                                ? $"[キャッシュ #{dbIndex}/{cacheFiles.Length}] DBファイルの読み込みに失敗しました。"
                                : $"[キャッシュ #{dbIndex}/{cacheFiles.Length}] DBが空です（フォルダ情報=0）。";
                            failReasons.Add(reason);

                            // ステータスに現在試しているDBを表示
                            form.Invoke((Action)(() =>
                            {
                                form.labelInfo.Text = $"[{dbIndex}/{cacheFiles.Length}] DB検証中: {Path.GetFileName(cacheFile)}";
                                Application.DoEvents();
                            }));

                            MessageBox.Show(
                                form,
                                reason + Environment.NewLine +
                                    "DB: " + cacheFile,
                                "DB 読み込みエラー",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);

                            LogError($"[TryRestoreFromCache #{dbIndex}] DB null or empty: {cacheFile}");
                            continue;
                        }

                        // Use parentFolder from JSON; fall back to filename inference only when invalid.
                        string? restoredRoot = !string.IsNullOrEmpty(cjData.ParentFolder) ? cjData.ParentFolder : null;

                        Log($"[TryRestoreFromCache #{dbIndex}] Before fallback: restoredRoot={restoredRoot}, Exists={(Directory.Exists(restoredRoot) ? "true" : "false")}");

                        if (string.IsNullOrEmpty(restoredRoot) || !Directory.Exists(restoredRoot))
                        {
                            var fileName = Path.GetFileNameWithoutExtension(cacheFile);
                            restoredRoot = InferParentFolderFromCacheName(fileName);
                            Log($"[TryRestoreFromCache #{dbIndex}] After infer: restoredRoot={restoredRoot}");
                        }

                        // パス不一致 → DB内部のファイルパスを表示して次のDBへ
                        if (string.IsNullOrEmpty(restoredRoot) || !Directory.Exists(restoredRoot))
                        {
                            string dbParent = cjData.ParentFolder ?? "(未設定)";
                            string reason =
                                $"[キャッシュ #{dbIndex}/{cacheFiles.Length}] DB内のフォルダパスが見つかりません。" + Environment.NewLine +
                                "  DB: " + cacheFile + Environment.NewLine +
                                "  DB内部の親フォルダ: " + dbParent;

                            failReasons.Add(reason);

                            form.Invoke((Action)(() =>
                            {
                                MessageBox.Show(
                                    form,
                                    reason,
                                    "DB 読み込みエラー",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                            }));

                            LogError($"[TryRestoreFromCache #{dbIndex}] Path not found: DB={cacheFile}, dbParent={dbParent}");
                            continue;
                        }

                        // ここで確認ダイアログ：何を読み込むか表示してOKを待つ（詳細情報付き）
                        string cacheName = Path.GetFileName(cacheFile);
                        string msg =
                            $"次のDB ({dbIndex}/{cacheFiles.Length}) を読み込みます。よろしいですか？" + Environment.NewLine +
                            Environment.NewLine +
                            "  ファイル: " + cacheName + Environment.NewLine +
                            "  フォルダ: " + restoredRoot + Environment.NewLine +
                            "  DB内フォルダ数: " + cjData.Folders.Count;

                        bool confirmed = false;
                        form.Invoke((Action)(() =>
                        {
                            DialogResult dr = MessageBox.Show(
                                form,
                                msg,
                                "DB の読み込み確認",
                                MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Question);
                            confirmed = (dr == DialogResult.OK);
                        }));

                        if (!confirmed)
                        {
                            // ユーザーが拒否 → 次のDBを試す（中断ではない）
                            Log($"[TryRestoreFromCache #{dbIndex}] User cancelled loading this DB.");
                            continue;
                        }

                        // ステータスに現在読み込んでいるDBを表示
                        form.Invoke((Action)(() =>
                        {
                            form.labelInfo.Text = $"[{dbIndex}/{cacheFiles.Length}] DB読み込み中: {cacheName}";
                            Application.DoEvents();
                        }));

                        // このキャッシュをアクティブに設定
                        form.Invoke((Action)(() =>
                        {
                            form._activeCjData = cjData;
                            form._activeDbFile = cacheFile;
                            form._activeCjParentFolder = restoredRoot;
                            Log($"[TryRestoreFromCache #{dbIndex}] Before BuildRankFiltered: DBListCount={form._folderList.Count}");
                            form.BuildRankFilteredListFromActiveCj();
                            Log($"[TryRestoreFromCache #{dbIndex}] After BuildRankFiltered: DBListCount={form._folderList.Count}");
                        }));

                        // ロード結果評価（詳細な理由付き）
                        var (ok, evalReason) = EvaluateLoadResult(form);
                        if (!ok)
                        {
                            string failMsg = $"[キャッシュ #{dbIndex}/{cacheFiles.Length}] {evalReason ?? "原因不明"}";
                            failReasons.Add(failMsg);

                            // このDBがなぜ使えないかを即座に通知してから次のDBへ進む
                            form.Invoke((Action)(() =>
                            {
                                MessageBox.Show(
                                    form,
                                    "このDBの読み込みに失敗しました。" + Environment.NewLine +
                                        Environment.NewLine + failMsg.Replace("\n", Environment.NewLine),
                                    "DB 読み込みエラー",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                            }));

                            LogError($"[TryRestoreFromCache #{dbIndex}] EvaluateLoadResult failed: {evalReason}");
                            continue;
                        }

                        Log($"Restored from cache: {cacheFile} -> root={restoredRoot}, folders added up to {form._folderList.Count}");
                        // Continue trying remaining DBs instead of stopping here.
                    }
                    catch (Exception ex)
                    {
                        string errFailMsg = $"[キャッシュ #{dbIndex}/{cacheFiles.Length}] エラー: {Path.GetFileName(cacheFile)} - {ex.Message}";
                        LogError($"Failed to restore from cache file {cacheFile}: {ex}");
                        failReasons.Add(errFailMsg);

                        // エラー理由を即座に通知
                        form.Invoke((Action)(() =>
                        {
                            MessageBox.Show(
                                form,
                                "このDBの読み込みに失敗しました。" + Environment.NewLine +
                                    Environment.NewLine + errFailMsg,
                                "DB 読み込みエラー",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }));
                    }
                }

                // ここに到達＝全てのキャッシュDBの処理が完了した（成功・失敗・キャンセルを含む）
                bool anyLoaded = false;
                int initialCountBeforeSummary = 0;

                form.Invoke((Action)(() =>
                {
                    initialCountBeforeSummary = form._folderList.Count;
                }));

                // If we have folders now, consider it loaded.
                if (initialCountBeforeSummary > 0)
                    anyLoaded = true;

                var summaryReasons = new StringBuilder();
                summaryReasons.AppendLine("利用可能なDBを全て試しました。");
                summaryReasons.AppendLine($"確認したDB数: {cacheFiles.Length} 個");
                if (anyLoaded)
                    summaryReasons.AppendLine("読み込み成功: はい（フォルダ一覧に反映済み）");
                else
                    summaryReasons.AppendLine("読み込み成功: いいえ（有効なDBが見つかりませんでした）");

                // Add failure reasons if any.
                if (failReasons.Count > 0)
                {
                    summaryReasons.AppendLine();
                    foreach (var r in failReasons)
                        summaryReasons.AppendLine(r);
                }

                // 「最後まで読んだ」ことをユーザーに明示的に通知（詳細付き）
                form.Invoke((Action)(() =>
                {
                    MessageBox.Show(
                        form,
                        summaryReasons.ToString().Trim(),
                        "DB 読み込み完了",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }));

                Log($"[TryRestoreFromCache] All {cacheFiles.Length} DB(s) processed. AnyLoaded={anyLoaded}. Failures:");
                foreach (var r in failReasons)
                    Log($"[TryRestoreFromCache] - {r}");

                return (anyLoaded, failReasons);
            }
            catch (Exception ex)
            {
                LogError($"Cache fallback failed: {ex}");
                failReasons.Add($"キャッシュ復元に失敗しました: {ex.Message}");
            }

            return (false, failReasons);
        }

        /// <summary>
        /// Infers parent folder path from cache filename.
        /// e.g., "O__NEW3" -> "O:\NEW3", "F__MangaDL_hitomi.la_Doujinshi" -> "F:\MangaDL\hitomi.la\Doujinshi"
        /// </summary>
        private static string? InferParentFolderFromCacheName(string cacheName)
        {
            // Format: ratings_cache_{escaped_name}.json
            if (cacheName.StartsWith("ratings_cache_"))
            {
                var escaped = cacheName.Substring("ratings_cache_".Length);

                // Try to reconstruct original path from escaped name
                // The escaping replaces non-alphanumeric chars with '_' except '.', '-'
                // Common patterns: O__NEW3 -> O:\NEW3, F__MangaDL... -> F:\MangaDL\...
                
                // Simple heuristic: first char is drive letter, second underscore is root separator
                if (escaped.Length > 2 && escaped[1] == '_')
                {
                    string drive = escaped.Substring(0, 1);
                    string rest = escaped.Substring(2);

                    // Replace underscores with backslashes for path reconstruction
                    // This is a best-effort approach; exact paths may vary
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