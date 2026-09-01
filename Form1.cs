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
        // 「起動時に1回だけ」DB(JSON)を読み込み、以降は再読込しない（TASK09.29）。
        internal CjRoot? _activeCjData;
        internal string? _activeCjParentFolder;
        internal bool _dbLoadedOnce = false;  // 起動時のDB読み込みが完了したか（TASK09.29）

        // Startup background task (for graceful shutdown)
        internal Task? _initTask;

        // INavigationActions implementation (exact signature required)
        public void LoadCjForParent(string parentFolder)
        {
            LoadCjForParentInternal(parentFolder, autoCreate: true);
        }

        private void LoadCjForParentInternal(string parentFolder, bool autoCreate)
        {
            try
            {
                StartupHandler.Log($"[Form1] LoadCjForParent called: {parentFolder}, autoCreate={autoCreate}");

                if (string.IsNullOrEmpty(parentFolder))
                {
                    StartupHandler.Log("[Form1] LoadCjForParent: empty parentFolder, returning.");
                    return;
                }

                if (_activeCjParentFolder == parentFolder && _activeCjData != null)
                {
                    StartupHandler.Log($"[Form1] LoadCjForParent: already loaded for {parentFolder}, skipping.");
                    return;
                }

                var result = CjService.FindAndLoadCj(parentFolder);

                if (result.HasValue)
                {
                    _activeCjParentFolder = parentFolder;
                    _activeCjData = result.Value.data;
                    _activeDbFile = result.Value.cjFile;

                    StartupHandler.Log(
                        $"[Form1] LoadCjForParent OK: cjFile={_activeDbFile}, " +
                        $"FoldersMapCount={_activeCjData.Folders.Count}");

                    if (IsRankDisplayMode)
                        BuildRankFilteredListFromActiveCj();
                }
                else if (autoCreate)
                {
                    StartupHandler.Log($"[Form1] LoadCjForParent: no CJ found for {parentFolder}, creating new.");
                    CreateCjForParent(parentFolder);
                }
                else
                {
                    // autoCreate=false → DB not found, do nothing so caller can handle.
                    StartupHandler.Log($"[Form1] LoadCjForParent: no CJ found for {parentFolder}, autoCreate=false.");
                }
            }
            catch (Exception ex)
            {
                StartupHandler.LogError($"[Form1] LoadCjForParent error: {ex}");
            }
        }

        public void CreateCjForParent(string parentFolder)
        {
            if (string.IsNullOrWhiteSpace(parentFolder)) return;
            if (!Directory.Exists(parentFolder)) return;

            try
            {
                Application.DoEvents();
                SafeInvokeUI(() => labelInfo.Text = "CJ作成中…");

                var (cjPath, cjData) = CjService.CreateForParent(parentFolder);

                _activeCjParentFolder = parentFolder;
                _activeCjData = cjData;
                _activeDbFile = cjPath;

                if (IsRankDisplayMode)
                    BuildRankFilteredListFromActiveCj();
                UpdateWindowTitle();
                labelInfo.Text = $"CJ作成完了: {parentFolder}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "CJ作成エラー:\n" + ex.Message, "CJ エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                labelInfo.Text = "CJ作成に失敗しました。";
            }
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
        private System.Windows.Forms.Timer? _slideshowTimer;
        private System.Windows.Forms.Timer? _folderDebounceTimer;

        // ListBox scroll/draw helper (TASK09.25)
        private ListBoxScrollHelper? _listBoxScrollHelper;

        // CBZ List overlay dialog (TASK09.31)
        private Panel? panelCbzListOverlay;
        private ListBox? listBoxCbzFiles;
        private Button? btnCbzOk;
        private Button? btnCbzCancel;

        public bool IsSlideshowRunning => _slideshowTimer?.Enabled ?? false;

        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                // L キー: CBZ/ZIP ファイル選択ダイアログの表示/非表示（TASK09.31）
                if (!e.Control && !e.Alt && e.KeyCode == Keys.L)
                {
                    e.Handled = true;
                    this.ToggleCbzListDialog();
                    return;
                }

                // ダイアログ表示中の Esc: ダイアログを閉じる（フルスクリーン切替を行わない）
                if (e.KeyCode == Keys.Escape && IsCbzListVisible)
                {
                    e.Handled = true;
                    this.HideCbzSelectDialog();
                    return;
                }

                // CBZ List 表示中の Enter: 選択確定（TASK09.31 バグ修正）
                if (IsCbzListVisible && e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    CbzListOnOk();
                    return;
                }

                // 上下キー：CBZ List 非表示時のみフォルダリスト操作（TASK09.31）
                if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && !IsCbzListVisible)
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
            _folderDebounceTimer.Tick += (s, ev) =>
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

            // 上下キーもここで補足し、Form の KeyDown と同じ処理を行う（CBZ List 非表示時のみ）
            if (!IsCbzListVisible && (keyData == Keys.Up || keyData == Keys.Down))
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

            string titleBase;
            if (!string.IsNullOrEmpty(_activeDbFile))
            {
                string dbName = Path.GetFileName(_activeDbFile);
                titleBase = $"{baseTitle} - DBList [{dbName}]";
            }
            else
            {
                // Fallback when no CJ selected.
                string label = string.IsNullOrWhiteSpace(_rootFolder)
                    ? "[<none>]"
                    : $"[{_rootFolder}]";
                titleBase = $"{baseTitle} - DBList {label}";
            }

            // TASK09.33: Append CBZ cache list sorted by extraction time.
            string cbzList = BuildCbzCacheList();
            this.Text = string.IsNullOrEmpty(cbzList) ? titleBase : $"{titleBase} {cbzList}";
        }

        private string BuildCbzCacheList()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentFolder))
                    return "";

                string[] cbzFiles = Directory.GetFiles(_currentFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (cbzFiles.Length == 0)
                    return "";

                var cacheRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MangaViewer",
                    "CBZCache"
                );

                var list = new List<(string name, DateTime time)>();

                foreach (var cbz in cbzFiles)
                {
                    try
                    {
                        using var md5 = System.Security.Cryptography.MD5.Create();
                        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cbz));
                        var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        string cacheDir = Path.Combine(cacheRoot, hashStr);

                        if (Directory.Exists(cacheDir) &&
                            File.Exists(Path.Combine(cacheDir, CbzManager.CacheCompleteMarkerFileName)))
                        {
                            DateTime dt = Directory.GetCreationTime(cacheDir);
                            list.Add((Path.GetFileName(cbz), dt));
                        }
                    }
                    catch { }
                }

                if (list.Count == 0)
                    return "";

                list.Sort((a, b) => a.time.CompareTo(b.time));

                var sb = new System.Text.StringBuilder();
                foreach (var item in list)
                    sb.Append(" /").Append(ShortenCbzName(item.name));

                return sb.ToString().TrimStart(' ');
            }
            catch
            {
                return "";
            }
        }

        private static string ShortenCbzName(string fileName)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            // 先頭の[...]を除去
            string stripped = System.Text.RegularExpressions.Regex.Replace(nameWithoutExt, @"^\[.*?\]\s*", "");
            if (stripped.Length <= 14)
                return stripped;

            return stripped.Substring(0, 10) + "..." + stripped.Substring(stripped.Length - 4);
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
            // Stop timers to prevent further UI operations.
            _folderDebounceTimer?.Stop();
            _slideshowTimer?.Stop();

            // Wait for startup background task with a simple timeout so Windows doesn't hang.
            var initTask = _initTask;
            if (initTask != null && !initTask.IsCompleted)
            {
                try { initTask.Wait(5000); } catch { /* ignore */ }
            }

            // Clean up resources.
            try { _imageService?.Dispose(); } catch { }
            try { _displayManager?.Dispose(); } catch { }

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

        #region CBZ List Overlay (TASK09.31)

        internal bool IsCbzListVisible => panelCbzListOverlay?.Visible ?? false;

        private void EnsureCbzListControls()
        {
            if (panelCbzListOverlay != null) return; // already created

            int cw = this.ClientSize.Width;
            int ch = this.ClientSize.Height;

            // Overlay panel
            panelCbzListOverlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(200, 15, 15, 15),
                Visible = false
            };
            this.Controls.Add(panelCbzListOverlay);

            // Content panel
            int boxW = Math.Max(360, (int)(cw * 0.4));
            int boxH = Math.Max(280, (int)(ch * 0.5));
            int x = (cw - boxW) / 2;
            int y = (ch - boxH) / 2;

            var contentPanel = new Panel
            {
                BackColor = Color.FromArgb(35, 35, 35),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(x, y),
                Size = new Size(boxW, boxH)
            };

            // Title label
            var lblTitle = new Label
            {
                Text = "CBZ / ZIP ファイルを選択",
                ForeColor = Color.White,
                Font = new Font("Meiryo UI", 10F),
                AutoSize = true,
                Location = new Point(10, 8)
            };
            contentPanel.Controls.Add(lblTitle);

            // ListBox
            listBoxCbzFiles = new ListBox
            {
                Location = new Point(10, 34),
                Size = new Size(boxW - 20, boxH - 95),
                SelectionMode = SelectionMode.One,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White
            };
            contentPanel.Controls.Add(listBoxCbzFiles);

            // OK button
            btnCbzOk = new Button
            {
                Text = "OK",
                Location = new Point(boxW - 175, boxH - 36),
                Size = new Size(70, 24)
            };
            contentPanel.Controls.Add(btnCbzOk);

            // Cancel button
            btnCbzCancel = new Button
            {
                Text = "キャンセル",
                Location = new Point(boxW - 95, boxH - 36),
                Size = new Size(70, 24)
            };
            contentPanel.Controls.Add(btnCbzCancel);

            // Wire events
            btnCbzOk.Click += (s, e) => CbzListOnOk();
            btnCbzCancel.Click += (s, e) => HideCbzSelectDialog();

            // Double click on ListBox triggers OK
            listBoxCbzFiles.DoubleClick += (s, ev) => CbzListOnOk();

            // Mark Enter as input key so ListBox raises it in KeyDown.
            listBoxCbzFiles.PreviewKeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter)
                    ev.IsInputKey = true;
            };

            // Enter key on ListBox triggers OK
            listBoxCbzFiles.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter)
                {
                    ev.Handled = true;
                    CbzListOnOk();
                }
            };

            panelCbzListOverlay.Controls.Add(contentPanel);
        }

        public void ToggleCbzListDialog()
        {
            if (IsCbzListVisible)
            {
                HideCbzSelectDialog();
            }
            else
            {
                ShowCbzSelectDialog();
            }
        }

        public void CopyCurrentNameToClipboard()
        {
            string name;
            if (_cbzManager != null && _cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(_cbzManager.ActiveCbxIndex, 0, _cbzManager.CbxFiles.Count - 1);
                name = Path.GetFileNameWithoutExtension(_cbzManager.CbxFiles[idx]);
            }
            else
            {
                name = Path.GetFileName(_currentFolder) ?? "";
            }
            if (!string.IsNullOrEmpty(name))
                Clipboard.SetText(name);
        }

        private void ShowCbzSelectDialog()
        {
            EnsureCbzListControls();
            if (panelCbzListOverlay == null || listBoxCbzFiles == null) return;

            // Clear previous items
            listBoxCbzFiles.Items.Clear();

            if (string.IsNullOrEmpty(_currentFolder))
            {
                HideCbzSelectDialog();
                return;
            }

            var cbzFiles = GetCbzZipListInCurrentFolder();
            foreach (var f in cbzFiles)
            {
                listBoxCbzFiles.Items.Add(f);
            }

            if (listBoxCbzFiles.Items.Count == 0)
            {
                HideCbzSelectDialog();
                return;
            }

            // Select active item if possible
            int activeIdx = _cbzManager?.ActiveCbxIndex ?? 0;
            if (activeIdx >= 0 && activeIdx < listBoxCbzFiles.Items.Count)
                listBoxCbzFiles.SelectedIndex = activeIdx;
            else
                listBoxCbzFiles.SelectedIndex = 0;

            panelCbzListOverlay.Visible = true;
            panelCbzListOverlay.BringToFront();
            listBoxCbzFiles.Focus();
        }

        private void HideCbzSelectDialog()
        {
            if (panelCbzListOverlay != null)
                panelCbzListOverlay.Visible = false;
        }

        private void CbzListOnOk()
        {
            if (listBoxCbzFiles == null || listBoxCbzFiles.SelectedIndex < 0)
            {
                HideCbzSelectDialog();
                return;
            }

            var selectedItem = listBoxCbzFiles.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedItem))
            {
                HideCbzSelectDialog();
                return;
            }

            // Build full path of selected file
            string cbzPath = Path.Combine(_currentFolder, selectedItem);
            if (!File.Exists(cbzPath))
            {
                HideCbzSelectDialog();
                return;
            }

            // Open this CBZ/ZIP via CbzManager and reload images
            try
            {
                if (_cbzManager == null)
                {
                    _cbzManager = new CbzManager();
                    _cbzManager.CacheChanged += () => BeginInvoke((Action)UpdateWindowTitle);
                }

                // Initialize for folder to set up multi-CBZ list, then switch to selected.
                bool ok = _cbzManager.InitializeForFolder(_currentFolder, forceReset: false);
                if (!ok || !_cbzManager.CbxFiles.Any())
                {
                    HideCbzSelectDialog();
                    return;
                }

                // Find index of the chosen file in CbxFiles list
                int idx = _cbzManager.CbxFiles.FindIndex(p => string.Equals(
                    Path.GetFullPath(p), Path.GetFullPath(cbzPath), StringComparison.OrdinalIgnoreCase));

                if (idx >= 0)
                {
                    _cbzManager.SwitchToCbx(idx);
                }

                // Reload images for current folder (mix JPG + CBZ).
                LoadAndSortImages(_currentFolder);
                _currentIndex = 0;
                DisplayImages(0);
            }
            catch
            {
                // On error, just close dialog.
            }

            HideCbzSelectDialog();
        }

        private List<string> GetCbzZipListInCurrentFolder()
        {
            if (string.IsNullOrEmpty(_currentFolder)) return new List<string>();

            if (_cbzManager != null && _cbzManager.CbxFiles.Any() &&
                string.Equals(_cbzManager.CurrentFolder, _currentFolder, StringComparison.OrdinalIgnoreCase))
            {
                return _cbzManager.CbxFiles.Select(Path.GetFileName).ToList()!;
            }

            try
            {
                return Directory.GetFiles(_currentFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .ToList()!;
            }
            catch
            {
                return new List<string>();
            }
        }

        #endregion CBZ List Overlay (TASK09.31)

        #endregion INavigationActions implementation
    }
}