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
        /// Display manager for dynamic PictureBox array (2 or 8 images)
        /// </summary>
        internal DisplayManager _displayManager = null!;

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
        /// Settings instance for configuration management.
        /// </summary>
        private Settings _settings = new Settings();

        /// <summary>
        /// Image service for loading and caching images.
        /// </summary>
        private readonly ImageService _imageService = new ImageService();

        /// <summary>
        /// Folder service for folder scanning, filtering, and cache management.
        /// </summary>
        private FolderService? _folderService;

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
            
            // PreviewKeyDown で矢印キーを IsInputKey に設定し、OSのキーリピート遅延を回避
            this.PreviewKeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
                    e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                    e.IsInputKey = true;
            };
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            _settings = SettingsManager.Load();
            _displayManager = new DisplayManager(this, _settings, _imageService);
            _displayManager.ImagePaths = _imagePaths;

            string rootPath = string.IsNullOrEmpty(_settings.LastRootFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _settings.LastRootFolder;

            BuildSubfolderList(rootPath);
            
            // BuildSubfolderList で_settings が再Loadされるので、DisplayManagerにも反映
            _displayManager.UpdateSettings(_settings);

            if (_folderList.Count > 0)
            {
                _currentFolderIndex = 0;
                LoadAndSortImages(_folderList[0]);
                _displayManager.ImagePaths = _imagePaths;
                _currentIndex = 0;
                _displayManager.InitializePictureBoxes();
                this.PerformLayout();
                UpdateLayout();
                _displayManager.DisplayImages(0);
                listBoxFolders.SelectedIndex = 0;
            }
        }

        internal void BuildSubfolderList(string rootPath)
        {
            _folderList.Clear();
            _currentFolderIndex = -1;

            // UIに「読込中」メッセージを表示し、画面を更新
            labelInfo.Text = "フォルダを読み込み中...";
            Application.DoEvents();

            _settings = SettingsManager.Load();
            _folderService = new FolderService(_settings);

            try
            {
                // BuildFolderIndex を1回だけ呼び出し、_folderList と ListBox表示を同時更新
                var entries = _folderService.BuildFolderIndex(rootPath);
                _folderList.Clear();
                listBoxFolders.DataSource = null;
                listBoxFolders.Items.Clear();

                foreach (var entry in entries)
                {
                    _folderList.Add(entry.Path);
                    string folderName = Path.GetFileName(entry.Path);
                    int rating = RatingService.ReadRating(entry.Path);
                    string ratingPrefix = rating >= 0 ? $"[{rating}] " : "";
                    listBoxFolders.Items.Add($"{ratingPrefix}{folderName} -[{entry.ImageCount}]");
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
            if (_folderService == null)
                _folderService = new FolderService(_settings);
            _imagePaths = _folderService.LoadAndSortImages(folderPath);
            _displayManager.ImagePaths = _imagePaths;
        }

        private void InitializeComponent()
        {
            this.Text = Constants.AppTitle;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(Constants.InitialWidth, Constants.InitialHeight);
            this.MinimumSize = new Size(Constants.MinWidth, Constants.MinHeight);
            this.BackColor = Color.Black;

            // PictureBoxes are created dynamically by DisplayManager.InitializePictureBoxes()
            
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

        internal void DisplayImages(int startIndex)
        {
            _displayManager.ImagePaths = _imagePaths;
            _displayManager.DisplayImages(startIndex);
            
            // Update info label using DisplayManager
            string infoText = _displayManager.GetInfoText(_currentFolder, _currentFolderIndex, _folderList);
            if (!string.IsNullOrEmpty(infoText))
                labelInfo.Text = infoText;
            else
                labelInfo.Text = "表示可能な画像がありません。";
        }

        private void UpdateLayout()
        {
            if (panelList == null || labelInfo == null) return;
            if (_displayManager == null) return;

            int clientWidth = this.ClientSize.Width;
            int clientHeight = this.ClientSize.Height;

            var bounds = _displayManager.CalculatePictureBoxBounds(clientWidth, clientHeight, out Rectangle listPanelBounds);
            
            for (int i = 0; i < _displayManager.pictureBoxes.Length; i++)
                _displayManager.pictureBoxes[i].Bounds = bounds[i];

            panelList.Bounds = listPanelBounds;
            if (listBoxFolders != null) { listBoxFolders.Bounds = panelList.ClientRectangle; }
            labelInfo.Location = new Point(10, clientHeight - 25);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _imageService.Dispose();
            _displayManager?.Dispose();
            base.OnFormClosing(e);
        }

        #region INavigationActions インターフェースの実装

        public int ImageCount => _imagePaths.Count;
        public int FolderListCount => _folderList.Count;
        public string CurrentFolder => _currentFolder;

        public void ShowSettingsDialog()
        {
            int prevDisplayCount = _settings.DisplayCount;
            int prevMinDisplayCount = _settings.MinDisplayCount;
            int prevMinEvaluation = _settings.MinEvaluation;

            using (var dialog = new SettingsDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _settings = SettingsManager.Load();
                    _displayManager.UpdateSettings(_settings);

                    // 最小表示枚数または最小評価値が変更された場合はフォルダリストを再フィルタ
                    if (_settings.MinDisplayCount != prevMinDisplayCount || _settings.MinEvaluation != prevMinEvaluation)
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
                                DisplayImages(0);
                                listBoxFolders.SelectedIndex = 0;
                            }
                            else
                            {
                                _folderList.Clear();
                                _currentFolderIndex = -1;
                                labelInfo.Text = "表示可能なフォルダがありません。";
                            }
                        }
                    }

                    // DisplayCountが変更された場合はPictureBoxを再構築
                    if (_settings.DisplayCount != prevDisplayCount)
                    {
                        _displayManager.InitializePictureBoxes();
                        UpdateLayout();
                        _currentIndex = 0;
                        DisplayImages(0);
                    }
                }
            }
        }

        public void ChangeRootFolder(string rootPath)
        {
            _settings.LastRootFolder = rootPath;
            SettingsManager.Save(_settings);
            BuildSubfolderList(rootPath);
            
            // BuildSubfolderList で_settings が再Loadされるので、DisplayManagerにも反映
            _displayManager.UpdateSettings(_settings);

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
            DisplayImages(_currentIndex);
            listBoxFolders.SelectedIndex = _currentFolderIndex;
        }

        public void NavigateBackwardTwoPages()
        {
            if (_imagePaths.Count > 0)
            {
                _currentIndex -= _displayManager.NavigationStep;
                if (_currentIndex < 0) _currentIndex = 0;
                DisplayImages(_currentIndex);
            }
        }

        public void NavigateForwardTwoPages()
        {
            if (_imagePaths.Count > 0)
            {
                _currentIndex += _displayManager.NavigationStep;
                int maxIndex = _imagePaths.Count - (_imagePaths.Count % _displayManager.DisplayCount == 0 ? _displayManager.DisplayCount : 1);
                if (_currentIndex > maxIndex) _currentIndex = maxIndex;
                DisplayImages(_currentIndex);
            }
            else if (_imagePaths.Count == 1)
            {
                DisplayImages(0);
            }
        }

        public void NavigateFolderUp()
        {
            if (_currentFolderIndex > 0)
            {
                _currentFolderIndex--;
                LoadAndSortImages(_folderList[_currentFolderIndex]);
                _currentIndex = 0;
                DisplayImages(_currentIndex);
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
                DisplayImages(_currentIndex);
                listBoxFolders.SetSelected(_currentFolderIndex, true);
            }
        }

        public void NavigateToNextUnrated()
        {
            // 先頭から未評価（rating == -1）のフォルダを探す
            for (int i = 0; i < _folderList.Count; i++)
            {
                if (RatingService.ReadRating(_folderList[i]) == -1)
                {
                    _currentFolderIndex = i;
                    LoadAndSortImages(_folderList[i]);
                    _currentIndex = 0;
                    DisplayImages(0);
                    listBoxFolders.SelectedIndex = i;
                    return;
                }
            }
            // 未評価のフォルダが存在しない場合は何もしない
        }

        #endregion
    }
}