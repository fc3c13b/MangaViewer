using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    public partial class Form1 : Form, INavigationActions
    {
        internal DisplayManager _displayManager = null!;
        private Panel panelList = null!;
        internal ListBox listBoxFolders = null!;
        internal Label labelInfo = null!;

        internal List<string> _imagePaths = new List<string>();
        internal int _currentIndex = 0;
        internal string _currentFolder = "";
        internal List<string> _folderList = new List<string>();
        internal int _currentFolderIndex = -1;
        internal bool _fullScreenMode = false;

        private FormWindowState _savedWindowState = FormWindowState.Normal;
        private FormBorderStyle _savedFormBorderStyle = FormBorderStyle.Sizable;
        private Size _savedSize = new Size(Constants.InitialWidth, Constants.InitialHeight);

        private Settings _settings = new Settings();
        private readonly ImageService _imageService = new ImageService();
        private FolderService? _folderService;
        private Timer? _slideshowTimer;

        public bool IsSlideshowRunning => _slideshowTimer?.Enabled ?? false;

        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                // 上下キー：リストの選択移動＋フォルダ表示を一括処理
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    HandleFolderListArrowKey(e);
                    return;
                }

                KeyboardInputHandler.HandleKeyDown(e, this);
            };

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
                    e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                    e.KeyCode == Keys.Escape)
                    e.IsInputKey = true;
            };
        }

        private void HandleFolderListArrowKey(KeyEventArgs e)
        {
            if (_folderList == null || _folderList.Count == 0) return;

            int current = listBoxFolders.SelectedIndex;
            if (current < 0)
                current = _currentFolderIndex >= 0 ? _currentFolderIndex : 0;

            int next = e.KeyCode switch
            {
                Keys.Up   => Math.Max(0, current - 1),
                Keys.Down => Math.Min(_folderList.Count - 1, current + 1),
                _         => current
            };

            if (next == current) return;

            _currentFolderIndex = next;
            listBoxFolders.SelectedIndex = next;

            if (string.IsNullOrEmpty(_folderList[next])) return;
            LoadAndSortImages(_folderList[next]);
            _currentIndex = 0;
            DisplayImages(0);
            e.Handled = true;
        }

        protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Up) || keyData == (Keys.Control | Keys.Down) ||
                keyData == (Keys.Control | Keys.Left) || keyData == (Keys.Control | Keys.Right) ||
                keyData == (Keys.Alt | Keys.Left) || keyData == (Keys.Alt | Keys.Right))
            {
                var e = new KeyEventArgs(keyData);
                KeyboardInputHandler.HandleKeyDown(e, this);
                return true;
            }

            // 上下キーもここで補足し、Form の KeyDown と同じ処理を行う
            if (keyData == Keys.Up || keyData == Keys.Down)
            {
                var e = new KeyEventArgs(keyData);
                HandleFolderListArrowKey(e);
                return e.Handled;
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
                            Environment.NewLine + string.Join(Environment.NewLine, errors),
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

                    labelInfo.Text = "初期化完了";
                }
            }
            catch (Exception ex)
            {
                EnsureBasicLayout();

                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                try { File.AppendAllText(logFile, $"Form1_Load error: {ex}{Environment.NewLine}"); } catch { }

                labelInfo.Text = "初期化エラーが発生しました。";
            }
        }

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

            labelInfo.Text = "フォルダを読み込み中...";
            Application.DoEvents();

            if (_folderService == null)
                _folderService = new FolderService(_settings);

            try
            {
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

            labelInfo = new Label { Text = "フォルダを選択してください (キー1)", AutoSize = true, ForeColor = Color.White, BackColor = Color.FromArgb(64, 64, 64) };
            this.Controls.Add(labelInfo);

            panelList = new Panel { BackColor = Color.FromArgb(30, 30, 30), BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(panelList);

            listBoxFolders = new ListBox
            {
                BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White,
                BorderStyle = BorderStyle.None, Font = new Font("Meiryo UI", 9F),
                SelectionMode = SelectionMode.One, HorizontalScrollbar = true, TabStop = false
            };

            listBoxFolders.KeyDown += (s, e) => KeyboardInputHandler.HandleKeyDown(e, this);

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

        public void ToggleFullScreen()
        {
            _fullScreenMode = !_fullScreenMode;

            if (_fullScreenMode)
            {
                _savedFormBorderStyle = this.FormBorderStyle;
                _savedWindowState = this.WindowState;
                _savedSize = this.Size;

                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
            }
            else
            {
                this.FormBorderStyle = _savedFormBorderStyle;
                this.WindowState = _savedWindowState;
                this.Size = _savedSize;
            }

            string infoText = _displayManager.GetInfoText(_currentFolder, _currentFolderIndex, _folderList);
            if (!string.IsNullOrEmpty(infoText))
                labelInfo.Text = infoText;
            else
                labelInfo.Text = "表示可能な画像がありません。";

            UpdateLayout();
        }

        #region INavigationActions implementation

        public int ImageCount => _imagePaths.Count;
        public int DisplayCount => _settings.DisplayCount;
        public int FolderListCount => _folderList.Count;
        public string CurrentFolder => _currentFolder;

        public void ShowRootFolderDialog()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(_settings.LastRootFolder) && Directory.Exists(_settings.LastRootFolder))
                    dialog.SelectedPath = _settings.LastRootFolder;

                var result = this.InvokeRequired
                    ? (DialogResult)this.Invoke((Func<DialogResult>)(() => dialog.ShowDialog(this)))
                    : dialog.ShowDialog(this);

                if (result == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    ChangeRootFolder(dialog.SelectedPath);
                }
            }
        }

        // 指定パスをルートフォルダとして再構築（内部用）
        public void ChangeRootFolder(string rootPath)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(ChangeRootFolder), rootPath);
                return;
            }

            _settings.LastRootFolder = rootPath;
            SettingsManager.Save(_settings);
            BuildSubfolderList(rootPath);
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

                // If rootPath is valid, try to use it as a single image folder.
                if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
                {
                    LoadAndSortImages(rootPath);
                    _currentIndex = 0;
                    DisplayImages(_currentIndex);
                }
                else
                {
                    _imagePaths.Clear();
                    _currentFolder = "";
                    _currentIndex = 0;
                    DisplayImages(0);
                }

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
            HandleFolderListArrowKey(new KeyEventArgs(Keys.Up));
        }

        public void NavigateFolderDown()
        {
            HandleFolderListArrowKey(new KeyEventArgs(Keys.Down));
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

        public void NavigateFolders(int delta)
        {
            if (_folderList.Count == 0 || _currentFolderIndex < 0) return;
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
        }

        public void SetDisplayCount(int count)
        {
            if (count <= 0) return;
            int prevDisplayCount = _settings.DisplayCount;
            _settings.DisplayCount = count;
            SettingsManager.Save(_settings);
            _displayManager.UpdateSettings(_settings);

            if (_settings.DisplayCount != prevDisplayCount)
            {
                _displayManager.InitializePictureBoxes();
                UpdateLayout();
                _currentIndex = 0;
                DisplayImages(0);
            }
        }

        // N-page jump (for Ctrl/Alt + arrow keys)
        public void NavigateForward(int pageCount)
        {
            if (_imagePaths.Count == 0 || pageCount <= 0) return;

            _currentIndex += pageCount;

            int maxIndex = _imagePaths.Count - (_imagePaths.Count % _displayManager.DisplayCount == 0 ? _displayManager.DisplayCount : 1);
            if (maxIndex < 0) maxIndex = 0;
            if (_currentIndex > maxIndex) _currentIndex = maxIndex;

            DisplayImages(_currentIndex);
        }

        public void NavigateBackward(int pageCount)
        {
            if (_imagePaths.Count == 0 || pageCount <= 0) return;

            _currentIndex -= pageCount;
            if (_currentIndex < 0) _currentIndex = 0;

            DisplayImages(_currentIndex);
        }

        #region Slideshow

        public void StartSlideshow()
        {
            int interval = 3000; // default 3 seconds

            _slideshowTimer?.Stop();
            _slideshowTimer = new Timer { Interval = interval };
            _slideshowTimer.Tick += SlideshowTimer_Tick;
            _slideshowTimer.Start();
        }

        public void StopSlideshow()
        {
            _slideshowTimer?.Stop();
        }

        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            if (_imagePaths.Count == 0) return;

            int next = _currentIndex + _displayManager.NavigationStep;
            int maxIndex = _imagePaths.Count - (_imagePaths.Count % _displayManager.DisplayCount == 0 ? _displayManager.DisplayCount : 1);
            if (maxIndex < 0) maxIndex = 0;

            if (next > maxIndex)
            {
                next = 0;
                if (_folderList.Count > 0)
                {
                    _currentFolderIndex = 0;
                    LoadAndSortImages(_folderList[0]);
                    _currentIndex = 0;
                    DisplayImages(0);
                    listBoxFolders.SetSelected(0, true);
                }
            }
            else
            {
                _currentIndex = next;
                DisplayImages(_currentIndex);
            }
        }

        #endregion Slideshow

        public void ShowSettingsDialog()
        {
            int prevDisplayCount = _settings.DisplayCount;
            int prevMinDisplayCount = _settings.MinDisplayCount;
            int prevMaxDisplayCount = _settings.MaxDisplayCount;
            int prevMinEvaluation = _settings.MinEvaluation;
            int prevNormalImageAreaPercent = _settings.NormalModeImageAreaPercent;
            int prevFullScreenImageAreaPercent = _settings.FullScreenModeImageAreaPercent;

            using (var dialog = new SettingsDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    (_settings, _) = SettingsManager.LoadWithValidation();
                    _displayManager.UpdateSettings(_settings);

                    bool displayCountChanged = _settings.DisplayCount != prevDisplayCount;
                    bool filterChanged = _settings.MinDisplayCount != prevMinDisplayCount ||
                                         _settings.MaxDisplayCount != prevMaxDisplayCount ||
                                         _settings.MinEvaluation != prevMinEvaluation;
                    bool layoutRatioChanged = _settings.NormalModeImageAreaPercent != prevNormalImageAreaPercent ||
                                              _settings.FullScreenModeImageAreaPercent != prevFullScreenImageAreaPercent;

                    if (filterChanged)
                    {
                        string rootPath = string.IsNullOrEmpty(_settings.LastRootFolder)
                            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                            : _settings.LastRootFolder;
                        BuildSubfolderList(rootPath);

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

                    if (displayCountChanged || layoutRatioChanged)
                    {
                        _displayManager.InitializePictureBoxes();
                        UpdateLayout();
                        _currentIndex = 0;
                        DisplayImages(0);
                    }
                }
            }
        }

        #endregion INavigationActions implementation
    }
}