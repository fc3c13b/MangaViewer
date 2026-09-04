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

                // Load and display images for the first folder if CJ was created successfully.
                if (IsRankDisplayMode && _folderList.Count > 0 && _currentFolderIndex >= 0)
                {
                    LoadAndSortImages(_folderList[_currentFolderIndex]);
                    _currentIndex = 0;
                    _displayManager.ImagePaths = _imagePaths;
                    _displayManager.DisplayImages(0);
                }

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

        // CBZ List overlay dialog (TASK09.31)
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

        public void ShowCbzSelectDialog()
        {
            if (panelCbzListOverlay == null)
            {
                panelCbzListOverlay = new Panel
                {
                    BackColor = Color.FromArgb(128, 0, 0, 0),
                    Dock = DockStyle.Fill
                };

                listBoxCbzFiles = new ListBox
                {
                    BackColor = Color.FromArgb(40, 40, 40),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.None,
                    Font = new Font("Meiryo UI", 9F),
                    SelectionMode = SelectionMode.One,
                    HorizontalScrollbar = true,
                    TabStop = false,
                    DrawMode = DrawMode.OwnerDrawFixed
                };

                listBoxCbzFiles.DrawItem += ListBoxCbzFiles_DrawItem;
                listBoxCbzFiles.SelectedIndexChanged += ListBoxCbzFiles_SelectedIndexChanged;

                panelCbzListOverlay.Controls.Add(listBoxCbzFiles);
                this.Controls.Add(panelCbzListOverlay);
            }

            if (_cbzManager != null)
            {
                listBoxCbzFiles.Items.Clear();
                listBoxCbzFiles.Items.AddRange(_cbzManager.CbxFiles.ToArray());
            }

            panelCbzListOverlay.Visible = true;
        }

        private void ListBoxCbzFiles_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();
            e.DrawFocusRectangle();

            string fileName = Path.GetFileName(listBoxCbzFiles.Items[e.Index].ToString());
            using (Brush brush = new SolidBrush(e.ForeColor))
            {
                e.Graphics.DrawString(fileName, e.Font, brush, e.Bounds);
            }
        }

        private void ListBoxCbzFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxCbzFiles.SelectedIndex >= 0)
            {
                string selectedFile = listBoxCbzFiles.SelectedItem.ToString();
                _cbzManager?.LoadCbx(selectedFile);
                HideCbzSelectDialog();
            }
        }

        public void HideCbzSelectDialog()
        {
            if (panelCbzListOverlay != null)
            {
                panelCbzListOverlay.Visible = false;
            }
        }

        public bool IsCbzListVisible => panelCbzListOverlay?.Visible ?? false;

        #endregion

        // Implementing missing methods from INavigationActions
        public void ShowSettingsDialog()
        {
            FormNavigator.ApplySettingsChanges(this);
        }

        public void NavigateForward(int pageCount)
        {
            FormNavigator.NavigateForward(this, pageCount);
        }

        public void NavigateBackward(int pageCount)
        {
            FormNavigator.NavigateBackward(this, pageCount);
        }

        public void StartSlideshow()
        {
            // Implement the logic to start slideshow
            MessageBox.Show("Slideshow start is not implemented yet.");
        }

        public void StopSlideshow()
        {
            // Implement the logic to stop slideshow
            MessageBox.Show("Slideshow stop is not implemented yet.");
        }

        public void NavigateCbzNext()
        {
            // Implement the logic to navigate to the next CBZ
            MessageBox.Show("Navigate to next CBZ is not implemented yet.");
        }

        public void NavigateCbzPrev()
        {
            // Implement the logic to navigate to the previous CBZ
            MessageBox.Show("Navigate to previous CBZ is not implemented yet.");
        }

        public void CopyCurrentNameToClipboard()
        {
            // Implement the logic to copy the current name to clipboard
            MessageBox.Show("Copy current name to clipboard is not implemented yet.");
        }

        private void CbzListOnOk()
        {
            if (listBoxCbzFiles != null && listBoxCbzFiles.SelectedItem != null)
            {
                string selectedFile = listBoxCbzFiles.SelectedItem.ToString();
                _cbzManager?.LoadCbx(selectedFile);
                HideCbzSelectDialog();
            }
        }

        private void ResetScrollAnimation()
        {
            _listBoxScrollHelper?.ResetScrollAnimation();
        }
    }
}
