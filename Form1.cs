using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// The main form for the Manga Viewer application.
    /// Implements <see cref="INavigationActions"/> for navigation actions.
    /// </summary>
    public partial class Form1 : Form, INavigationActions
    {
        /// <summary>
        /// PictureBox to display the left manga image.
        /// </summary>
        internal PictureBox pictureBoxLeft = null!;

        /// <summary>
        /// PictureBox to display the right manga image.
        /// </summary>
        internal PictureBox pictureBoxRight = null!;

        /// <summary>
        /// Panel containing folder list and other controls.
        /// </summary>
        private Panel panelList = null!;

        /// <summary>
        /// ListBox to display the list of folders.
        /// </summary>
        internal ListBox listBoxFolders = null!;

        /// <summary>
        /// Label to display information about the current folder and image.
        /// </summary>
        internal Label labelInfo = null!;

        /// <summary>
        /// List of image paths in the current folder.
        /// </summary>
        internal List<string> _imagePaths = new List<string>();

        /// <summary>
        /// Index of the currently displayed image.
        /// </summary>
        internal int _currentIndex = 0;

        /// <summary>
        /// Path to the current folder being viewed.
        /// </summary>
        internal string _currentFolder = "";

        /// <summary>
        /// List of folders containing manga images.
        /// </summary>
        internal List<string> _folderList = new List<string>();

        /// <summary>
        /// Index of the currently selected folder in listBoxFolders.
        /// </summary>
        internal int _currentFolderIndex = -1;

        /// <summary>
        /// The image currently displayed on pictureBoxRight.
        /// </summary>
        private Image? currentImageRight = null;

        /// <summary>
        /// The image currently displayed on pictureBoxLeft.
        /// </summary>
        private Image? currentImageLeft = null;

        /// <summary>
        /// Settings instance for configuration management.
        /// </summary>
        private Settings _settings = new Settings();

        /// <summary>
        /// Image service for loading and caching images.
        /// </summary>
        private readonly ImageService _imageService = new ImageService();

        /// <summary>
        /// Constructor for Form1. Initializes components and subscribes to Load event.
        /// </summary>
        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;
            
            // キーボード入力処理の設定
            this.KeyPreview = true;
            this.KeyDown += (s, e) => KeyboardInputHandler.HandleKeyDown(e, this);
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            _settings = SettingsManager.Load();

            string rootPath = string.IsNullOrEmpty(_settings.LastRootFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _settings.LastRootFolder;

            BuildSubfolderList(rootPath);

            if (_folderList.Count > 0)
            {
                _currentFolderIndex = 0;
                LoadAndSortImages(_folderList[0]);
                _currentIndex = 0;
                DisplayTwoImages(0);
                listBoxFolders.SelectedIndex = 0;
            }
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
                foreach (var (fp, ic) in folderData)
                {
                    string folderName = Path.GetFileName(fp);
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

        private void InitializeComponent()
        {
            this.Text = Constants.AppTitle;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(Constants.InitialWidth, Constants.InitialHeight);
            this.MinimumSize = new Size(Constants.MinWidth, Constants.MinHeight);
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

            int leftImgW = (int)((double)(clientWidth * Constants.RatioLeftImg) / Constants.TotalRatio);
            int rightImgW = (int)((double)(clientWidth * Constants.RatioRightImg) / Constants.TotalRatio);
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

        #region INavigationActions インターフェースの実装

        public int ImageCount => _imagePaths.Count;
        public int FolderListCount => _folderList.Count;
        public string CurrentFolder => _currentFolder;

        public void ShowSettingsDialog()
        {
            bool prevEnabled = _settings.MinDisplayCountEnabled;
            int prevValue = _settings.MinDisplayCount;

            using (var dialog = new SettingsDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _settings = SettingsManager.Load();

                    // 最小表示枚数設定が変更された場合はフォルダリストを再フィルタ
                    if (_settings.MinDisplayCountEnabled != prevEnabled || _settings.MinDisplayCount != prevValue)
                    {
                        string rootPath = string.IsNullOrEmpty(_settings.LastRootFolder)
                            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                            : _settings.LastRootFolder;
                        BuildSubfolderList(rootPath);

                        // 現在のフォルダが新しいリストに含まれているか確認
                        int newIdx = -1;
                        for (int i = 0; i < _folderList.Count; i++)
                        {
                            if (_folderList[i] == _currentFolder) { newIdx = i; break; }
                        }

                        if (newIdx >= 0)
                        {
                            _currentFolderIndex = newIdx;
                            listBoxFolders.SelectedIndex = newIdx;
                        }
                        else
                        {
                            if (_folderList.Count > 0)
                            {
                                _currentFolderIndex = 0;
                                LoadAndSortImages(_folderList[0]);
                                _currentIndex = 0;
                                DisplayTwoImages(0);
                                listBoxFolders.SelectedIndex = 0;
                            }
                            else
                            {
                                _folderList.Clear();
                                _currentFolderIndex = -1;
                                pictureBoxRight.Image = null;
                                pictureBoxLeft.Image = null;
                                labelInfo.Text = "表示可能なフォルダがありません。";
                            }
                        }
                    }
                }
            }
        }

        public void ChangeRootFolder(string rootPath)
        {
            _settings.LastRootFolder = rootPath;
            SettingsManager.Save(_settings);
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

        public void NavigateBackwardTwoPages()
        {
            if (_imagePaths.Count >= 2)
            {
                _currentIndex -= 2;
                if (_currentIndex < 0) _currentIndex = 0;
                DisplayTwoImages(_currentIndex);
            }
        }

        public void NavigateForwardTwoPages()
        {
            if (_imagePaths.Count >= 2)
            {
                _currentIndex += 2;
                int maxIndex = _imagePaths.Count - (_imagePaths.Count % 2 == 0 ? 2 : 1);
                if (_currentIndex > maxIndex) _currentIndex = maxIndex;
                DisplayTwoImages(_currentIndex);
            }
            else if (_imagePaths.Count == 1)
            {
                DisplayTwoImages(0);
            }
        }

        public void NavigateFolderUp()
        {
            if (_currentFolderIndex > 0)
            {
                _currentFolderIndex--;
                LoadAndSortImages(_folderList[_currentFolderIndex]);
                _currentIndex = 0;
                DisplayTwoImages(_currentIndex);
                listBoxFolders.SetSelected(_currentFolderIndex, true);
            }
        }

        public void NavigateFolderDown()
        {
            if (_currentFolderIndex < _folderList.Count - 1)
            {
                _currentFolderIndex++;
                LoadAndSortImages(_folderList[_currentFolderIndex]);
                _currentIndex = 0;
                DisplayTwoImages(_currentIndex);
                listBoxFolders.SetSelected(_currentFolderIndex, true);
            }
        }

        #endregion
    }
}