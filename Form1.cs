using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MangaViewer
{
    public partial class Form1 : Form
    {
        private PictureBox pictureBoxLeft = null!;
        private PictureBox pictureBoxRight = null!;
        private Panel panelList = null!;
        private ListBox listBoxFolders = null!;
        private Label labelInfo = null!;

        // 数字でソート済み（小さい順）
        private List<string> imagePaths = new List<string>();
        // 2ページ単位で進むインデックス
        private int currentIndex = 0;
        private string currentFolder = "";

        // フォルダリスト管理（直下サブフォルダを昇順ソート）
        private List<string> folderList = new List<string>();
        private int currentFolderIndex = -1;

        private Image? currentImageRight = null;
        private Image? currentImageLeft = null;

        // Phase 11: リサイズ用比率 (左画像 : 右画像 : リスト = 36:36:28)
        private const int RatioLeftImg    = 36;
        private const int RatioRightImg   = 36;
        private const int RatioList       = 28;
        private const int TotalRatio      = RatioLeftImg + RatioRightImg + RatioList;

        // setting.json の保存パス（実行フォルダ直下）
        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.json");

        public Form1()
        {
            InitializeComponent();
            this.KeyPreview = true;
            this.KeyDown += (s, e) => OnKey(e);

            // ListBox のクリック（選択変更）でフォルダ切り替え
            listBoxFolders.SelectedIndexChanged += (s, e) =>
            {
                if (listBoxFolders.SelectedIndex >= 0 &&
                    listBoxFolders.SelectedIndex < folderList.Count)
                {
                    currentFolderIndex = listBoxFolders.SelectedIndex;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            };

            // 起動時に前回保存されたフォルダを復元
            this.Load += (s, e) => RestoreLastRootFolder();
        }

        // キー操作:
        // - Ctrl+0: 設定ダイアログ表示
        // - Ctrl+1: フォルダ選択（サブフォルダリスト化）＋ 設定保存
        // - ←/→: ページ送り（ListBoxに影響させない）
        // - ↑/↓: フォルダリストの移動＋画像再読み込み
        private void OnKey(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.D0)
            {
                // Ctrl+0: 設定ダイアログを表示
                using (var dialog = new SettingsDialog())
                {
                    dialog.ShowDialog(this);
                }
            }
            else if (e.Control && e.KeyCode == Keys.D1)
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        string rootPath = dialog.SelectedPath;

                        // 保存（setting.json）
                        SaveRootFolder(rootPath);

                        // 直下サブフォルダを昇順で取得・表示
                        BuildSubfolderList(rootPath);

                        if (folderList.Count > 0)
                        {
                            currentFolderIndex = 0;
                            LoadAndSortImages(folderList[currentFolderIndex]);
                        }
                        else
                        {
                            // サブフォルダなし → そのまま選択フォルダを使う（後互換）
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
                // 右矢印: 数字の小さい方へ戻る（2ページ単位）
                if (imagePaths.Count >= 2)
                {
                    currentIndex -= 2;
                    if (currentIndex < 0)
                        currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            }
            else if (e.KeyCode == Keys.Left)
            {
                // 左矢印: 数字の大きい方へ進む（2ページ単位）
                if (imagePaths.Count >= 2)
                {
                    currentIndex += 2;
                    int maxIndex = imagePaths.Count - (imagePaths.Count % 2 == 0 ? 2 : 1);
                    if (currentIndex > maxIndex)
                        currentIndex = maxIndex;
                    DisplayTwoImages(currentIndex);
                }
                else if (imagePaths.Count == 1)
                {
                    DisplayTwoImages(0);
                }
            }
            else if (e.KeyCode == Keys.Up && folderList.Count > 0)
            {
                // 上矢印: フォルダリストの上位へ移動
                if (currentFolderIndex > 0)
                {
                    currentFolderIndex--;
                    listBoxFolders.SelectedIndex = currentFolderIndex;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            }
            else if (e.KeyCode == Keys.Down && folderList.Count > 0)
            {
                // 下矢印: フォルダリストの下位へ移動
                if (currentFolderIndex < folderList.Count - 1)
                {
                    currentFolderIndex++;
                    listBoxFolders.SelectedIndex = currentFolderIndex;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            }
        }

        /// <summary>
        /// Ctrl+1: 選択したフォルダの直下サブフォルダを昇順ソートしてリスト化（Phase 12）
        /// フォルダ名の最後に画像数を "-[x]" の形式で付与
        /// </summary>
        private void BuildSubfolderList(string rootPath)
        {
            folderList.Clear();
            currentFolderIndex = -1;

            try
            {
                var dirs = Directory.GetDirectories(rootPath);

                // フォルダ名（パスの末尾）で昇順ソート
                var sorted = dirs
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                folderList = sorted;

                // ListBox に反映（フォルダ名の最後に画像数を付与）
                listBoxFolders.DataSource = null;
                listBoxFolders.Items.Clear();
                
                // 対応拡張子
                string[] imgExtensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };

                foreach (var dir in folderList)
                {
                    int imageCount = 0;
                    foreach (var ext in imgExtensions)
                    {
                        try
                        {
                            imageCount += Directory.GetFiles(dir, ext, SearchOption.TopDirectoryOnly).Length;
                        }
                        catch
                        {
                            // アクセスできないフォルダはスキップ
                        }
                    }

                    string folderName = Path.GetFileName(dir);
                    listBoxFolders.Items.Add($"{folderName} -[{imageCount}]");
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
        }

        /// <summary>
        /// 設定ファイル(setting.json)から最後に使用したルートフォルダを読み込み、
        /// その直下サブフォルダをリスト表示して初期化する。
        /// </summary>
        private void RestoreLastRootFolder()
        {
            if (!File.Exists(SettingsFilePath))
                return;

            string? rootPath = null;
            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("LastRootFolder", out var prop) &&
                    prop.ValueKind == JsonValueKind.String)
                {
                    rootPath = prop.ToString();
                }
            }
            catch
            {
                // 読み込み失敗時は何もしない（Ctrl+1 で手動選択に任せる）
                return;
            }

            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return;

            // 前回保存されたフォルダを適用
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
        /// ルートフォルダパスを setting.json に保存する。
        /// </summary>
        private void SaveRootFolder(string rootPath)
        {
            try
            {
                var obj = new Dictionary<string, string>
                {
                    { "LastRootFolder", rootPath }
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(obj, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // 書き込み失敗時は静かに失敗（アプリ自体は続行）
            }
        }

        private void InitializeComponent()
        {
            // フォーム基本設定
            this.Text = "Manga Viewer";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(1400, 800);
            this.MinimumSize = new Size(900, 600);
            this.BackColor = Color.Black;

            // 右側 PictureBox（小さい数字の画像用）
            pictureBoxRight = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
            };

            // 左側 PictureBox（大きい数字の画像用）
            pictureBoxLeft = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
            };

            this.Controls.Add(pictureBoxRight);
            this.Controls.Add(pictureBoxLeft);

            // Phase 7: 情報表示ラベル（下部中央）
            labelInfo = new Label
            {
                Text = "フォルダを選択してください (Ctrl+1)",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(64, 64, 64),
            };
            this.Controls.Add(labelInfo);

            // Phase 11: リスト表示エリア（右側パネル）
            panelList = new Panel
            {
                BackColor = Color.FromArgb(30, 30, 30),
                BorderStyle = BorderStyle.FixedSingle,
            };
            this.Controls.Add(panelList);

            // Phase 12: フォルダリスト (ListBox) を右パネルに配置
            listBoxFolders = new ListBox
            {
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Meiryo UI", 9F),
                SelectionMode = SelectionMode.One,
                HorizontalScrollbar = true,
                TabStop = false, // ←/→がListBox選択に捕られないように
            };
            panelList.Controls.Add(listBoxFolders);

            // 初期レイアウト適用
            UpdateLayout();

            // イベント
            this.Resize += (s, e) => UpdateLayout();
        }

        /// <summary>
        /// 指定フォルダから画像ファイルを読み込んで、ファイル名の数字部分で昇順ソートする。
        /// 対応形式: jpg, jpeg, webp, png
        /// </summary>
        private void LoadAndSortImages(string folderPath)
        {
            currentFolder = folderPath;
            imagePaths.Clear();

            string[] extensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };
            var allFiles = new List<string>();

            foreach (var ext in extensions)
            {
                try
                {
                    allFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly));
                }
                catch
                {
                    // アクセスできないフォルダなどはスキップ
                }
            }

            imagePaths = allFiles
                .OrderBy(f => ExtractNumberFromFileName(f))
                .ToList();
        }

        /// <summary>
        /// ファイル名から数字部分を抽出する（例: "page_001.jpg" -> 1）。
        /// 数字が見つからない場合は 0 を返す。
        /// </summary>
        private int ExtractNumberFromFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"\d+");

            if (match.Success && int.TryParse(match.Value, out int number))
            {
                return number;
            }

            return 0;
        }

        /// <summary>
        /// 2枚の画像を同時表示（右側=小さい数字、左側=大きい数字）。
        /// </summary>
        private void DisplayTwoImages(int startIndex)
        {
            if (imagePaths.Count == 0)
            {
                pictureBoxRight.Image = null;
                pictureBoxLeft.Image = null;
                labelInfo.Text = "表示可能な画像がありません。";
                return;
            }

            // 右側（startIndex: 小さい数字）
            LoadImageIntoPictureBox(
                pictureBoxRight,
                ref currentImageRight,
                startIndex < imagePaths.Count ? imagePaths[startIndex] : null);

            // 左側（startIndex+1: 大きい数字）
            LoadImageIntoPictureBox(
                pictureBoxLeft,
                ref currentImageLeft,
                (startIndex + 1) < imagePaths.Count ? imagePaths[startIndex + 1] : null);

            // Phase 7: 情報表示更新
            UpdateInfoLabel(startIndex);
        }

        /// <summary>
        /// Phase 7: 情報ラベルを更新する（話番号＋ページ組位置）
        /// </summary>
        private void UpdateInfoLabel(int startIndex)
        {
            if (imagePaths.Count == 0) return;

            int rightPageNum = ExtractNumberFromFileName(imagePaths[startIndex]);
            int spreadIndex = startIndex / 2 + 1;
            int totalSpreads = (imagePaths.Count + 1) / 2;

            string folderName = Path.GetFileName(currentFolder);
            string folderInfo = !string.IsNullOrEmpty(folderName) ? $"[{folderName}] " : "";
            string folderIndexInfo = folderList.Count > 1
                ? $"{currentFolderIndex + 1}/{folderList.Count}話 "
                : "";

            if (startIndex + 1 < imagePaths.Count)
            {
                int leftPageNum = ExtractNumberFromFileName(imagePaths[startIndex + 1]);
                labelInfo.Text = folderInfo + folderIndexInfo
                    + $"右: {rightPageNum} | 左: {leftPageNum} | {spreadIndex}/{totalSpreads} ページ組";
            }
            else
            {
                labelInfo.Text = folderInfo + folderIndexInfo
                    + $"右: {rightPageNum} | {spreadIndex}/{totalSpreads} ページ組";
            }
        }

        /// <summary>
        /// 画像を PictureBox に安全に読み込み（メモリリーク防止）。
        /// </summary>
        private void LoadImageIntoPictureBox(PictureBox pb, ref Image? currentImage, string? imagePath)
        {
            // 前の画像をDispose
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
                using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    currentImage = new Bitmap(stream);
                }
                pb.Image = currentImage;
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show(
                    "画像の読み込みに失敗しました:\n" + imagePath,
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                pb.Image = null;
                if (currentImage != null)
                {
                    currentImage.Dispose();
                    currentImage = null;
                }
            }
        }

        // Phase 11: 3エリアレイアウト（比率固定）＋リサイズ対応
        private void UpdateLayout()
        {
            if (pictureBoxRight == null || pictureBoxLeft == null || panelList == null || labelInfo == null)
                return;

            int clientWidth = this.ClientSize.Width;
            int clientHeight = this.ClientSize.Height;
            int gap = 2; // エリア間のギャップ（px）

            int leftImgW    = (int)((double)(clientWidth * RatioLeftImg) / TotalRatio);
            int rightImgW   = (int)((double)(clientWidth * RatioRightImg) / TotalRatio);
            int listW       = clientWidth - leftImgW - rightImgW;

            int height = clientHeight - 30; // 下部情報表示領域分確保

            pictureBoxLeft.Bounds    = new Rectangle(0, 0, leftImgW - gap, height);
            pictureBoxRight.Bounds   = new Rectangle(leftImgW, 0, rightImgW - gap * 2, height);
            panelList.Bounds         = new Rectangle(leftImgW + rightImgW, 0, listW, clientHeight);

            // Phase 12: ListBox はpanelListいっぱいに広げる（少しマージン）
            if (listBoxFolders != null)
            {
                listBoxFolders.Bounds = panelList.ClientRectangle;
            }

            // ラベルは中央（左右画像の下部付近）に配置
            labelInfo.Location = new Point(10, clientHeight - 25);
        }

        /// <summary>
        /// フォーム閉じる前にリソースを解放
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (currentImageRight != null)
            {
                currentImageRight.Dispose();
                currentImageRight = null;
            }

            if (currentImageLeft != null)
            {
                currentImageLeft.Dispose();
                currentImageLeft = null;
            }

            base.OnFormClosing(e);
        }
    }
}