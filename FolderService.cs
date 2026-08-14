using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace MangaViewer
{
    /// <summary>
    /// フォルダエントリ（パス、画像数、CBZファイル数、評価値を保持）
    /// </summary>
    public struct FolderEntry
    {
        public string Path;
        public int ImageCount;
        public int CbzFileCount; // CBZ ファイルの数
        public int Rating; // -1: 評価未設定
    }

    /// <summary>
    /// フォルダリストの構築・フィルタリング・JSONキャッシュを担当するサービス。
    /// </summary>
    public class FolderService
    {
        private readonly Settings _settings;
        /// <summary>
        /// 指定フォルダが READ-ONLY（書き込み禁止）として登録されているか判定。
        /// RatingService.ReadOnlyCache に存在するかをチェックする。
        /// </summary>
        public static bool IsReadOnlyFolder(string folderPath)
        {
            return RatingService.IsReadOnlyCached(folderPath);
        }

        private Action<string>? _statusCallback;

        /// <summary>
        /// 最後に BuildFolderIndex で構築されたフォルダ一覧。
        /// </summary>
        internal List<FolderEntry> entries = new List<FolderEntry>();

        public FolderService(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// ステータス表示用のコールバックを設定（Form1などから設定）
        /// </summary>
        public void SetStatusCallback(Action<string>? callback)
        {
            _statusCallback = callback;
        }

        private void Status(string message)
        {
            _statusCallback?.Invoke(message);
        }

        #region 中核関数（フォルダスキャン＋フィルタ＋表示データを一括構築）

        /// <summary>
        /// ルートフォルダ内のサブフォルダを「一度だけ」スキャンし、
        /// フィルタを適用した結果を返す。
        /// ディレクトリスキャンは1回のみで、キャッシュを活用する。
        /// 表示条件: ((画像数OK) または (CBZあり)) かつ (評価条件OK)
        /// </summary>
        public List<FolderEntry> BuildFolderIndex(string rootPath)
        {
            var entries = new List<FolderEntry>();

            if (string.IsNullOrWhiteSpace(rootPath)) return entries;
            if (!Directory.Exists(rootPath)) return entries;

            // 30秒タイムアウト機構
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                var sorted = dirs
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var dir in sorted)
                {
                    // タイムアウトチェック
                    if (cts.IsCancellationRequested)
                    {
                        Status("30秒経過のためフォルダスキャンを中止します（既存のデータで続行）");
                        break;
                    }

                    try
                    {
                        bool writeFailed = false;

                        string folderName = Path.GetFileName(dir);
                        string jsonPath = Path.Combine(dir, $"{folderName}.json");
                        var (imageCount, cbzFileCount, djRating) = ReadFolderJson(jsonPath);

                        // DJにRがあるか、GC（画像数またはCBZ数）があるかで判定
                        bool hasDjRating = djRating != -1;
                        bool hasDjGc = imageCount > 0 || cbzFileCount > 0;
                        int cachedRating = RatingService.GetCachedRating(dir);
                        bool hasCjRating = cachedRating != -1;

                        // ルール判定：スキャンするかどうか
                        if (hasDjRating && hasCjRating)
                        {
                            // 規則1: DJにRあり + CJにRあり → スキャンスキップ
                            djRating = cachedRating; // キャッシュの評価値を優先
                        }
                        else if (hasDjRating && !hasCjRating)
                        {
                            // 規則2: DJにRあり + CJにRなし → RをCJにコピー、スキャンスキップ
                            RatingService.SetReadOnlyCacheRating(dir, djRating, imageCount, folderName);
                        }
                        else if (!hasDjRating && hasDjGc)
                        {
                            // 規則3: DJにRなし + DJにGCあり → スキャンスキップ
                        }
                        else
                        {
                            // 規則4: DJにRなし + DJにGCなし → フォルダをスキャン
                            cbzFileCount = CountCbzFiles(dir);
                            if (cbzFileCount > 0)
                                imageCount = CountImagesWithoutCbzFallback(dir);

                            try
                            {
                                SaveFolderMetaJson(jsonPath, imageCount, cbzFileCount, djRating);
                            }
                            catch
                            {
                                writeFailed = true;
                            }
                        }

                        // 1段目: 画像数フィルタまたはCBZあり判定
                        bool imageConditionOk;
                        if (_settings.MinDisplayCount > 0 || _settings.MaxDisplayCount > 0)
                        {
                            bool inRange = true;
                            if (_settings.MinDisplayCount > 0 && imageCount < _settings.MinDisplayCount)
                                inRange = false;
                            if (_settings.MaxDisplayCount > 0 && imageCount > _settings.MaxDisplayCount)
                                inRange = false;

                            // （（最小画像数以上かつ最大画像数以下）または（CBZファイル数が1以上））
                            imageConditionOk = inRange || (cbzFileCount >= 1);
                        }
                        else
                        {
                            // 設定されていない場合は無条件通過
                            imageConditionOk = true;
                        }

                        if (!imageConditionOk)
                            continue;

                        // 2段目: 最小評価値（MinEvaluation > 0 の場合に有効）
                        // djRating == -1（未設定）は通過する
                        if (_settings.MinEvaluation > 0 && djRating >= 0 && djRating < _settings.MinEvaluation)
                            continue;

                        entries.Add(new FolderEntry
                        {
                            Path = dir,
                            ImageCount = imageCount,
                            CbzFileCount = cbzFileCount,
                            Rating = djRating
                        });

                        // インクリメンタルJSON更新（書き込み可能時のみ）
                        if (!writeFailed)
                        {
                            try
                            {
                                SaveFolderMetaJson(jsonPath, imageCount, cbzFileCount, djRating);
                            }
                            catch
                            {
                                writeFailed = true;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // アクセス不可のフォルダはスキップ（全体を落とさない）
                    }
                    catch
                    {
                        // その他エラーもそのフォルダのみスキップ
                    }
                }
            }
            catch { /* ディレクトリ読み取り失敗時は空リストを返す */ }

            this.entries = entries;

            // スキャン完了後、キャッシュされた評価値をratings_cache.jsonに保存
            RatingService.FlushReadOnlyCache();

            return entries;
        }

        #endregion

        #region 公開メソッド（BuildFolderIndex を中核として利用）

        /// <summary>
        /// ローカルキャッシュのみから即座にフォルダリストを構築（NASアクセスなし）。
        /// BuildSubfolderList の高速版として使用。
        /// </summary>
        public List<FolderEntry> LoadCachedFolderMeta(string rootPath)
        {
            var entries = new List<FolderEntry>();

            if (string.IsNullOrWhiteSpace(rootPath)) return entries;
            if (!Directory.Exists(rootPath)) return entries;

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                var sorted = dirs
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var dir in sorted)
                {
                    try
                    {
                        string folderName = Path.GetFileName(dir);
                        string jsonPath = Path.Combine(dir, $"{folderName}.json");
                        var (imageCount, cbzFileCount, rating) = ReadFolderJson(jsonPath);

                        // キャッシュにデータがあればそのまま使用
                        if (imageCount == 0 && cbzFileCount == 0)
                            continue;

                        entries.Add(new FolderEntry
                        {
                            Path = dir,
                            ImageCount = imageCount,
                            CbzFileCount = cbzFileCount,
                            Rating = rating
                        });
                    }
                    catch { /* スキップ */ }
                }
            }
            catch { /* 失敗時は空リストを返す */ }

            return entries;
        }

        /// <summary>
        /// ルートフォルダ内のサブフォルダ一覧を取得し、フィルタリング適用
        /// （後方互換用：BuildFolderIndex の Path のみを返す）
        /// </summary>
        public List<string> BuildSubfolderList(string rootPath)
        {
            var entries = BuildFolderIndex(rootPath);
            return entries.Select(e => e.Path).ToList();
        }

        /// <summary>
        /// ListBox表示用のフォルダデータ一覧を取得（パス + 画像数）
        /// （後方互換用：BuildFolderIndex から派生）
        /// </summary>
        public List<(string path, int imageCount)> GetFolderDisplayData(string rootPath)
        {
            var entries = BuildFolderIndex(rootPath);
            return entries.Select(e => (e.Path, e.ImageCount)).ToList();
        }

        #endregion

        /// <summary>
        /// 指定フォルダ内の画像パスを取得・ソート
        /// </summary>
        public List<string> LoadAndSortImages(string folderPath)
        {
            var imagePaths = new List<string>();

            if (string.IsNullOrWhiteSpace(folderPath)) return imagePaths;
            if (!Directory.Exists(folderPath)) return imagePaths;

            string[] extensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };
            var allFiles = new List<string>();
            foreach (var ext in extensions)
            {
                try { allFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly)); }
                catch { /* 拡張子ごとのファイル取得は失敗しても他の拡張子は続行 */ }
            }

            imagePaths = allFiles.OrderBy(f => ExtractNumberFromFileName(f)).ToList();

            return imagePaths;
        }

        /// <summary>
        /// ファイル名から数字を抽出（ソート用）
        /// </summary>
        public static int ExtractNumberFromFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int number)) return number;
            return 0;
        }

        /// <summary>
        /// 指定フォルダ内の画像数をカウント（1回のディスクI/Oで完了）
        /// 既存の動作を保持のため、CBZも +1 のフォールバックとして扱う。
        /// </summary>
        public static int CountImages(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return 0;
            if (!Directory.Exists(dir)) return 0;

            try
            {
                int imgCount = CountImagesWithoutCbzFallback(dir);
                int cbzCount = CountCbzFiles(dir);
                // 後方互換: CBZがある場合は必ず >0 とみなす（CBZ自体を1として加算）
                return imgCount + Math.Max(0, cbzCount);
            }
            catch { return 0; }
        }

        /// <summary>
        /// 通常の画像ファイルのみをカウント（CBZフォールバックなし）。
        /// BuildFolderIndex など内部的に使用。
        /// </summary>
        private static int CountImagesWithoutCbzFallback(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return 0;
            if (!Directory.Exists(dir)) return 0;

            try
            {
                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".webp", ".png"
                };

                return Directory.EnumerateFiles(dir, "*.*")
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f)))
                    .Count();
            }
            catch { return 0; }
        }

        /// <summary>
        /// 指定フォルダ内の CBZ ファイル数をカウント。
        /// </summary>
        private static int CountCbzFiles(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return 0;
            if (!Directory.Exists(dir)) return 0;

            try
            {
                return Directory.EnumerateFiles(dir, "*.cbz", SearchOption.TopDirectoryOnly).Count();
            }
            catch { return 0; }
        }

        /// <summary>
        /// フォルダのJSONメタデータを1回のファイルI/Oで読み込む（imageCount, cbzFileCount, rating）。
        /// </summary>
        public static (int imageCount, int cbzFileCount, int rating) ReadFolderJson(string jsonPath)
        {
            int imageCount = 0;
            int cbzFileCount = 0;
            int rating = -1;

            if (!File.Exists(jsonPath))
                return (imageCount, cbzFileCount, rating);

            try
            {
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                    imageCount = ic.GetInt32();

                if (doc.RootElement.TryGetProperty("cbzFileCount", out var cb) && cb.ValueKind == JsonValueKind.Number)
                    cbzFileCount = cb.GetInt32();

                if (doc.RootElement.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number)
                    rating = r.GetInt32();
            }
            catch { /* JSON解析エラーはデフォルト値(0,0,-1)で継続 */ }

            return (imageCount, cbzFileCount, rating);
        }

        /// <summary>
        /// 画像数を JSON ファイルに保存（{ "imageCount": N }）
        /// 既存のメソッドは後方互換のため残すが、内部では新しいSaveFolderMetaJsonを使う。
        /// </summary>
        public static void SaveImageCountJson(string jsonPath, int imageCount)
        {
            try
            {
                // この呼び出し元では cbzFileCount/rating が不明な場合が多いので、
                // 既存のJSONから復旧し、imageCountのみ更新する。
                var (existingImg, existingCbz, existingRating) = ReadFolderJson(jsonPath);

                SaveFolderMetaJson(
                    jsonPath,
                    imageCount,
                    existingCbz,
                    existingRating
                );
            }
            catch { /* 保存失敗は無視 */ }
        }

        /// <summary>
        /// フォルダメタJSON（imageCount, cbzFileCount, rating）を一括保存。
        /// </summary>
        private static void SaveFolderMetaJson(string jsonPath, int imageCount, int cbzFileCount, int rating)
        {
            try
            {
                var obj = new Dictionary<string, object>();

                if (imageCount > 0)
                    obj["imageCount"] = imageCount;

                if (cbzFileCount > 0)
                    obj["cbzFileCount"] = cbzFileCount;

                if (rating != -1)
                    obj["rating"] = rating;

                File.WriteAllText(jsonPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 保存失敗は無視 */ }
        }

        /// <summary>
        /// 指定フォルダの評価値を JSON に保存（既存のJSONにratingを追加・更新）
        /// </summary>
        public static void SaveRatingToFolder(string folderPath, int rating)
        {
            try
            {
                string folderName = Path.GetFileName(folderPath);
                string jsonPath = Path.Combine(folderPath, $"{folderName}.json");

                var (imageCount, cbzFileCount, _) = ReadFolderJson(jsonPath);

                SaveFolderMetaJson(jsonPath, imageCount, cbzFileCount, rating);
            }
            catch { /* 評価値JSONの保存失敗は無視 */ }
        }

        /// <summary>
        /// フィルタ統計情報を計算（合计数、不合计数、总数）
        /// 同じルールを一貫させる: (画像条件OKまたはCBZあり) の判定を使う。
        /// </summary>
        public (int passCount, int failCount, int totalCount) ComputeFilterStats(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) return (0, 0, 0);
            if (!Directory.Exists(rootPath)) return (0, 0, 0);

            int passCount = 0;
            int failCount = 0;
            int totalCount = 0;

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                foreach (var dir in dirs)
                {
                    string folderName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{folderName}.json");
                    var (imageCount, cbzFileCount, _) = ReadFolderJson(jsonPath);

                    // スキャン前に値を最新化
                    if (imageCount == 0)
                        imageCount = GetCachedOrCount(dir);

                    if (cbzFileCount == 0)
                        cbzFileCount = CountCbzFiles(dir);

                    totalCount++;

                    bool inRange = true;
                    if (_settings.MinDisplayCount > 0 && imageCount < _settings.MinDisplayCount)
                        inRange = false;
                    // ComputeFilterStats は MaxDisplayCount を今のところ使っていないので、
                    // ここで追加したい場合は後で調整可能。

                    bool pass = inRange || (cbzFileCount >= 1);

                    if (pass)
                        passCount++;
                    else
                        failCount++;
                }
            }
            catch { /* フィルタ統計計算失敗時は0で継続 */ }

            return (passCount, failCount, totalCount);
        }

        private int GetCachedOrCount(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return 0;

            string folderName = Path.GetFileName(dir);
            string jsonPath = Path.Combine(dir, $"{folderName}.json");
            var (imageCount, _, _) = ReadFolderJson(jsonPath);

            if (imageCount > 0)
                return imageCount;

            int count = CountImages(dir);
            SaveImageCountJson(jsonPath, count);
            return count;
        }

        /// <summary>
        /// SettingsDialog で使用。指定されたパラメータで2段階フィルタ統計を計算。
        /// 戻り値：(画像数フィルター通過数、総フォルダ数、評価フィルター通過数)
        /// 同じルールを一貫させる: (画像条件OKまたはCBZあり) + 評価条件。
        /// </summary>
        public (int imagePassCount, int totalCount, int ratingPassCount) ComputeDisplayStats(
            string rootPath, int minImages, int maxImages, int minRating)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) return (0, 0, 0);
            if (!Directory.Exists(rootPath)) return (0, 0, 0);

            int imagePassCount = 0;
            int totalCount = 0;
            int ratingPassCount = 0;

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                foreach (var dir in dirs)
                {
                    string folderName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{folderName}.json");
                    var (imageCount, cbzFileCount, rating) = ReadFolderJson(jsonPath);

                    // キャッシュ未存在の場合はカウント＋保存
                    if (imageCount == 0)
                    {
                        imageCount = CountImagesWithoutCbzFallback(dir);
                        SaveFolderMetaJson(jsonPath, imageCount, cbzFileCount, rating);
                    }

                    if (cbzFileCount == 0)
                    {
                        cbzFileCount = CountCbzFiles(dir);
                        SaveFolderMetaJson(jsonPath, imageCount, cbzFileCount, rating);
                    }

                    totalCount++;

                    // 1段目：画像数フィルター または CBZあり
                    bool inRange = true;
                    if (minImages > 0 && imageCount < minImages)
                        inRange = false;
                    if (maxImages > 0 && imageCount > maxImages)
                        inRange = false;

                    // （（最小画像数以上かつ最大画像数以下）または（CBZファイル数が1以上））
                    bool passImageOrCbz = inRange || (cbzFileCount >= 1);
                    if (!passImageOrCbz)
                        continue;

                    imagePassCount++;

                    // 2段目：評価フィルター（minRating <= 0 は無効）
                    // rating == -1（未設定）は通過する
                    if (minRating <= 0)
                    {
                        ratingPassCount++;
                        continue;
                    }

                    // rating >= 0 で且つ rating < minRating の場合のみ除外
                    if (rating >= 0 && rating < minRating)
                        continue;

                    ratingPassCount++;
                }
            }
            catch { /* 失敗時は0で継続 */ }

            return (imagePassCount, totalCount, ratingPassCount);
        }
    }
}