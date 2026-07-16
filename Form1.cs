using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkiaSharp;

namespace MangaViewer
{
    public partial class Form1 : Form
    {
        internal PictureBox pictureBoxLeft = null!;
        internal PictureBox pictureBoxRight = null!;
        private Panel panelList = null!;
        internal ListBox listBoxFolders = null!;
        internal Label labelInfo = null!;

        internal List<string> _imagePaths = new List<string>();
        internal int _currentIndex = 0;
        internal string _currentFolder = "";

        internal List<string> _folderList = new List<string>();
        internal int _currentFolderIndex = -1;

        private Image? currentImageRight = null;
        private Image? currentImageLeft = null;

        // Image cache for preloading
        private System.Collections.Concurrent.ConcurrentDictionary<string, Bitmap> imageCache = new();
        private const int MaxCacheSize = 200;

        private const int RatioLeftImg = 36;
        private const int RatioRightImg = 36;
        private const int RatioList = 28;
        internal const int TotalRatio = RatioLeftImg + RatioRightImg + RatioList;

        // 最小表示枚数設定
        internal bool _minDisplayCountEnabled = false;
        internal int _minDisplayCountValue = 20;

        // 最小評価値（0-10, デフォルト8）
        private int minEvaluationValue = 8;

        public Form1()
        {
            InitializeComponent();
            this.KeyPreview = true;
            // KeyDownイベントをKeyboardInputHandlerに委譲
            this.KeyDown += (s, e) => KeyboardInputHandler.HandleKeyDown(e, this);

            listBoxFolders.SelectedIndexChanged += (s, e) =>
            {
                if (listBoxFolders.SelectedIndex >= 0 && listBoxFolders.SelectedIndex < _folderList.Count)
                {
                    _currentFolderIndex = listBoxFolders.SelectedIndex;
                    LoadAndSortImages(_folderList[_currentFolderIndex]);
                    _currentIndex = 0;
                    DisplayTwoImages(_currentIndex);
                }
            };

            this.Load += (s, e) => RestoreLastRootFolder();
        }

        private void RestoreLastRootFolder()
        {
            LoadSettingsFromFile();
            string? rootPath = null;
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("LastRootFolder", out var prop) && prop.ValueKind == JsonValueKind.String)
                        rootPath = prop.ToString();
                }
            }
            catch { return; }

            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            BuildSubfolderList(rootPath);
            if (_folderList.Count > 0)
            {
                _currentFolderIndex = 0;
                LoadAndSortImages(_folderList[_currentFolderIndex]);
            }
            else
            {
                _folderList.Clear();
                _currentFolderIndex = -1;
                LoadAndSortImages(rootPath);
            }
            _currentIndex = 0;
            DisplayTwoImages(_currentIndex);
            listBoxFolders.SelectedIndex = _currentFolderIndex;
        }

        /// <summary>
        /// 最後のルートフォルダを setting.json から取得
        /// </summary>
        internal string GetRootFolder()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("LastRootFolder", out var prop) && prop.ValueKind == JsonValueKind.String)
                        return prop.ToString();
                }
            }
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        internal void SaveRootFolder(string rootPath)
        {
            try
            {
                var obj = new Dictionary<string, string> { { "LastRootFolder", rootPath } };
                if (File.Exists(SettingsFilePath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(SettingsFilePath);
                        using var doc = JsonDocument.Parse(existingJson);
                        if (doc.RootElement.TryGetProperty("MinDisplayCountEnabled", out var ep))
                            { try { obj["MinDisplayCountEnabled"] = ep.GetBoolean().ToString(); } catch { } }
                        if (doc.RootElement.TryGetProperty("MinDisplayCount", out var cp) && cp.ValueKind == JsonValueKind.Number)
                            obj["MinDisplayCount"] = cp.GetInt32().ToString();
                        if (doc.RootElement.TryGetProperty("MinEvaluation", out var evp) && evp.ValueKind == JsonValueKind.Number)
                            obj["MinEvaluation"] = evp.GetInt32().ToString();
                    } catch { }
                }
                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }

        internal void BuildSubfolderList(string rootPath)
        {
            _folderList.Clear();
            _currentFolderIndex = -1;

            // UIに「読込中」メッセージを表示し、画面を更新
            labelInfo.Text = "フォルダ一覧を読み込み中...";
            Application.DoEvents();

            LoadSettingsFromFile();

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                var sorted = dirs
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 第一段階: JSONキャッシュからimageCountを一括取得
                var cacheResult = new Dictionary<string, int>();

                foreach (var dir in sorted)
                {
                    string folderName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{folderName}.json");
                    var (imageCount, _) = ReadFolderJson(jsonPath);

                    if (imageCount > 0)
                    {
                        cacheResult[dir] = imageCount;
                    }
                    else
                    {
                        // キャッシュ未存在の場合は直接カウント
                        int count = CountImages(dir);
                        cacheResult[dir] = count;
                        SaveImageCountJson(jsonPath, count);
                    }
                }

                // 第三段階: フィルタ＋ListBox表示用データ作成
                var folderData = new List<(string path, int imageCount)>();

                foreach (var dir in sorted)
                {
                    int ic = cacheResult.TryGetValue(dir, out var v) ? v : 0;

                    // フィルタ基準未満のフォルダは除外（有効な場合のみ）
                    if (_minDisplayCountEnabled && ic < _minDisplayCountValue) continue;

                    _folderList.Add(dir);
                    folderData.Add((dir, ic));
                }

                // ListBox 表示（folderDataのキャッシュを使用し、JSONを2度読む必要なし）
                listBoxFolders.DataSource = null;
                listBoxFolders.Items.Clear();
                foreach (var (path, ic) in folderData)
                {
                    string folderName = Path.GetFileName(path);
                    listBoxFolders.Items.Add($"{folderName} -[{ic}]");
                }

                if (_folderList.Count > 0)
                {
                    listBoxFolders.SelectedIndex = 0;
                    _currentFolderIndex = 0;
                }
            }
            catch
            {
                _folderList.Clear();
                _currentFolderIndex = -1;
            }
            finally
            {
                // 読込完了後にラベルを元に戻し画面更新
                labelInfo.Text = "";
                Application.DoEvents();
            }
        }

        /// <summary>
        /// 指定フォルダ内の画像数をカウント（1回のディスクI/Oで完了）
        /// </summary>
        private int CountImages(string dir)
        {
            try
            {
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".webp", ".png" };
                return Directory.EnumerateFiles(dir, "*.*")
                    .Where(f => extensions.Contains(Path.GetExtension(f)))
                    .Count();
            }
            catch { return 0; }
        }

        /// <summary>
        /// フォルダのJSONメタデータを1回のファイルI/Oで読み込む（imageCount, rating）。
        /// </summary>
        private (int imageCount, int rating) ReadFolderJson(string jsonPath)
        {
            int imageCount = 0;
            int rating = -1;

            if (!File.Exists(jsonPath))
                return (imageCount, rating);

            try
            {
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("imageCount", out var ic) && ic.ValueKind == JsonValueKind.Number)
                    imageCount = ic.GetInt32();
                if (doc.RootElement.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number)
                    rating = r.GetInt32();
            }
            catch { }

            return (imageCount, rating);
        }

        /// <summary>
        /// 画像数を JSON ファイルに保存（{ "imageCount": N }）
        /// </summary>
        private void SaveImageCountJson(string jsonPath, int imageCount)
        {
            try
            {
                var obj = new Dictionary<string, int> { { "imageCount", imageCount } };
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>
        /// フォルダの評価値を JSON ファイルに保存（既存のJSONにratingを追加・更新）
        /// </summary>
        internal void SaveRatingToFolder(string folderPath, int rating)
        {
            try
            {
                string folderName = Path.GetFileName(folderPath);
                string jsonPath = Path.Combine(folderPath, $"{folderName}.json");

                // ReadFolderJsonで既存値を1回で取得
                var (imageCount, _) = ReadFolderJson(jsonPath);

                var obj = new Dictionary<string, object> { { "rating", rating } };
                if (imageCount > 0)
                    obj["imageCount"] = imageCount;

                File.WriteAllText(jsonPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        internal void LoadSettingsFromFile()
        {
            if (!File.Exists(SettingsFilePath)) return;
            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("MinDisplayCountEnabled", out var enabledProp))
                {
                    try { _minDisplayCountEnabled = enabledProp.GetBoolean(); } catch { }
                }
                if (doc.RootElement.TryGetProperty("MinDisplayCount", out var countProp) && countProp.ValueKind == JsonValueKind.Number)
                {
                    int val = countProp.GetInt32();
                    if (val >= 2 && val <= 100) _minDisplayCountValue = val;
                }
                if (doc.RootElement.TryGetProperty("MinEvaluation", out var evalProp) && evalProp.ValueKind == JsonValueKind.Number)
                {
                    int val = evalProp.GetInt32();
                    if (val >= 0 && val <= 10) minEvaluationValue = val;
                }
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.Text = "Manga Viewer";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(1400, 800);
            this.MinimumSize = new Size(900, 600);
            this.BackColor = Color.Black;

            pictureBoxRight = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            pictureBoxLeft = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            this.Controls.Add(pictureBoxRight);
            this.Controls.Add(pictureBoxLeft);

            labelInfo = new Label { Text = "フォルダを選択してください (キー1)", AutoSize = true, ForeColor = Color.White, BackColor = Color.FromArgb(64, 64, 64) };
            this.Controls.Add(labelInfo);

            panelList = new Panel { BackColor = Color.FromArgb(30, 30, 30), BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(panelList);

            listBoxFolders = new ListBox
            {
                BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White,
                BorderStyle = BorderStyle.None, Font = new Font("Meiryo UI", 9F),
                SelectionMode = SelectionMode.One, HorizontalScrollbar = true, TabStop = false,
            };
            panelList.Controls.Add(listBoxFolders);

            UpdateLayout();
            this.Resize += (s, e) => UpdateLayout();
        }

        internal void LoadAndSortImages(string folderPath)
        {
            _currentFolder = folderPath;
            _imagePaths.Clear();
            string[] extensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };
            var allFiles = new List<string>();
            foreach (var ext in extensions)
            {
                try { allFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly)); } catch { }
            }
            _imagePaths = allFiles.OrderBy(f => ExtractNumberFromFileName(f)).ToList();
        }

        private int ExtractNumberFromFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int number)) return number;
            return 0;
        }

        internal void DisplayTwoImages(int startIndex)
        {
            if (_imagePaths.Count == 0)
            { pictureBoxRight.Image = null; pictureBoxLeft.Image = null; labelInfo.Text = "表示可能な画像がありません。"; return; }

            LoadImageIntoPictureBox(pictureBoxRight, ref currentImageRight, startIndex < _imagePaths.Count ? _imagePaths[startIndex] : null);
            LoadImageIntoPictureBox(pictureBoxLeft, ref currentImageLeft, (startIndex + 1) < _imagePaths.Count ? _imagePaths[startIndex + 1] : null);
            UpdateInfoLabel(startIndex);

            // Preload next 2 images into cache in background
            int nextIdx = startIndex + 2;
            int nextNextIdx = startIndex + 3;
            if (nextIdx < _imagePaths.Count || nextNextIdx < _imagePaths.Count)
            {
                Task.Run(() =>
                {
                    if (nextIdx < _imagePaths.Count) LoadOrGetCachedImage(_imagePaths[nextIdx]);
                    if (nextNextIdx < _imagePaths.Count) LoadOrGetCachedImage(_imagePaths[nextNextIdx]);
                });
            }
        }

        private void UpdateInfoLabel(int startIndex)
        {
            if (_imagePaths.Count == 0) return;
            int rightPageNum = ExtractNumberFromFileName(_imagePaths[startIndex]);
            int spreadIndex = startIndex / 2 + 1;
            int totalSpreads = (_imagePaths.Count + 1) / 2;
            string folderName = Path.GetFileName(_currentFolder);
            string folderInfo = !string.IsNullOrEmpty(folderName) ? $"[{folderName}] " : "";
            string folderIndexInfo = _folderList.Count > 1 ? $"{_currentFolderIndex + 1}/{_folderList.Count}話 " : "";

            if (startIndex + 1 < _imagePaths.Count)
            {
                int leftPageNum = ExtractNumberFromFileName(_imagePaths[startIndex + 1]);
                labelInfo.Text = folderInfo + folderIndexInfo + $"右: {rightPageNum} | 左: {leftPageNum} | {spreadIndex}/{totalSpreads} ページ組";
            }
            else
            {
                labelInfo.Text = folderInfo + folderIndexInfo + $"右: {rightPageNum} | {spreadIndex}/{totalSpreads} ページ組";
            }
        }

        private Bitmap LoadOrGetCachedImage(string imagePath)
        {
            // Cache hit: return cloned bitmap for display
            if (imageCache.TryGetValue(imagePath, out var cached))
            {
                return new Bitmap(cached);
            }

            // Decode image using SkiaSharp
            var bytes = File.ReadAllBytes(imagePath);
            using var skImage = SKImage.FromEncodedData(bytes);
            using var skPm = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(skPm.ToArray());
            var bitmap = new Bitmap(ms);

            // Store in cache (evict if full)
            if (imageCache.Count >= MaxCacheSize)
            {
                string oldestKey = imageCache.Keys.First();
                if (imageCache.TryRemove(oldestKey, out var oldBitmap))
                    oldBitmap.Dispose();
            }
            imageCache[imagePath] = new Bitmap(bitmap);

            return bitmap;
        }

        private void LoadImageIntoPictureBox(PictureBox pb, ref Image? currentImage, string? imagePath)
        {
            if (currentImage != null)
            {
                currentImage.Dispose();
                currentImage = null;
            }

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                pb.Image = null;
                return;
            }

            try
            {
                currentImage = LoadOrGetCachedImage(imagePath);
                pb.Image = currentImage;
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show("画像の読み込みに失敗しました:\n" + imagePath, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pb.Image = null;
                if (currentImage != null)
                {
                    currentImage.Dispose();
                    currentImage = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("画像の読み込みに失敗しました:\n" + imagePath + "\n\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pb.Image = null;
                if (currentImage != null)
                {
                    currentImage.Dispose();
                    currentImage = null;
                }
            }
        }

        private void ClearCache()
        {
            foreach (var bmp in imageCache.Values)
                bmp.Dispose();
            imageCache.Clear();
        }

        private void UpdateLayout()
        {
            if (pictureBoxRight == null || pictureBoxLeft == null || panelList == null || labelInfo == null) return;

            int clientWidth = this.ClientSize.Width;
            int clientHeight = this.ClientSize.Height;
            int gap = 2;

            int leftImgW = (int)((double)(clientWidth * RatioLeftImg) / TotalRatio);
            int rightImgW = (int)((double)(clientWidth * RatioRightImg) / TotalRatio);
            int listW = clientWidth - leftImgW - rightImgW;
            int height = clientHeight - 30;

            pictureBoxLeft.Bounds = new Rectangle(0, 0, leftImgW - gap, height);
            pictureBoxRight.Bounds = new Rectangle(leftImgW, 0, rightImgW - gap * 2, height);
            panelList.Bounds = new Rectangle(leftImgW + rightImgW, 0, listW, clientHeight);

            if (listBoxFolders != null) { listBoxFolders.Bounds = panelList.ClientRectangle; }

            labelInfo.Location = new Point(10, clientHeight - 25);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearCache();
            if (currentImageRight != null) { currentImageRight.Dispose(); currentImageRight = null; }
            if (currentImageLeft != null) { currentImageLeft.Dispose(); currentImageLeft = null; }
            base.OnFormClosing(e);
        }

        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.json");
    }
}