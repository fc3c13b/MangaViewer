using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        internal CbzManager? _cbzManager;
        internal int _currentIndex = 0;
        internal string _currentFolder = "";
        internal List<string> _folderList = new List<string>();
        internal int _currentFolderIndex = -1;
        internal bool _fullScreenMode = false;

        // DBList モード固定（モード切替廃止）
        public bool IsRankDisplayMode => true;

        private string _rootFolder = "";
        internal string? _activeDbFile = null;

        // オンメモリ CJ 保持 (FormNavigator からアクセス)
        internal CjRoot? _activeCjData;
        internal string? _activeCjParentFolder;

        public void LoadCjForParent(string parentFolder)
        {
            if (string.IsNullOrEmpty(parentFolder)) return;
            if (_activeCjParentFolder == parentFolder && _activeCjData != null)
                return;

            var result = CjService.FindAndLoadCj(parentFolder);
            if (result.HasValue)
            {
                _activeCjParentFolder = parentFolder;
                _activeCjData = result.Value.data;
                _activeDbFile = result.Value.cjFile;
                if (IsRankDisplayMode)
                    BuildRankFilteredListFromActiveCj();
            }
            else
            {
                CreateCjForParent(parentFolder);
            }
        }

        public void CreateCjForParent(string parentFolder)
        {
            if (string.IsNullOrWhiteSpace(parentFolder)) return;
            if (!Directory.Exists(parentFolder)) return;

            Application.DoEvents();
            SafeInvokeUI(() => labelInfo.Text = "CJ作成中…");

            Task.Run(() =>
            {
                try
                {
                    var (cjPath, cjData) = CjService.CreateForParent(parentFolder);

                    _activeCjParentFolder = parentFolder;
                    _activeCjData = cjData;
                    _activeDbFile = cjPath;

                    SafeInvokeUI(() =>
                    {
                        if (IsRankDisplayMode)
                            BuildRankFilteredListFromActiveCj();
                        UpdateWindowTitle();
                        labelInfo.Text = $"CJ作成完了: {parentFolder}";
                    });
                }
                catch (Exception ex)
                {
                    SafeInvokeUI(() =>
                    {
                        MessageBox.Show(this, "CJ作成エラー:\n" + ex.Message, "CJ エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        labelInfo.Text = "CJ作成に失敗しました。";
                    });
                }
            });
        }

        private void SafeInvokeUI(Action action)
        {
            if (this.InvokeRequired)
                this.BeginInvoke(action);
            else
                action();
        }

        /// <summary>
        /// DBList モード用：アクティブ CJ から評価値フィルタリストを構築（FormNavigator 委譲）。
        /// </summary>
        internal void BuildRankFilteredListFromActiveCj()
        {
            FormNavigator.BuildRankFiltered(this);
        }

        private FormWindowState _savedWindowState = FormWindowState.Normal;
        private FormBorderStyle _savedFormBorderStyle = FormBorderStyle.Sizable;
        private Size _savedSize = new Size(Constants.InitialWidth, Constants.InitialHeight);

        internal Settings _settings = new Settings();
        internal readonly ImageService _imageService = new ImageService();
        internal FolderService? _folderService;
        private Timer? _slideshowTimer;
        private System.Windows.Forms.Timer? _folderDebounceTimer;

        // ListBox scroll/draw helper (TASK09.25)
        private ListBoxScrollHelper? _listBoxScrollHelper;

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

            // デバウンスタイマーをリセット（1秒間キー操作がない場合に画像を表示）
            _folderDebounceTimer?.Stop();
            _folderDebounceTimer ??= new System.Windows.Forms.Timer { Interval = 1000 };
            _folderDebounceTimer.Tick += (s, e) =>
            {
                _folderDebounceTimer!.Stop();
                LoadAndSortImages(_folderList[_currentFolderIndex]);
                _currentIndex = 0;
                DisplayImages(0);
            };
            _folderDebounceTimer.Start();
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
            StartupHandler.Initialize(this);
        }

        internal void EnsureBasicLayout()
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
            _rootFolder = rootPath;
            FolderListBuilder.Build(this, rootPath);
        }

        internal void LoadAndSortImages(string folderPath)
        {
            _currentFolder = folderPath;
            ImageLoader.Load(this);
        }

        internal void UpdateWindowTitle()
        {
            // TASK09.24: Always DBList mode (no more FolderList mode).
            string baseTitle = Constants.AppTitle;

            if (!string.IsNullOrEmpty(_activeDbFile))
            {
                string dbName = Path.GetFileName(_activeDbFile);
                this.Text = $"{baseTitle} - DBList [{dbName}]";
            }
            else
            {
                // Fallback when no CJ selected.
                string label = string.IsNullOrWhiteSpace(_rootFolder)
                    ? "[<none>]"
                    : $"[{_rootFolder}]";
                this.Text = $"{baseTitle} - DBList {label}";
            }
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
                SelectionMode = SelectionMode.One, HorizontalScrollbar = true, TabStop = false,
                DrawMode = DrawMode.OwnerDrawFixed
            };

            listBoxFolders.KeyDown += (s, e) => KeyboardInputHandler.HandleKeyDown(e, this);

            // TASK09.25: ListBoxScrollHelper handles DrawItem + scroll animation
            _listBoxScrollHelper = new ListBoxScrollHelper(listBoxFolders, panelList);

            listBoxFolders.SelectedIndexChanged += (s, e) =>
            {
                if (listBoxFolders.SelectedIndex >= 0 && _folderList.Count > 0)
                {
                    _currentFolderIndex = listBoxFolders.SelectedIndex;
                    // Start scroll animation for newly selected row
                    ResetScrollAnimation();
                }
            };

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

            string folderDisplay = BuildFolderDisplayWithCbz();
            string infoText = _displayManager.GetInfoText(_currentFolder, _currentFolderIndex, _folderService!.entries);
            if (!string.IsNullOrEmpty(infoText))
                labelInfo.Text = $"[{folderDisplay}] {infoText.TrimStart()}";
            else
                labelInfo.Text = $"[{folderDisplay}] 表示可能な画像がありません。";

            // CBZ: preload next when near end.
            if (_cbzManager != null && _imagePaths.Count > 0)
            {
                int remainingPages = _imagePaths.Count - startIndex;
                if (remainingPages <= 16)
                {
                    _ = Task.Run(() => _cbzManager.PreloadNextCbx());
                }
            }
        }

        internal void UpdateLayout()
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

            UpdateInfoLabelAfterToggle();
            UpdateLayout();
        }

        // Shared label update for DisplayImages/ToggleFullScreen.
        private void UpdateInfoLabelAfterToggle()
        {
            string folderDisplay = BuildFolderDisplayWithCbz();
            string infoText = _displayManager.GetInfoText(_currentFolder, _currentFolderIndex, _folderService!.entries);
            if (!string.IsNullOrEmpty(infoText))
                labelInfo.Text = $"[{folderDisplay}] {infoText.TrimStart()}";
            else
                labelInfo.Text = $"[{folderDisplay}] 表示可能な画像がありません。";
        }

        // Build folder+CBZ display string.
        private string BuildFolderDisplayWithCbz()
        {
            string folderName = Path.GetFileName(_currentFolder);
            string cbzInfo = "";
            if (_cbzManager != null && _cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(_cbzManager.ActiveCbxIndex, 0, _cbzManager.CbxFiles.Count - 1);
                string name = Path.GetFileNameWithoutExtension(_cbzManager.CbxFiles[idx]);
                if (FolderService.ExtractNumberFromFileName(name) > 0)
                    cbzInfo = $" [{name}]";
            }
            return $"{folderName}{cbzInfo}";
        }

        #region INavigationActions implementation (thin wrappers → FormNavigator)

        public int ImageCount => _imagePaths.Count;
        public int DisplayCount => _settings.DisplayCount;
        public int FolderListCount => _folderList.Count;
        public string CurrentFolder => _currentFolder;

        // Root folder dialog + ChangeRootFolder delegated.
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
                    FormNavigator.ChangeRootFolder(this, dialog.SelectedPath);
                }
            }
        }

        public void ChangeRootFolder(string rootPath)
        {
            FormNavigator.ChangeRootFolder(this, rootPath);
        }

        // Navigation: delegate to FormNavigator.
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
            FormNavigator.NavigateForward(this, 2);
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
            FormNavigator.NavigateFolderBy(this, delta);
        }

        public void NavigateFolders(int delta)
        {
            FormNavigator.NavigateFolderBy(this, delta);
        }

        public void NavigateToNextUnrated()
        {
            FormNavigator.NavigateToNextUnrated(this);
        }

        public void SetDisplayCount(int count)
        {
            FormNavigator.SetDisplayCount(this, count);
        }

        // N-page jump for Ctrl/Alt+arrows.
        public void NavigateForward(int pageCount)
        {
            FormNavigator.NavigateForward(this, pageCount);
        }

        public void NavigateBackward(int pageCount)
        {
            FormNavigator.NavigateBackward(this, pageCount);
        }

        public void NavigateCbzNext()
        {
            FormNavigator.NavigateCbzNext(this);
        }

        public void NavigateCbzPrev()
        {
            FormNavigator.NavigateCbzPrev(this);
        }

        // Slideshow.
        public void StartSlideshow()
        {
            if (_slideshowTimer == null)
                _slideshowTimer = new Timer { Interval = 3000 };

            _slideshowTimer.Tick += (s, e) => NavigateForwardTwoPages();
            _slideshowTimer.Start();
        }

        public void StopSlideshow()
        {
            _slideshowTimer?.Stop();
        }

        // Mode toggle removed; DBList-only.
        public void ToggleDisplayMode() { /* no-op */ }

        // Rating in-memory update.
        private void UpdateRatingInMemory(string folderPath, int newRating)
        {
            if (_activeCjData == null || string.IsNullOrEmpty(_activeCjParentFolder)) return;
            if (!_activeCjData.Folders.TryGetValue(folderPath, out var entry)) return;

            // オンメモリ更新
            entry.Rating = newRating;

            // ディスク保存
            CjManager.SaveCj(_activeCjParentFolder, _activeCjData);
        }

        // Settings dialog delegated.
        public void ShowSettingsDialog()
        {
            FormNavigator.ApplySettingsChanges(this);
        }

        // Scroll animation reset.
        private void ResetScrollAnimation()
        {
            _listBoxScrollHelper?.OnSelectedIndexChanged();
        }

        #endregion INavigationActions implementation
    }
}
