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
        /// Full-screen display mode toggle (FormBorderStyle=None + Maximized).
        /// </summary>
        internal bool _fullScreenMode = false;

        /// <summary>Saved window state for restoring from full-screen.</summary>
        private FormWindowState _savedWindowState = FormWindowState.Normal;

        /// <summary>Saved form border style for restoring from full-screen.</summary>
        private FormBorderStyle _savedFormBorderStyle = FormBorderStyle.Sizable;

        /// <summary>Saved window size for restoring from full-screen.</summary>
        private Size _savedSize = new Size(Constants.InitialWidth, Constants.InitialHeight);

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
        /// Slideshow timer to automatically advance images.
        /// </summary>
        private Timer? _slideshowTimer;

        /// <summary>
        /// Whether the slideshow is currently running.
        /// </summary>
        public bool IsSlideshowRunning => _slideshowTimer?.Enabled ?? false;

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
                    e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                    e.KeyCode == Keys.Escape)
                    e.IsInputKey = true;
            };
        }

        // ListBox がフォーカス中でも Ctrl+Up/Down を確実に捕まえるため、ProcessCmdKey をオーバーライドする。
        protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Up) || keyData == (Keys.Control | Keys.Down))
            {
                var e = new KeyEventArgs(keyData);
                KeyboardInputHandler.HandleKeyDown(e, this);
                return true; // 処理済みとしてここで吸収
            }

            return base.ProcessCmdKey(ref m, keyData);
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            try
            {
                var (loadedSettings, errors) = SettingsManager.LoadWithValidation();
                _settings = loadedSettings;

                if (errors.Count > 0)
                {
                    MessageBox.Show(
                        this,
                        "設定に不正な値が含まれているため、該当項目はデフォルト値を使用します。" + Environment.NewLine +
                            Environment.NewLine +
                            string.Join(Environment.NewLine, errors),
                        "設定エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                _displayManager = new DisplayManager(this, _settings, _imageService);
                _displayManager.ImagePaths = _imagePaths;

                string rootPath = string.IsNullOrEmpty(_settings.LastRootFolder)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : _settings.LastRootFolder;

                BuildSubfolderList(rootPath);
                
                // BuildSubfolderList で_settings が再Loadされるので、DisplayManagerにも反映
                _displayManager.UpdateSettings(_settings);

                if (_folderList.Count > 0 && _currentFolderIndex >= 0)
                {
                    LoadAndSortImages(_folderList[_currentFolderIndex]);
                    _displayManager.ImagePaths = _imagePaths;
                    _currentIndex = 0;
                    _displayManager.InitializePictureBoxes();
                    this.PerformLayout();
                    UpdateLayout();
                    _displayManager.DisplayImages(0);
                    listBoxFolders.SelectedIndex = _currentFolderIndex;
                }
            }
            catch (Exception ex)
            {
                // 初期化失敗時でもUIは最低限表示されるようにする
                EnsureBasicLayout();

                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                try { File.AppendAllText(logFile, $"Form1_Load error: {ex}{Environment.NewLine}"); } catch { }

                labelInfo.Text = "初期化エラーが発生しました。";
            }
        }

        /// <summary>
        /// 画像読み込み失敗等でも、フォームとリストパネルの基本的なレイアウトを保証する。
        /// </summary>
        private void EnsureBasicLayout()
        {
            if (this.ClientSize.Width < Constants.MinWidth || this.ClientSize.Height < Constants.MinHeight)
                this.Size = new Size(Constants.InitialWidth, Constants.InitialHeight);

            int cw = this.ClientSize.Width;
            int ch = this.ClientSize.Height;

            int listX = (int)(cw * 0.72);
            int listW = cw - listX;

            panelList.Bounds = new Rectangle(listX, 0, listW, ch);
            if (listBoxFolders != null)
                listBoxFolders.Bounds = panelList.ClientRectangle;

            labelInfo.Visible = !_fullScreenMode;
            if (!_fullScreenMode && labelInfo.Visible)
                labelInfo.Location = new Point(10, ch - 25);
        }

        internal void BuildSubfolderList(string rootPath)
        {
            _folderList.Clear();
            _currentFolderIndex = -1;

            // UIに「読込中」メッセージを表示し、画面を更新
            labelInfo.Text = "フォルダを読み込み中...";
            Application.DoEvents();

            // BuildSubfolderList 内で不要な再Loadを抑え、既存インメモリ設定を使用する
            if (_folderService == null)
                _folderService = new FolderService(_settings);

            try
            {
                // BuildFolderIndex を1回だけ呼び出し、_folderList と ListBox表示を同時更新
                var entries = _folderService.BuildFolderIndex(rootPath);
                _folderList.Clear();
                listBoxFolders.DataSource = null;
                listBoxFolders.Items.Clear();

                int firstUnratedIndex = -1;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    _folderList.Add(entry.Path);
                    string folderName = Path.GetFileName(entry.Path);
                    int rating = RatingService.ReadRating(entry.Path);
                    string ratingPrefix = rating >= 0 ? $"[{rating}] " : "";
                    listBoxFolders.Items.Add($"{ratingPrefix}{folderName} -[{entry.ImageCount}]");

                    if (firstUnratedIndex < 0 && rating < 0)
                        firstUnratedIndex = i;
                }

                if (_folderList.Count > 0)
                {
                    int initialIndex = firstUnratedIndex >= 0 ? firstUnratedIndex : 0;
                    listBoxFolders.SelectedIndex = initialIndex;
                    _currentFolderIndex = initialIndex;
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
            // ListBox がフォーカス中でも Ctrl＋上下を確実に捕捉するため、ListBox の KeyDown から KeyboardInputHandler を呼び出す
            listBoxFolders.KeyDown += (s, e) => KeyboardInputHandler.HandleKeyDown(e, this);

            // 矢印キー（Ctrl付き含む）が KeyDown に到達するように IsInputKey = true を設定
            listBoxFolders.PreviewKeyDown += (s, e) =>
            {
                Keys code = e.KeyCode;
                if (code == Keys.Up || code == Keys.Down ||
                    code == Keys.Left || code == Keys.Right)
                {
                    e.IsInputKey = true;
                }
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

            var bounds = _displayManager.CalculatePictureBoxBounds(clientWidth, clientHeight, out Rectangle listPanelBounds, _fullScreenMode);
            
            for (int i = 0; i < _displayManager.pictureBoxes.Length; i++)
                _displayManager.pictureBoxes[i].Bounds = bounds[i];

            panelList.Bounds = listPanelBounds;
            if (listBoxFolders != null) { listBoxFolders.Bounds = panelList.ClientRectangle; }
            
            // フルサイズモード時はlabelInfoを非表示、通常時は再表示
            labelInfo.Visible = !_fullScreenMode;
            if (!_fullScreenMode)
                labelInfo.Location = new Point(10, clientHeight - 25);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _slideshowTimer?.Stop();
            _slideshowTimer?.Dispose();
            _imageService.Dispose();
            _displayManager?.Dispose();
            base.OnFormClosing(e);
        }

        /// <summary>
        /// フルサイズモードのトグル（数字キー8で発動）
        /// 仕様: FormBorderStyle=None + Maximized で画面埋め尽くし
        /// リストパネルは維持するが極狭に、画像配置ルール（2枚/8枚）はそのまま。
        /// </summary>
        public void ToggleFullScreen()
        {
            _fullScreenMode = !_fullScreenMode;

            if (_fullScreenMode)
            {
                // 現在のウィンドウ状態を保存
                _savedFormBorderStyle = this.FormBorderStyle;
                _savedWindowState = this.WindowState;
                _savedSize = this.Size;

                // フルサイズに切り替え（タイトルバーなし＋最大化）
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
            }
            else
            {
                // 元のウィンドウ状態を復元
                this.FormBorderStyle = _savedFormBorderStyle;
                this.WindowState = _savedWindowState;
                this.Size = _savedSize;
            }

            // labelInfo のテキストを更新
            string infoText = _displayManager.GetInfoText(_currentFolder, _currentFolderIndex, _folderList);
            if (!string.IsNullOrEmpty(infoText))
                labelInfo.Text = infoText;
            else
                labelInfo.Text = "表示可能な画像がありません。";

            UpdateLayout();
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
                    // ダイアログから戻った後にもファイル再Loadせず、SettingsDialogで適用されたインメモリ設定を使用する
                    // これにより「一度Load失敗→デフォルト」で値が消える問題を回避
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

            if (_folderList.Count > 0 && _currentFolderIndex >= 0)
            {
                LoadAndSortImages(_folderList[_currentFolderIndex]);
                _currentIndex = 0;
                DisplayImages(_currentIndex);
                listBoxFolders.SelectedIndex = _currentFolderIndex;
            }
            else if (_folderList.Count == 0)
            {
                _folderList.Clear();
                _currentFolderIndex = -1;
                LoadAndSortImages(rootPath);
                _currentIndex = 0;
                DisplayImages(_currentIndex);
                labelInfo.Text = "表示可能なフォルダがありません。";
            }
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

        public void NavigateFolderBy(int delta)
        {
            if (_folderList.Count == 0) return;
            int newIndex = _currentFolderIndex + delta;
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= _folderList.Count) newIndex = _folderList.Count - 1;
            if (newIndex == _currentFolderIndex) return;

            _currentFolderIndex = newIndex;
            LoadAndSortImages(_folderList[_currentFolderIndex]);
            _currentIndex = 0;
            DisplayImages(0);
            listBoxFolders.SetSelected(_currentFolderIndex, true);
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

        #region Slideshow implementation

        public void StartSlideshow()
        {
            if (_imagePaths.Count == 0)
                return;

            if (_slideshowTimer == null)
            {
                _slideshowTimer = new Timer();
                _slideshowTimer.Interval = Math.Max(500, _settings.SlideshowIntervalMs);
                _slideshowTimer.Tick += SlideshowTimer_Tick;
            }

            // Reload settings in case interval was changed externally
            _slideshowTimer.Interval = Math.Max(500, _settings.SlideshowIntervalMs);
            _slideshowTimer.Start();
        }

        public void StopSlideshow()
        {
            if (_slideshowTimer != null)
                _slideshowTimer.Stop();
        }

        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            // If current folder has no images, stop slideshow.
            if (_imagePaths.Count == 0)
            {
                StopSlideshow();
                return;
            }

            int step = _displayManager.NavigationStep;

            // Advance index.
            _currentIndex += step;

            // If we reached or passed the end, loop back to start.
            if (_currentIndex >= _imagePaths.Count)
                _currentIndex = 0;

            DisplayImages(_currentIndex);
        }

        #endregion
    }
}