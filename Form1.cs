using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

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

        // Services
        internal ImageService _imageService = new ImageService();

        private const int RatioLeftImg = 36;
        private const int RatioRightImg = 36;
        private const int RatioList = 28;
        internal const int TotalRatio = RatioLeftImg + RatioRightImg + RatioList;

        // アプリケーション設定
        internal Settings _settings = new Settings();

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
            _settings = SettingsManager.Load();

            string rootPath = string.IsNullOrEmpty(_settings.LastRootFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _settings.LastRootFolder;

            if (!Directory.Exists(rootPath)) return;

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

        internal void BuildSubfolderList(string rootPath)
        {
            _folderList.Clear();
            _currentFolderIndex = -1;

            // UIに「読込中」メッセージを表示し、画面を更新
            labelInfo.Text = "フォルダ一覧を読み込み中...";
            Application.DoEvents();

            _settings = SettingsManager.Load();
            var folderService = new FolderService(_settings);

            try
            {
                _folderList = folderService.BuildSubfolderList(rootPath);

                // ListBox 表示用データ作成
                var folderData = folderService.GetFolderDisplayData(rootPath);

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

        internal void LoadAndSortImages(string folderPath)
        {
            _currentFolder = folderPath;
            var folderService = new FolderService(_settings);
            _imagePaths = folderService.LoadAndSortImages(folderPath);
        }

        internal void SaveRatingToFolder(string folderPath, int rating)
        {
            FolderService.SaveRatingToFolder(folderPath, rating);
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
                System.Threading.Tasks.Task.Run(() =>
                {
                    if (nextIdx < _imagePaths.Count) _imageService.LoadOrGetCachedImage(_imagePaths[nextIdx]);
                    if (nextNextIdx < _imagePaths.Count) _imageService.LoadOrGetCachedImage(_imagePaths[nextNextIdx]);
                });
            }
        }

        private void UpdateInfoLabel(int startIndex)
        {
            if (_imagePaths.Count == 0) return;
            int rightPageNum = FolderService.ExtractNumberFromFileName(_imagePaths[startIndex]);
            int spreadIndex = startIndex / 2 + 1;
            int totalSpreads = (_imagePaths.Count + 1) / 2;
            string folderName = Path.GetFileName(_currentFolder);
            string folderInfo = !string.IsNullOrEmpty(folderName) ? $"[{folderName}] " : "";
            string folderIndexInfo = _folderList.Count > 1 ? $"{_currentFolderIndex + 1}/{_folderList.Count}話 " : "";

            if (startIndex + 1 < _imagePaths.Count)
            {
                int leftPageNum = FolderService.ExtractNumberFromFileName(_imagePaths[startIndex + 1]);
                labelInfo.Text = folderInfo + folderIndexInfo + $"右: {rightPageNum} | 左: {leftPageNum} | {spreadIndex}/{totalSpreads} ページ組";
            }
            else
            {
                labelInfo.Text = folderInfo + folderIndexInfo + $"右: {rightPageNum} | {spreadIndex}/{totalSpreads} ページ組";
            }
        }

        private void LoadImageIntoPictureBox(PictureBox pb, ref Image? currentImage, string? imagePath)
        {
            if (currentImage != null)
            {
                ImageService.DisposeImage(currentImage);
                currentImage = null;
            }

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                pb.Image = null;
                return;
            }

            try
            {
                currentImage = _imageService.LoadOrGetCachedImage(imagePath);
                pb.Image = currentImage;
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show("画像の読み込みに失敗しました:\n" + imagePath, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pb.Image = null;
                if (currentImage != null)
                {
                    ImageService.DisposeImage(currentImage);
                    currentImage = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("画像の読み込みに失敗しました:\n" + imagePath + "\n\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pb.Image = null;
                if (currentImage != null)
                {
                    ImageService.DisposeImage(currentImage);
                    currentImage = null;
                }
            }
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
            _imageService.Dispose();
            if (currentImageRight != null) { currentImageRight.Dispose(); currentImageRight = null; }
            if (currentImageLeft != null) { currentImageLeft.Dispose(); currentImageLeft = null; }
            base.OnFormClosing(e);
        }
    }
}