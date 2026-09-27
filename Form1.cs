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
        internal Panel panelList = null!;
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
                StartupHandler.WriteStartupLog($"[Form1] LoadCjForParent called: {parentFolder}, autoCreate={autoCreate}");

                if (string.IsNullOrEmpty(parentFolder))
                {
                    StartupHandler.WriteStartupLog("[Form1] LoadCjForParent: empty parentFolder, returning.");
                    return;
                }

                if (_activeCjParentFolder == parentFolder && _activeCjData != null)
                {
                    StartupHandler.WriteStartupLog($"[Form1] LoadCjForParent: already loaded for {parentFolder}, skipping.");
                    return;
                }

                var result = CjService.FindAndLoadCj(parentFolder);

                if (result.HasValue)
                {
                    _activeCjParentFolder = parentFolder;
                    _activeCjData = result.Value.data;
                    _activeDbFile = result.Value.cjFile;

                    StartupHandler.WriteStartupLog(
                        $"[Form1] LoadCjForParent OK: cjFile={_activeDbFile}, " +
                        $"FoldersMapCount={_activeCjData.Folders.Count}");

                    if (IsRankDisplayMode)
                        BuildRankFilteredListFromActiveCj();

                    // Load and display images if folders exist and not during startup background init
                    if (IsRankDisplayMode && _folderList.Count > 0 && _currentFolderIndex >= 0 && _initTask?.IsCompleted != false)
                    {
                        LoadAndSortImages(_folderList[_currentFolderIndex]);
                        _displayManager.ImagePaths = _imagePaths;
                        _displayManager.DisplayImages(_currentIndex);
                    }

                    _settings.LastRootFolder = parentFolder;
                    SettingsManager.Save(_settings);
                    ScheduleWindowTitleUpdate();
                }
                else if (autoCreate)
                {
                    StartupHandler.WriteStartupLog($"[Form1] LoadCjForParent: no CJ found for {parentFolder}, creating new.");
                    CreateCjForParent(parentFolder);
                }
                else
                {
                    // autoCreate=false → DB not found, do nothing so caller can handle.
                    StartupHandler.WriteStartupLog($"[Form1] LoadCjForParent: no CJ found for {parentFolder}, autoCreate=false.");
                }
            }
            catch (Exception ex)
            {
                StartupHandler.WriteErrorLog($"[Form1] LoadCjForParent error: {ex}");
            }
        }

        public void CreateCjForParent(string parentFolder)
        {
            if (string.IsNullOrWhiteSpace(parentFolder)) return;
            if (!Directory.Exists(parentFolder)) return;

            try
            {
                Application.DoEvents();
                SafeInvokeUI(() => UpdateInfoLabelBase("CJ作成中…", "create-cj:start"));

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
                    _displayManager.ImagePaths = _imagePaths;
                    _displayManager.DisplayImages(_currentIndex);
                }

                _settings.LastRootFolder = parentFolder;
                SettingsManager.Save(_settings);

                ScheduleWindowTitleUpdate();
                UpdateInfoLabelBase($"CJ作成完了: {parentFolder}", "create-cj:done");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "CJ作成エラー:\n" + ex.Message, "CJ エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateInfoLabelBase("CJ作成に失敗しました。", "create-cj:error");
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
        internal System.Windows.Forms.Timer? _slideshowTimer = null;
        private System.Windows.Forms.Timer? _folderDebounceTimer;
        private System.Windows.Forms.Timer? _titleUpdateTimer;
        internal System.Windows.Forms.Timer? _cacheStatusTimer = null;
        private int _folderLoadGeneration;
        private int _cbzLoadGeneration;
        // Serializes volume transitions so concurrent key presses cannot corrupt ActiveCbxIndex.
        private readonly System.Threading.SemaphoreSlim _volumeTransitionGate = new(1, 1);
        private long _arrowNavSequence;
        private long _pendingArrowNavSequence;
        private long _pendingArrowNavScheduledAtMs;
        internal string _baseInfoLabelText = "フォルダを選択してください (キー1)";
        internal string? _cacheStatusLabelText;
        internal DateTime _cacheStatusExpiresAtUtc = DateTime.MinValue;
        internal string? _lastRenderedInfoLabelText;

        // ListBox scroll/draw helper (TASK09.25)
        private ListBoxScrollHelper? _listBoxScrollHelper;

        // CBZ List overlay dialog (TASK09.31)
        private Panel? panelCbzListOverlay;
        private ListBox? listBoxCbzFiles;

        public bool IsSlideshowRunning => _slideshowTimer?.Enabled ?? false;
        public bool IsLeadingBlankPageEnabled { get; private set; }

        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                // L キー: CBZ/ZIP ファイル選択ダイアログの表示/非表示（TASK09.31）
                // CBZリストがフォーカスされていても、ここで優先的に処理して閉じる。
                if (!e.Control && !e.Alt && e.KeyCode == Keys.L)
                {
                    e.Handled = true;
                    if (IsCbzListVisible)
                        HideCbzSelectDialog();
                    else
                        ShowCbzSelectDialog();
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
                // Alt/Ctrl 修飾付きの上下キーは、ナビゲーションの一元処理に委譲する。
                // ここで拾うと、同じキーが複数入口で処理される重複発火の原因になる。
                if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && !IsCbzListVisible && !e.Alt && !e.Control)
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

            // 上下キーのリスト移動を最優先にする。
            // 進行中のフォルダ読込結果は世代不一致で破棄し、入力応答を優先する。
            int canceledGeneration = System.Threading.Interlocked.Increment(ref _folderLoadGeneration);
            long seq = System.Threading.Interlocked.Increment(ref _arrowNavSequence);

            int current = listBoxFolders.SelectedIndex;
            if (current < 0)
                current = _currentFolderIndex >= 0 ? _currentFolderIndex : 0;

            int next = e.KeyCode switch
            {
                Keys.Up   => Math.Max(0, current - 1),
                Keys.Down => Math.Min(_folderList.Count - 1, current + 1),
                _         => current
            };

            StartupHandler.WriteStartupLog($"[KEY-NAV] phase=key seq={seq} key={e.KeyCode} from={current} to={next} cancelGen={canceledGeneration}");

            if (next == current)
            {
                StartupHandler.WriteStartupLog($"[KEY-NAV] phase=boundary seq={seq} index={current}");
                return;
            }

            _currentFolderIndex = next;
            listBoxFolders.SelectedIndex = next;

            if (string.IsNullOrEmpty(_folderList[next])) return;

            // デバウンスタイマーをリセット（1秒間キー操作がない場合に画像を表示）
            _folderDebounceTimer?.Stop();
            if (_folderDebounceTimer == null)
            {
                _folderDebounceTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _folderDebounceTimer.Tick += OnFolderDebounceTimerTick;
            }
            _folderDebounceTimer.Start();
            _pendingArrowNavSequence = seq;
            _pendingArrowNavScheduledAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            StartupHandler.WriteStartupLog($"[KEY-NAV] phase=debounce-start seq={seq} waitMs=1000 targetIndex={next}");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private async void OnFolderDebounceTimerTick(object? sender, EventArgs e)
        {
            if (_folderDebounceTimer == null) return;
            _folderDebounceTimer.Stop();

            long seq = System.Threading.Volatile.Read(ref _pendingArrowNavSequence);
            long scheduledAt = System.Threading.Volatile.Read(ref _pendingArrowNavScheduledAtMs);
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long waitedMs = scheduledAt > 0 ? Math.Max(0, nowMs - scheduledAt) : -1;
            StartupHandler.WriteStartupLog($"[KEY-NAV] phase=debounce-fire seq={seq} waitedMs={waitedMs} currentIndex={_currentFolderIndex}");

            if (_currentFolderIndex < 0 || _currentFolderIndex >= _folderList.Count)
                return;

            string folderPath = _folderList[_currentFolderIndex];
            StartupHandler.WriteStartupLog($"[KEY-NAV] phase=load-request seq={seq} folder={folderPath}");

            try
            {
                if (!await LoadAndSortImagesAsync(folderPath))
                {
                    StartupHandler.WriteStartupLog($"[KEY-NAV] phase=load-discarded seq={seq} reason=generation-mismatch folder={folderPath}");
                    return;
                }
            }
            catch (Exception ex)
            {
                StartupHandler.WriteStartupLog($"[KEY-NAV] phase=load-error seq={seq} error={ex.GetType().Name}");
                StartupHandler.WriteErrorLog($"[Form1] folder load failed: {ex}");
                UpdateInfoLabelBase("画像読み込みに失敗しました。", "folder-load-error");
                return;
            }

            StartupHandler.WriteStartupLog($"[KEY-NAV] phase=load-applied seq={seq} index={_currentFolderIndex}");
            DisplayImages(_currentIndex);
            ScheduleNavigationCbzPreload();
            ScheduleWindowTitleUpdate();
        }

        protected override bool ProcessCmdKey(ref Message m, Keys keyData)
        {
            // Ctrl/Alt の矢印は KeyboardInputHandler で一元処理する。
            // ここで重複すると、1 回のキー押下で同じナビゲーションが 2 回実行される。

            // 上下キーはフォームの Folder List への移動のみを担う（CBZ List 非表示時のみ）
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
            UiLayoutService.EnsureBasicLayout(this);
        }


        internal void LoadAndSortImages(string folderPath)
        {
            RememberCurrentPlaybackPosition();
            _currentFolder = folderPath;
            ImageLoader.Load(this);
            ApplyPlaybackStartPosition();
        }

        internal async Task<bool> LoadAndSortImagesAsync(string folderPath)
        {
            int loadGeneration = System.Threading.Interlocked.Increment(ref _folderLoadGeneration);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            StartupHandler.WriteStartupLog($"[KEY-NAV] phase=load-start gen={loadGeneration} folder={folderPath}");

            RememberCurrentPlaybackPosition();
            _currentFolder = folderPath;

            await Task.Run(() => ImageLoader.Load(this)).ConfigureAwait(false);

            sw.Stop();

            int latestGeneration = System.Threading.Volatile.Read(ref _folderLoadGeneration);
            if (loadGeneration != latestGeneration)
            {
                StartupHandler.WriteStartupLog($"[KEY-NAV] phase=load-finish gen={loadGeneration} result=discarded latestGen={latestGeneration} elapsedMs={sw.ElapsedMilliseconds}");
                return false;
            }

            StartupHandler.WriteStartupLog($"[KEY-NAV] phase=load-finish gen={loadGeneration} result=applied elapsedMs={sw.ElapsedMilliseconds}");

            ApplyPlaybackStartPosition();
            return true;
        }

        internal async Task<bool> SwitchToNextCbxAsync()
        {
            return await CbzFormBridge.SwitchToNextAsync(this).ConfigureAwait(false);
        }

        internal async Task<bool> SwitchToPreviousCbxAsync()
        {
            return await CbzFormBridge.SwitchToPreviousAsync(this).ConfigureAwait(false);
        }

        internal void ScheduleNavigationCbzPreload()
        {
            CbzFormBridge.ScheduleNavigationPreload(this);
        }

        internal void ScheduleWindowTitleUpdate()
        {
            if (IsDisposed)
                return;

            if (_titleUpdateTimer == null)
            {
                _titleUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _titleUpdateTimer.Tick += (s, e) =>
                {
                    _titleUpdateTimer?.Stop();
                    if (!IsDisposed)
                        UpdateWindowTitle();
                };
            }

            _titleUpdateTimer.Stop();
            _titleUpdateTimer.Start();
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

            // TASK09.33: Append CBZ cache list (order is not guaranteed).
            string cbzList = BuildCbzCacheList();
            this.Text = string.IsNullOrEmpty(cbzList) ? titleBase : $"{titleBase} {cbzList}";
        }

        private string BuildCbzCacheList()
        {
            try
            {
                if (_folderList == null || _folderList.Count == 0 || _currentFolderIndex < 0 || _currentFolderIndex >= _folderList.Count)
                    return "";

                string currentFolder = _folderList[_currentFolderIndex];
                string? nextFolder = (_currentFolderIndex + 1 < _folderList.Count) ? _folderList[_currentFolderIndex + 1] : null;

                var candidateFolders = new List<string> { currentFolder };
                if (!string.IsNullOrWhiteSpace(nextFolder))
                    candidateFolders.Add(nextFolder);

                var cbzFiles = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in candidateFolders)
                {
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                        continue;

                    foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                                 .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (seen.Add(file))
                            cbzFiles.Add(file);
                    }
                }

                if (cbzFiles.Count == 0)
                    return "";

                string? area1Root = CbzManager.GetCacheSearchRoots().FirstOrDefault();
                if (string.IsNullOrEmpty(area1Root))
                    return "";

                var cachedList = new List<string>();

                foreach (var cbz in cbzFiles)
                {
                    try
                    {
                        using var md5 = System.Security.Cryptography.MD5.Create();
                        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cbz));
                        var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        bool existsInArea1 = Directory.Exists(Path.Combine(area1Root, hashStr)) &&
                                             File.Exists(Path.Combine(area1Root, hashStr, CbzManager.CacheCompleteMarkerFileName));

                        if (existsInArea1)
                            cachedList.Add(cbz);
                    }
                    catch { }
                }

                var sb = new System.Text.StringBuilder();
                foreach (var item in cachedList)
                    sb.Append(" /").Append(ShortenCbzName(Path.GetFileName(item)));

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

            listBoxFolders.KeyDown += (s, e) =>
            {
                if (!IsCbzListVisible && !e.Control && !e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
                {
                    HandleFolderListArrowKey(e);
                    return;
                }

                KeyboardInputHandler.HandleKeyDown(e, this);
            };

            // TASK09.25: ListBoxScrollHelper handles DrawItem + scroll animation
            _listBoxScrollHelper = new ListBoxScrollHelper(listBoxFolders, panelList);

            listBoxFolders.SelectedIndexChanged += (s, e) =>
            {
                if (listBoxFolders.SelectedIndex >= 0 && _folderList.Count > 0)
                {
                    _currentFolderIndex = listBoxFolders.SelectedIndex;
                    // Start scroll animation for newly selected row
                    ResetScrollAnimation();
                    // 上下キー移動中はフォーカス移動のみ行う。
                    // 先読み/タイトル更新をここで走らせると、1秒デバウンス前にI/Oが発生する。
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

        internal int BeginCbzLoadGeneration()
        {
            return System.Threading.Interlocked.Increment(ref _cbzLoadGeneration);
        }

        internal bool IsLatestCbzLoadGeneration(int generation)
        {
            return generation == System.Threading.Volatile.Read(ref _cbzLoadGeneration);
        }

        internal System.Threading.SemaphoreSlim VolumeTransitionGate => _volumeTransitionGate;

        internal void UpdateInfoLabelBase(string text, string source = "base")
        {
            UiStateService.UpdateInfoLabelBase(this, text, source);
        }

        internal void UpdateCacheStatusLabel(string cbzPath, string status)
        {
            void Apply()
            {
                UiStateService.UpdateCacheStatusLabel(this, cbzPath, status);
            }

            if (InvokeRequired)
                BeginInvoke((Action)Apply);
            else
                Apply();
        }

        internal void DisplayImages(int startIndex)
        {
            _displayManager.ImagePaths = _imagePaths;
            _displayManager.DisplayImages(startIndex);

            UpdateInfoLabelBase(UiStateService.BuildDisplayInfoText(this));

            CbzFormBridge.TriggerPreloadNearEnd(this, startIndex);

            // Alternate trigger path: if normal navigation hook was missed,
            // enforce focused/next title warmup from display path as well.
            ScheduleNavigationCbzPreload();
        }

        internal void UpdateLayout()
        {
            UiLayoutService.UpdateLayout(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Save playback position for next launch.
            try
            {
                RememberCurrentPlaybackPosition();
                SettingsManager.Save(_settings);
            }
            catch
            {
                // 保存失敗時も終了は継続
            }

            // Flush all pending logs and copy to app-live.log (AI reading).
            LogWriter.Shutdown();

            // Stop timers to prevent further UI operations.
            _folderDebounceTimer?.Stop();
            _slideshowTimer?.Stop();
            _cacheStatusTimer?.Stop();

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
            UiLayoutService.ToggleFullScreen(this);
        }

        public void ToggleLeadingBlankPage()
        {
            UiLayoutService.ToggleLeadingBlankPage(this);
        }

        // Shared label update for DisplayImages/ToggleFullScreen.
        internal void UpdateInfoLabelAfterToggle()
        {
            UiStateService.UpdateInfoLabelAfterToggle(this);
        }

        internal void SetFullScreenMode(bool enabled)
        {
            _fullScreenMode = enabled;

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
        }

        internal void SetLeadingBlankPageEnabled(bool enabled)
        {
            IsLeadingBlankPageEnabled = enabled;
            int startIndex = IsLeadingBlankPageEnabled ? -1 : 0;
            _currentIndex = startIndex;

            if (_imagePaths.Count > 0)
                DisplayImages(startIndex);
            else
                UpdateInfoLabelAfterToggle();
        }

        #region INavigationActions implementation (thin wrappers → FormNavigator)

        public int ImageCount => _imagePaths.Count;
        public int DisplayCount => _settings.DisplayCount;
        public int FolderListCount => _folderList.Count;
        public string CurrentFolder => _currentFolder;

        internal string GetPlaybackTitleKey()
        {
            return PlaybackStateService.GetTitleKey(this);
        }

        internal int GetRememberedPlaybackIndex()
        {
            return PlaybackStateService.GetRememberedIndex(this);
        }

        internal void ApplyPlaybackStartPosition()
        {
            PlaybackStateService.ApplyStartPosition(this);
        }

        internal void RememberCurrentPlaybackPosition()
        {
            PlaybackStateService.RememberCurrent(this);
        }

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
                panelCbzListOverlay = CbzDialogLayout.CreateOverlay(this);
                listBoxCbzFiles = panelCbzListOverlay.Controls.OfType<ListBox>().FirstOrDefault();
            }

            if (listBoxCbzFiles != null)
            {
                CbzSelectionBridge.PopulateSelectionList(this, _cbzManager, listBoxCbzFiles);
            }

            if (panelCbzListOverlay != null && listBoxCbzFiles != null)
            {
                CbzDialogLayout.Layout(this, panelCbzListOverlay, listBoxCbzFiles);
                panelCbzListOverlay.Visible = true;
                panelCbzListOverlay.BringToFront();
                listBoxCbzFiles.Focus();
            }
        }

        private void ListBoxCbzFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxCbzFiles != null && listBoxCbzFiles.SelectedIndex >= 0)
            {
                string? selectedFile = listBoxCbzFiles.SelectedItem?.ToString();
                CbzSelectionBridge.LoadSelectedCbz(this, selectedFile, RememberCurrentPlaybackPosition);
            }
        }

        public void HideCbzSelectDialog()
        {
            if (panelCbzListOverlay != null)
            {
                panelCbzListOverlay.Visible = false;
            }

            if (listBoxCbzFiles != null)
            {
                listBoxCbzFiles.SelectedIndex = -1;
            }

            this.Select();
            this.Focus();
        }

        public bool IsCbzListVisible => panelCbzListOverlay?.Visible ?? false;

        #endregion

        // Implementing missing methods from INavigationActions
        public void ShowSettingsDialog()
        {
            FormNavigator.ShowSettingsDialog(this);
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
            UiBehaviorService.StartSlideshow(this);
        }

        public void StopSlideshow()
        {
            UiBehaviorService.StopSlideshow(this);
        }

        public void NavigateCbzNext()
        {
            FormNavigator.NavigateCbzNext(this);
        }

        public void NavigateCbzPrev()
        {
            FormNavigator.NavigateCbzPrev(this);
        }

        public void CopyCurrentNameToClipboard()
        {
            UiBehaviorService.CopyCurrentNameToClipboard(this);
        }

        private void CbzListOnOk()
        {
            if (listBoxCbzFiles != null && listBoxCbzFiles.SelectedItem != null)
            {
                string? selectedFile = listBoxCbzFiles.SelectedItem?.ToString();
                CbzSelectionBridge.LoadSelectedCbz(this, selectedFile, RememberCurrentPlaybackPosition);
            }
        }

        private void ResetScrollAnimation()
        {
            _listBoxScrollHelper?.ResetScrollAnimation();
        }
    }
}
