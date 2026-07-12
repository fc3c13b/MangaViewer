using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MangaViewer
{
    public partial class Form1 : Form
    {
        private PictureBox pictureBoxLeft = null!;
        private PictureBox pictureBoxRight = null!;
        private Label labelInfo = null!;

        // 数字でソート済み（小さい順）
        private List<string> imagePaths = new List<string>();
        // 2ページ単位で進むインデックス
        private int currentIndex = 0;
        private string currentFolder = "";

        // フォルダリスト管理（親ディレクトリのサブフォルダを昇順ソート）
        private List<string> folderList = new List<string>();
        private int currentFolderIndex = -1;

        private Image? currentImageRight = null;
        private Image? currentImageLeft = null;

        public Form1()
        {
            InitializeComponent();
            this.KeyPreview = true;
            this.KeyDown += (s, e) => OnKey(e);
        }

        // Phase 5/6: キー操作（ページ送り＋話移動）
        private void OnKey(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.D1)
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        string path = dialog.SelectedPath;
                        BuildFolderList(path);
                        LoadAndSortImages(path);
                        currentIndex = 0;
                        DisplayTwoImages(currentIndex);
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
            else if (e.KeyCode == Keys.Up)
            {
                // 上矢印: フォルダリストの上位へ移動
                if (currentFolderIndex > 0)
                {
                    currentFolderIndex--;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            }
            else if (e.KeyCode == Keys.Down)
            {
                // 下矢印: フォルダリストの下位へ移動
                if (currentFolderIndex < folderList.Count - 1)
                {
                    currentFolderIndex++;
                    LoadAndSortImages(folderList[currentFolderIndex]);
                    currentIndex = 0;
                    DisplayTwoImages(currentIndex);
                }
            }
        }

        /// <summary>
        /// 選択されたフォルダの親ディレクトリにあるサブフォルダを昇順ソートしてリスト化
        /// </summary>
        private void BuildFolderList(string selectedFolder)
        {
            string? parentPath = Directory.GetParent(selectedFolder)?.FullName;
            if (string.IsNullOrEmpty(parentPath))
            {
                folderList.Clear();
                currentFolderIndex = -1;
                return;
            }

            try
            {
                folderList = Directory.GetDirectories(parentPath)
                    .Select(d => d.Replace("\\", "/"))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string normalizedSelected = selectedFolder.Replace("\\", "/");
                currentFolderIndex = folderList.FindIndex(
                    f => string.Equals(f, normalizedSelected, StringComparison.OrdinalIgnoreCase)
                );

                if (currentFolderIndex == -1)
                {
                    currentFolderIndex = 0;
                }
            }
            catch
            {
                folderList.Clear();
                currentFolderIndex = -1;
            }
        }

        private void InitializeComponent()
        {
            // フォーム基本設定
            this.Text = "Manga Viewer";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(1200, 800);
            this.MinimumSize = new Size(800, 600);
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

            // Phase 7: 情報表示ラベル（下部）
            labelInfo = new Label
            {
                Text = "フォルダを選択してください (Ctrl+1)",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(64, 64, 64),
            };
            this.Controls.Add(labelInfo);

            // 初期レイアウト適用
            UpdatePictureBoxLayout();

            // イベント
            this.Resize += (s, e) => UpdatePictureBoxLayout();
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

        private void UpdatePictureBoxLayout()
        {
            if (pictureBoxRight == null || pictureBoxLeft == null || labelInfo == null) return;

            int midX = this.ClientSize.Width / 2;
            int labelHeight = 30;
            int height = this.ClientSize.Height - labelHeight;

            // 右側
            pictureBoxRight.Bounds = new Rectangle(midX, 0, midX - 5, height);

            // 左側
            pictureBoxLeft.Bounds = new Rectangle(0, 0, midX - 5, height);

            // ラベル（下部中央寄り）
            labelInfo.Location = new Point(10, this.ClientSize.Height - labelHeight + 5);
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