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
        private List<string> imagePaths = new List<string>(); // 数字でソート済み（小さい順）
        private int currentIndex = 0; // 2ページ単位で進むインデックス
        private string currentFolder = "";

        // フォルダリスト管理（親ディレクトリのサブフォルダを昇順ソート）
        private List<string> folderList = new List<string>();
        private int currentFolderIndex = -1;

        // 左右の PictureBox と対応する Image
        private PictureBox pictureBoxRight = null;
        private PictureBox pictureBoxLeft = null;
        private Image currentImageRight = null;
        private Image currentImageLeft = null;

        public Form1()
        {
            InitializeComponent();
            InitializeGuiComponents();
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.D1)
            {
                // Ctrl+1 でフォルダ選択
                using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                {
                    DialogResult result = folderDialog.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        string selectedPath = folderDialog.SelectedPath;
                        BuildFolderList(selectedPath);
                        LoadAndSortImages(selectedPath);
                        if (imagePaths.Count > 0)
                        {
                            currentIndex = 0;
                            DisplayTwoImages(currentIndex);
                        }
                        else
                        {
                            MessageBox.Show("選択したフォルダには有効な画像ファイルが見つかりませんでした。");
                        }
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
                    {
                        currentIndex = 0;
                    }
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
                    {
                        currentIndex = maxIndex;
                    }
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
            string parentPath = Directory.GetParent(selectedFolder)?.FullName;
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

        private void Form1_Load(object sender, EventArgs e)
        {
            // 初期表示はしない（フォルダ選択待ち）
        }

        private void InitializeGuiComponents()
        {
            this.Text = "Manga Viewer";
            this.Size = new Size(1200, 800);
            this.MinimumSize = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.Black;

            // 右側の PictureBox（小さい数字の画像を表示）
            pictureBoxRight = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
            };

            // 左側の PictureBox（大きい数字の画像を表示）
            pictureBoxLeft = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
            };

            UpdatePictureBoxLayout();

            this.Controls.Add(pictureBoxRight);
            this.Controls.Add(pictureBoxLeft);

            // 情報表示用 Label
            labelInfo = new Label
            {
                Text = "フォルダを選択してください (Ctrl+1)",
                Location = new Point(10, this.Height - 30),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(64, 64, 64),
            };
            this.Controls.Add(labelInfo);
        }

        private Label labelInfo = null;

        private void UpdatePictureBoxLayout()
        {
            // PictureBoxがまだ初期化されていない場合は何もしない
            if (pictureBoxRight == null || pictureBoxLeft == null) return;

            int midX = this.ClientSize.Width / 2;
            int height = this.ClientSize.Height - 40; // ラベル領域を引く

            pictureBoxRight.Bounds = new Rectangle(midX, 10, midX - 5, height);
            pictureBoxLeft.Bounds = new Rectangle(5, 10, midX - 5, height);

            if (labelInfo != null)
            {
                labelInfo.Location = new Point(10, this.ClientSize.Height - 30);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdatePictureBoxLayout();
        }

        /// <summary>
        /// 指定フォルダから画像ファイルを読み込んで、ファイル名の数字部分でソートする
        /// 対応形式: jpg, jpeg, webp, png
        /// </summary>
        private void LoadAndSortImages(string folderPath)
        {
            // 前の画像をDispose
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

            imagePaths.Clear();
            currentFolder = folderPath;

            string[] extensions = { "*.jpg", "*.jpeg", "*.webp", "*.png" };
            var allFiles = new List<string>();

            foreach (var ext in extensions)
            {
                allFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly));
            }

            // ファイル名から数字部分を抽出してソート
            imagePaths = allFiles
                .OrderBy(f => ExtractNumberFromFileName(f))
                .ToList();

            pictureBoxRight.Image = null;
            pictureBoxLeft.Image = null;
        }

        /// <summary>
        /// ファイル名から数字部分を抽出する（例: "page_001.jpg" -> 1）
        /// 数字が見つからない場合は0を返す
        /// </summary>
        private int ExtractNumberFromFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            
            // 連続する数字を取得
            var match = Regex.Match(fileName, @"\d+");
            if (match.Success)
            {
                return int.Parse(match.Value);
            }
            
            return 0;
        }

        /// <summary>
        /// 2枚の画像を同時表示（右側=小さい数字、左側=大きい数字）
        /// </summary>
        private void DisplayTwoImages(int startIndex)
        {
            if (imagePaths.Count == 0) return;

            // 右側の画像（startIndex = 小さい数字側）
            LoadImageIntoPictureBox(
                pictureBoxRight,
                ref currentImageRight,
                startIndex < imagePaths.Count ? imagePaths[startIndex] : null);

            // 左側の画像（startIndex+1 = 大きい数字側）
            LoadImageIntoPictureBox(
                pictureBoxLeft,
                ref currentImageLeft,
                (startIndex + 1) < imagePaths.Count ? imagePaths[startIndex + 1] : null);

            // 情報更新
            UpdateInfoLabel(startIndex);
        }

        /// <summary>
        /// 画像を PictureBox に安全に読み込み（メモリリーク防止）
        /// </summary>
        private void LoadImageIntoPictureBox(PictureBox pb, ref Image currentImage, string imagePath)
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
                MessageBox.Show("画像の読み込みに失敗しました: " + imagePath);
                pb.Image = null;
                if (currentImage != null)
                {
                    currentImage.Dispose();
                    currentImage = null;
                }
            }
        }

        /// <summary>
        /// 情報ラベルを更新する
        /// </summary>
        private void UpdateInfoLabel(int startIndex)
        {
            if (imagePaths.Count == 0) return;

            int rightPageNum = ExtractNumberFromFileName(imagePaths[startIndex]);
            
            string folderInfo = string.IsNullOrEmpty(currentFolder) ? "" : $"[{Path.GetFileName(currentFolder)}] ";
            string folderIndexInfo = folderList.Count > 1 ? $"/ {folderList.Count}話 " : "";
            
            string infoText;
            if (startIndex + 1 < imagePaths.Count)
            {
                int leftPageNum = ExtractNumberFromFileName(imagePaths[startIndex + 1]);
                infoText = folderInfo + folderIndexInfo + $"右: {rightPageNum} | 左: {leftPageNum} | {(startIndex / 2 + 1)}/{(imagePaths.Count + 1) / 2} ページ組";
            }
            else
            {
                infoText = folderInfo + folderIndexInfo + $"右: {rightPageNum} | {(startIndex / 2 + 1)}/{(imagePaths.Count + 1) / 2} ページ組";
            }

            labelInfo.Text = infoText;
        }

        /// <summary>
        /// フォーム閉じる前にリソースを解放
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (currentImageRight != null)
            {
                currentImageRight.Dispose();
            }
            if (currentImageLeft != null)
            {
                currentImageLeft.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}