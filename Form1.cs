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
        private PictureBox pictureBoxLeft = null!;
        private PictureBox pictureBoxRight = null!;
        private Panel panelList = null!;
        private ListBox listBoxFolders = null!;
        private Label labelInfo = null!;

        private List<string> imagePaths = new List<string>();
        private int currentIndex = 0;
        private string currentFolder = "";

        private List<string> folderList = new List<string>();
        private int currentFolderIndex = -1;

        private Image? currentImageRight = null;
        private Image? currentImageLeft = null;

        // Image cache for preloading
        private System.Collections.Concurrent.ConcurrentDictionary<string, Bitmap> imageCache = new();
        private const int MaxCacheSize = 200;

        private const int RatioLeftImg = 36;
        private const int RatioRightImg = 36;
        private const int RatioList = 28;
        private const int TotalRatio = RatioLeftImg + RatioRightImg + RatioList;

        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.json");

        private bool minDisplayCountEnabled = false;
        private int minDisplayCountValue = 20;

        // 最小評価値（0-10, デフォルト8）
        private int minEvaluationValue = 8;

        public Form1()
        {
            InitializeComponent();
            this.KeyPreview = true;
            this.KeyDown += (s, e) => OnKey(e);

            listBoxFolders.SelectedIndexChanged += (s, e) =>
            {
                if (listBoxFolders.SelectedIndex >= 0 && listBoxFolders.SelectedIndex < folderList.Count)
                {
                    currentFolderIndex = listBoxFolders.SelectedIndex;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            };

            this.Load += (s, e) => RestoreLastRootFolder();
        }

        private void OnKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D0)
            {
                // 変更前後で最小表示枚数設定が異なるか判定
                bool prevEnabled = minDisplayCountEnabled;
                int prevValue = minDisplayCountValue;

                using (var dialog = new SettingsDialog())
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        LoadSettingsFromFile();

                        // 最小表示枚数設定が変更された場合はフォルダリストを再フィルタ
                        if (minDisplayCountEnabled != prevEnabled || minDisplayCountValue != prevValue)
                        {
                            string rootPath = GetRootFolder();
                            BuildSubfolderList(rootPath);

                            // 現在のフォルダが新しいリストに含まれているか確認
                            int newIdx = -1;
                            for (int i = 0; i < folderList.Count; i++)
                            {
                                if (folderList[i] == currentFolder)
                                { newIdx = i; break; }
                            }

                            if (newIdx >= 0)
                            {
                                // 現在のフォルダがフィルタに一致：表示状態を維持
                                currentFolderIndex = newIdx;
                                listBoxFolders.SelectedIndex = newIdx;
                            }
                            else
                            {
                                // 現在のフォルダがフィルタ外になった：最初のフォルダを選択
                                if (folderList.Count > 0)
                                {
                                    currentFolderIndex = 0;
                                    LoadAndSortImages(folderList[0]);
                                    currentIndex = 0;
                                    DisplayTwoImages(0);
                                    listBoxFolders.SelectedIndex = 0;
                                }
                                else
                                {
                                    folderList.Clear();
                                    currentFolderIndex = -1;
                                    pictureBoxRight.Image = null;
                                    pictureBoxLeft.Image = null;
                                    labelInfo.Text = "表示可能なフォルダがありません。";
                                }
                            }
                        }
                    }
                }
            }
            else if (e.KeyCode == Keys.D1)
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        string rootPath = dialog.SelectedPath;
                        SaveRootFolder(rootPath);
                        BuildSubfolderList(rootPath);
                        if (folderList.Count > 0)
                        {
                            currentFolderIndex = 0;
                            LoadAndSortImages(folderList[currentFolderIndex]);
                        }
                        else
                        {
                            folderList.Clear();
                            currentFolderIndex = -1;
                            LoadAndSortImages(rootPath);
                        }
                        currentIndex = 0;
                        DisplayTwoImages(currentIndex);
                        listBoxFolders.SelectedIndex = currentFolderIndex;
                    }
                }
            }
            else if (e.KeyCode == Keys.Right)
            {
                if (imagePaths.Count >= 2)
                {
                    currentIndex -= 2;
                    if (currentIndex < 0) currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            }
            else if (e.KeyCode == Keys.Left)
            {
                if (imagePaths.Count >= 2)
                {
                    currentIndex += 2;
                    int maxIndex = imagePaths.Count - (imagePaths.Count % 2 == 0 ? 2 : 1);
                    if (currentIndex > maxIndex) currentIndex = maxIndex;
                    DisplayTwoImages(currentIndex);
                }
                else if (imagePaths.Count == 1) { DisplayTwoImages(0); }
            }
            // Ctrl + テンキーで評価値保存（画像表示中のみ）
            else if ((Control.ModifierKeys & Keys.Control) != 0 && imagePaths.Count > 0)
            {
                int rating = e.KeyCode switch
                {
                    Keys.NumPad0 => 0,
                    Keys.NumPad1 => 1,
                    Keys.NumPad2 => 2,
                    Keys.NumPad3 => 3,
                    Keys.NumPad4 => 4,
                    Keys.NumPad5 => 5,
                    Keys.NumPad6 => 6,
                    Keys.NumPad7 => 7,
                    Keys.NumPad8 => 8,
                    Keys.NumPad9 => 9,
                    Keys.Add => 10,    // テンキー +
                    Keys.Subtract => -1, // テンキー -
                    _ => int.MinValue   // 無効なキー
                };

                if (rating != int.MinValue)
                {
                    e.Handled = true;
                    SaveRatingToFolder(currentFolder, rating);
                }
            }
            else if (e.KeyCode == Keys.Up && folderList.Count > 0)
            {
                e.Handled = true;
                if (currentFolderIndex > 0)
                {
                    currentFolderIndex--;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                    listBoxFolders.SetSelected(currentFolderIndex, true);
                }
            }
            else if (e.KeyCode == Keys.Down && folderList.Count > 0)
            {
                e.Handled = true;
                if (currentFolderIndex < folderList.Count - 1)
                {
                    currentFolderIndex++;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                    listBoxFolders.SetSelected(currentFolderIndex, true);
                }
            }
        }

        private void LoadSettingsFromFile()
        {
            if (!File.Exists(SettingsFilePath)) return;
            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("MinDisplayCountEnabled", out var enabledProp))
                {
                    try { minDisplayCountEnabled = enabledProp.GetBoolean(); } catch { }
                }
                if (doc.RootElement.TryGetProperty("MinDisplayCount", out var countProp) && countProp.ValueKind == JsonValueKind.Number)
                {
                    int val = countProp.GetInt32();
                    if (val >= 2 && val <= 100) minDisplayCountValue = val;
                }
                if (doc.RootElement.TryGetProperty("MinEvaluation", out var evalProp) && evalProp.ValueKind == JsonValueKind.Number)
                {
                    int val = evalProp.GetInt32();
                    if (val >= 0 && val <= 10) minEvaluationValue = val;
                }
            }
            catch { }
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
            if (folderList.Count > 0)
            {
                currentFolderIndex = 0;
                LoadAndSortImages(folderList[currentFolderIndex]);
            }
            else
            {
                folderList.Clear();
                currentFolderIndex = -1;
                LoadAndSortImages(rootPath);
            }
            currentIndex = 0;
            DisplayTwoImages(currentIndex);
            listBoxFolders.SelectedIndex = currentFolderIndex;
        }

        /// <summary>
        /// 最後のルートフォルダを setting.json から取得
        /// </summary>
        private string GetRootFolder()
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

        private void SaveRootFolder(string rootPath)
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

        private void BuildSubfolderList(string rootPath)
        {
            folderList.Clear();
            currentFolderIndex = -1;

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
                var toCount = new List<(string dir, string jsonPath)>();

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
                        toCount.Add((dir, jsonPath));
                    }
                }

                // 第二段階: キャッシュ未存在のフォルダをPLINQで並列カウント
                if (toCount.Count > 0)
                {
                    var results = toCount.AsParallel()
                        .WithDegreeOfParallelism(Environment.ProcessorCount)
                        .Select(item => new { item.dir, item.jsonPath, Count = CountImages(item.dir) })
                        .ToList();

                    foreach (var r in results)
                    {
                        cacheResult[r.dir] = r.Count;
                        SaveImageCountJson(r.jsonPath, r.Count);
                    }
                }

                // 第三段階: フィルタ＋ListBox表示用データ作成
                var folderData = new List<(string path, int imageCount)>();

                foreach (var dir in sorted)
                {
                    int ic = cacheResult.TryGetValue(dir, out var v) ? v : 0;

                    // フィルタ基準未満のフォルダは除外
                    if (ic < minDisplayCountValue) continue;

                    folderList.Add(dir);
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

                if (folderList.Count > 0)
                {
                    listBoxFolders.SelectedIndex = 0;
                    currentFolderIndex = 0;
                }
            }
            catch 
            { 
                folderList.Clear(); 
                currentFolderIndex = -1; 
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
        /// 10,000フォルダ以上のケースでも最小のオーバーヘッドに抑える。
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
        private void SaveRatingToFolder(string folderPath, int rating)
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

        private void LoadAndSortImages(string folderPath)
        {
            currentFolder = folderPath;
            imagePaths.Clear();
            string[] extensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };
            var allFiles = new List<string>();
            foreach (var ext in extensions)
            {
                try { allFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly)); } catch { }
            }
            imagePaths = allFiles.OrderBy(f => ExtractNumberFromFileName(f)).ToList();
        }

        private int ExtractNumberFromFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"\d+");
            if (match.Success && int.TryParse(match.Value, out int number)) return number;
            return 0;
        }

        private void DisplayTwoImages(int startIndex)
        {
            if (imagePaths.Count == 0)
            { pictureBoxRight.Image = null; pictureBoxLeft.Image = null; labelInfo.Text = "表示可能な画像がありません。"; return; }

            LoadImageIntoPictureBox(pictureBoxRight, ref currentImageRight, startIndex < imagePaths.Count ? imagePaths[startIndex] : null);
            LoadImageIntoPictureBox(pictureBoxLeft, ref currentImageLeft, (startIndex + 1) < imagePaths.Count ? imagePaths[startIndex + 1] : null);
            UpdateInfoLabel(startIndex);

            // Preload next 2 images into cache in background
            int nextIdx = startIndex + 2;
            int nextNextIdx = startIndex + 3;
            if (nextIdx < imagePaths.Count || nextNextIdx < imagePaths.Count)
            {
                Task.Run(() =>
                {
                    if (nextIdx < imagePaths.Count) LoadOrGetCachedImage(imagePaths[nextIdx]);
                    if (nextNextIdx < imagePaths.Count) LoadOrGetCachedImage(imagePaths[nextNextIdx]);
                });
            }
        }

        private void UpdateInfoLabel(int startIndex)
        {
            if (imagePaths.Count == 0) return;
            int rightPageNum = ExtractNumberFromFileName(imagePaths[startIndex]);
            int spreadIndex = startIndex / 2 + 1;
            int totalSpreads = (imagePaths.Count + 1) / 2;
            string folderName = Path.GetFileName(currentFolder);
            string folderInfo = !string.IsNullOrEmpty(folderName) ? $"[{folderName}] " : "";
            string folderIndexInfo = folderList.Count > 1 ? $"{currentFolderIndex + 1}/{folderList.Count}話 " : "";

            if (startIndex + 1 < imagePaths.Count)
            {
                int leftPageNum = ExtractNumberFromFileName(imagePaths[startIndex + 1]);
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
    }
}