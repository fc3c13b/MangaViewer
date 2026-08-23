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
        internal int _displayMode = 0; // 0=階層順(FolderList), 1=DBList(Rank)
        public bool IsRankDisplayMode => _displayMode == 1;

        /// <summary>
        /// DBList モードかどうかを判定する（ShowSettingsDialog などから利用）。
        /// </summary>
        private bool IsDbListMode() => _displayMode != 0;

        private string _rootFolder = "";
        private string? _activeDbFile = null; // アクティブな CJ/DB のファイルパス

        // オンメモリ CJ 保持（TASK09.18）
        private CjRoot? _activeCjData;
        private string? _activeCjParentFolder;

        /// <summary>
        /// 指定した親フォルダに対して CJ を読み込み、オンメモリに保持する。
        /// - 既存の CJ ファイルがあればそれをロード
        /// - なければ CreateCjForParent で新規作成
        /// </summary>
        public void LoadCjForParent(string parentFolder)
        {
            if (string.IsNullOrEmpty(parentFolder)) return;

            // 同じ親フォルダで既に保持済みの場合は再読み込み不要
            if (_activeCjParentFolder == parentFolder && _activeCjData != null)
                return;

            string? cjPath = AppPaths.GetCacheFilePath(parentFolder);

            if (!string.IsNullOrEmpty(cjPath) && File.Exists(cjPath))
            {
                // 既存 CJ をロードしてオンメモリ保持
                try
                {
                    var json = File.ReadAllText(cjPath);
                    var options = new System.Text.Json.JsonSerializerOptions();
                    options.PropertyNameCaseInsensitive = true;
                    var cjRoot = System.Text.Json.JsonSerializer.Deserialize<CjRoot>(json, options);

                    if (cjRoot != null)
                    {
                        _activeCjParentFolder = parentFolder;
                        _activeCjData = cjRoot;
                        _activeDbFile = cjPath;
                    }
                }
                catch
                {
                    // パース失敗時は後続で再作成経路に任せる
                }
            }

            // CJ がまだない、またはロード失敗した場合は新規作成
            if (_activeCjData == null)
            {
                CreateCjForParent(parentFolder);
            }
            else
            {
                Log($"[DB_DEBUG] LoadCjForParent: loaded existing CJ for parent={parentFolder}, folders_count={_activeCjData?.Folders?.Count ?? 0}");

                // DB リストモードなら、ロードした CJ に基づいてリストを再構築
                if (IsRankDisplayMode)
                {
                    BuildRankFilteredListFromActiveCj();
                }
            }
        }

        /// <summary>
        /// キー3（DBリストモード時）で親フォルダー指定してCJを作成する。
        /// 重いスキャンはバックグラウンド化し、完了後にオンメモリ CJ を保持・再構築。
/// </summary>
        public void CreateCjForParent(string parentFolder)
        {
            if (string.IsNullOrWhiteSpace(parentFolder)) return;
            if (!Directory.Exists(parentFolder)) return;

            Application.DoEvents();

            // UI に「作成中」表示
            SafeInvokeUI(() => { labelInfo.Text = "CJ作成中…"; });

            Task.Run(() =>
            {
                try
                {
                    var folders = new Dictionary<string, CjFolderEntry>();

                    if (Directory.Exists(parentFolder))
                    {
                        foreach (string dir in Directory.GetDirectories(parentFolder, "*", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                int imageCount = 0;
                                int cbzZipCount = 0;

                                var files = Directory.GetFiles(dir, "*");
                                foreach (var f in files)
                                {
                                    string ext = Path.GetExtension(f)?.ToLowerInvariant();
                                    if (ext == ".cbz" || ext == ".zip")
                                        cbzZipCount++;
                                    else if (
                                        ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp")
                                    {
                                        imageCount++;
                                    }
                                }

                                int rating = RatingService.ReadRating(dir);

                                var entry = new CjFolderEntry
                                {
                                    FolderName = Path.GetFileName(dir),
                                    ImageCount = imageCount,
                                    CbzZipCount = cbzZipCount,
                                    Rating = rating,
                                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                };

                                folders[dir] = entry;
                            }
                            catch { /* フォルダ読み取りエラーはスキップ */ }
                        }
                    }

                    var cjData = new CjRoot
                    {
                        ParentFolder = parentFolder,
                        Folders = folders
                    };

                    string cjPath = AppPaths.GetCacheFilePath(parentFolder);
                    CjManager.SaveCj(parentFolder, cjData);

                    // オンメモリ CJ 保持（TASK09.18）
                    _activeCjParentFolder = parentFolder;
                    _activeCjData = cjData;

                    // アクティブ DB/CJ を設定（DBList モード用）
                    _activeDbFile = cjPath;

                    Log($"[DB_DEBUG] CreateCjForParent: CJ created/updated for parent={parentFolder}, folders_count={cjData.Folders.Count}");

                    // UI スレッドで再構築・表示更新
                    SafeInvokeUI(() =>
                    {
                        if (IsRankDisplayMode)
                        {
                            Log($"[DB_DEBUG] CreateCjForParent: about to build DB list from CJ, parent={parentFolder}");
                            BuildRankFilteredListFromActiveCj();
                        }

                        UpdateWindowTitle();
                        labelInfo.Text = $"CJ作成完了: {parentFolder}";
                    });
                }
                catch (Exception ex)
                {
                    SafeInvokeUI(() =>
                    {
                        MessageBox.Show(
                            this,
                            $"CJ作成エラー:\n{ex.Message}",
                            "CJ エラー",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        labelInfo.Text = "CJ作成に失敗しました。";
                    });
                }
            });
        }

        /// <summary>
        /// UI スレッドで安全に実行するヘルパー（Invoke/BeginInvoke 用）。
        /// </summary>
        private void SafeInvokeUI(Action action)
        {
            if (this.InvokeRequired)
                this.BeginInvoke(action);
            else
                action();
        }

        private void BuildRankFilteredListFromCj(Dictionary<string, CjFolderEntry> folders)
        {
            Log($"[DB_DEBUG] BuildRankFilteredListFromCj: called with folders_count={folders?.Count ?? 0}");

            if (!IsRankDisplayMode) return;

            // DBList: _activeCjData.Folders の情報だけでフィルタ（ディスクI/Oなし）
            // 7.4: CJ に記載された順序を尊重し、フィルタリングのみ行う。
            Settings settings = SettingsManager.Load();
            bool dbFilterGreaterOrEqual = settings.DbFilterGreaterOrEqual;
            int dbMinEval = settings.DbMinEvaluation;
            int dbMinDisplay = settings.DbMinDisplayCount;
            int dbMaxDisplay = settings.DbMaxDisplayCount;

            _folderList.Clear();
            listBoxFolders.DataSource = null;
            listBoxFolders.Items.Clear();

            foreach (var kvp in folders)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;

                string folderPath = kvp.Key;
                var entry = kvp.Value;
                int cbzZipCount = entry.CbzZipCount ?? 0;
                int imageCount = entry.ImageCount ?? 0;
                int rating = entry.Rating;

                bool show = false;

                if (cbzZipCount >= 1)
                {
                    // 7.2 CBZ/ZIPありの場合
                    if (dbFilterGreaterOrEqual)
                    {
                        // A: DB表示チェックON → (Rating==-1) または (Rating>=DbMinEvaluation)
                        show = (rating == -1 || rating >= dbMinEval);
                    }
                    else
                    {
                        // B: DB表示チェックOFF → CBZ/ZIPがあれば表示
                        show = true;
                    }
                }
                else
                {
                    // 7.3 CBZ/ZIPなし、画像ファイルのみ
                    bool countOk = (dbMinDisplay <= imageCount) && (imageCount <= dbMaxDisplay);

                    if (!countOk) continue;

                    if (dbFilterGreaterOrEqual)
                    {
                        // A: DB表示チェックON → Rating条件 + 枚数範囲
                        show = (rating == -1 || rating >= dbMinEval);
                    }
                    else
                    {
                        // B: DB表示チェックOFF → 枚数範囲のみ
                        show = true;
                    }
                }

                if (!show) continue;

                _folderList.Add(folderPath);

                string folderName = Path.GetFileName(folderPath);
                int displayCount = cbzZipCount >= 1 ? cbzZipCount : imageCount;
                listBoxFolders.Items.Add($"[{displayCount}] {folderName}");
            }

            Log($"[DB_DEBUG] BuildRankFilteredListFromCj: resulting folder_list_count={_folderList.Count}");

            if (_folderList.Count > 0)
            {
                int idx = _folderList.IndexOf(_currentFolder);
                _currentFolderIndex = idx >= 0 ? idx : 0;
                listBoxFolders.SelectedIndex = _currentFolderIndex;
            }
        }

        private FormWindowState _savedWindowState = FormWindowState.Normal;
        private FormBorderStyle _savedFormBorderStyle = FormBorderStyle.Sizable;
        private Size _savedSize = new Size(Constants.InitialWidth, Constants.InitialHeight);

        private Settings _settings = new Settings();
        private readonly ImageService _imageService = new ImageService();
        private FolderService? _folderService;
        private Timer? _slideshowTimer;
        private bool _isDisplayModeToggling = false;
        private System.Windows.Forms.Timer? _folderDebounceTimer;

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

        private static readonly string LoadLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        private void Form1_Load(object? sender, EventArgs e)
        {
            try
            {
                Log("Form1_Load start");
                var (loadedSettings, errors) = SettingsManager.LoadWithValidation();
                _settings = loadedSettings;
                Log($"Settings loaded. Errors: {errors.Count}");

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
                Log("DisplayManager created");

                // 基本レイアウトを先に確保してフォームを表示
                EnsureBasicLayout();
                labelInfo.Text = "読み込み中...";
                UpdateLayout();
                Application.DoEvents();

                string rootPath = string.IsNullOrEmpty(_settings.LastRootFolder)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : _settings.LastRootFolder;
                Log($"Root path: {rootPath}");

                // 重い処理をバックグラウンドで実行
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Yield(); // UI描画を優先
                        
                        this.Invoke(() => BuildSubfolderList(rootPath));
                        Log($"BuildSubfolderList done. Folders: {_folderList.Count}, Index: {_currentFolderIndex}");

                        _displayManager.UpdateSettings(_settings);

                        if (_folderList.Count > 0 && _currentFolderIndex >= 0)
                        {
                            this.Invoke(() => LoadAndSortImages(_folderList[_currentFolderIndex]));
                            Log($"LoadAndSortImages done. Images: {_imagePaths.Count}");

                            this.Invoke(() =>
                            {
                                _displayManager.ImagePaths = _imagePaths;
                                _currentIndex = 0;
                                _displayManager.InitializePictureBoxes();
                                Log("InitializePictureBoxes done");

                                this.PerformLayout();
                                UpdateLayout();
                                Log("UpdateLayout done");

                                _displayManager.DisplayImages(0);
                                Log("DisplayImages done");

                                listBoxFolders.SelectedIndex = _currentFolderIndex;
                                labelInfo.Text = "初期化完了";

                                // Set initial window title with mode and path info
                                UpdateWindowTitle();
                            });
                        }
                        else
                        {
                            this.Invoke(() =>
                            {
                                _displayManager.InitializePictureBoxes();
                                UpdateLayout();
                                _displayManager.DisplayImages(0);
                                labelInfo.Text = "表示可能なフォルダがありません。";
                            });
                        }
                        Log("Form1_Load complete");
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(() => EnsureBasicLayout());
                        try { File.AppendAllText(LoadLogFile, $"Background init error: {ex}{Environment.NewLine}"); } catch { }
                        this.Invoke(() => { labelInfo.Text = "初期化エラーが発生しました。"; });
                    }
                });
            }
            catch (Exception ex)
            {
                EnsureBasicLayout();

                try { File.AppendAllText(LoadLogFile, $"Form1_Load error: {ex}{Environment.NewLine}"); } catch { }

                labelInfo.Text = "初期化エラーが発生しました。";
            }
        }

        private void Log(string msg)
        {
            try { File.AppendAllText(LoadLogFile, $"{DateTime.Now}: {msg}{Environment.NewLine}"); } catch { }
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
            _rootFolder = rootPath;
            _folderList.Clear();
            _currentFolderIndex = -1;

            labelInfo.Text = "フォルダを読み込み中...";
            Application.DoEvents();

            if (_folderService == null)
                _folderService = new FolderService(_settings);

            // ステータスコールバックを設定（CBZカウント中などのメッセージをlabelInfoに表示）
            _folderService.SetStatusCallback(msg =>
            {
                labelInfo.Text = msg;
                Application.DoEvents();
            });

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

            // If no images found, try CBZ via CbzManager as fallback
            if ((_imagePaths == null || _imagePaths.Count == 0) && Directory.Exists(folderPath))
            {
                string[] cbzFiles;
                try
                {
                    cbzFiles = System.IO.Directory.GetFiles(folderPath, "*.cbz", System.IO.SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException)
                {
                    // 権限不足：CBZスキャンはスキップ
                    cbzFiles = Array.Empty<string>();
                }
                catch
                {
                    // その他エラーもスキップ（クラッシュ防止）
                    cbzFiles = Array.Empty<string>();
                }

                if (cbzFiles.Length > 0)
                {
                    if (_cbzManager == null) _cbzManager = new CbzManager();
                    bool ok = _cbzManager.InitializeForFolder(folderPath);
                    if (ok && _cbzManager.CurrentImagePaths != null && _cbzManager.CurrentImagePaths.Count > 0)
                    {
                        _imagePaths = _cbzManager.CurrentImagePaths;
                    }
                }
            }

            _displayManager.ImagePaths = _imagePaths;
        }

        private void UpdateWindowTitle()
        {
            string baseTitle = Constants.AppTitle;

            if (_displayMode == 0)
            {
                // FolderList mode: show root path or "<none>"
                string label = string.IsNullOrWhiteSpace(_rootFolder)
                    ? "[<none>]"
                    : $"[{_rootFolder}]";
                this.Text = $"{baseTitle} - FolderList {label}";
            }
            else
            {
                // DBList mode: show active DB/CJ filename
                if (!string.IsNullOrEmpty(_activeDbFile))
                {
                    string dbName = Path.GetFileName(_activeDbFile);
                    this.Text = $"{baseTitle} - DBList [{dbName}]";
                }
                else
                {
                    // Fallback to rank_display_db.json (dummy) when no CJ selected.
                    string dbName = Constants.RankDisplayDbName;
                    this.Text = $"{baseTitle} - DBList [{dbName}]";
                }
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
                SelectionMode = SelectionMode.One, HorizontalScrollbar = true, TabStop = false
            };

            listBoxFolders.KeyDown += (s, e) => KeyboardInputHandler.HandleKeyDown(e, this);

            listBoxFolders.SelectedIndexChanged += (s, e) =>
            {
                if (listBoxFolders.SelectedIndex >= 0 && _folderList.Count > 0)
                {
                    _currentFolderIndex = listBoxFolders.SelectedIndex;
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

            string folderName = Path.GetFileName(_currentFolder);
            string cbzInfo = "";
            if (_cbzManager != null && _cbzManager.CbxFiles.Count > 0)
            {
                int idx = Math.Clamp(_cbzManager.ActiveCbxIndex, 0, _cbzManager.CbxFiles.Count - 1);
                string cbzFileName = Path.GetFileNameWithoutExtension(_cbzManager.CbxFiles[idx]);
                // ファイル名から巻数情報を抽出（例: "Vol1", "001", "巻1" など）
                int volNum = FolderService.ExtractNumberFromFileName(cbzFileName);
                if (volNum > 0)
                    cbzInfo = $" [{cbzFileName}]";
            }

            string folderDisplay = $"{folderName}{cbzInfo}";

            string infoText = _displayManager.GetInfoText(_currentFolder, _currentFolderIndex, _folderService!.entries);
            if (!string.IsNullOrEmpty(infoText))
                labelInfo.Text = $"[{folderDisplay}] {infoText.TrimStart()}";
            else
                labelInfo.Text = $"[{folderDisplay}] 表示可能な画像がありません。";

            // CBZ表示中、残り16ページ以下なら次のCBZをプレロード（バックグラウンドスレッドでUIブロックしない）
            if (_cbzManager != null && _imagePaths.Count > 0)
            {
                int remainingPages = _imagePaths.Count - startIndex;
                if (remainingPages <= 16)
                {
                    _ = Task.Run(() => _cbzManager.PreloadNextCbx());
                }
            }
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

            string folderName2 = Path.GetFileName(_currentFolder);
            string cbzInfo2 = "";
            if (_cbzManager != null && _cbzManager.CbxFiles.Count > 0)
            {
                int idx2 = Math.Clamp(_cbzManager.ActiveCbxIndex, 0, _cbzManager.CbxFiles.Count - 1);
                string cbzFileName2 = Path.GetFileNameWithoutExtension(_cbzManager.CbxFiles[idx2]);
                int volNum2 = FolderService.ExtractNumberFromFileName(cbzFileName2);
                if (volNum2 > 0)
                    cbzInfo2 = $" [{cbzFileName2}]";
            }

            string folderDisplay2 = $"{folderName2}{cbzInfo2}";

            string infoText = _displayManager.GetInfoText(_currentFolder, _currentFolderIndex, _folderService!.entries);
            if (!string.IsNullOrEmpty(infoText))
                labelInfo.Text = $"[{folderDisplay2}] {infoText.TrimStart()}";
            else
                labelInfo.Text = $"[{folderDisplay2}] 表示可能な画像がありません。";

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

            UpdateWindowTitle();

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

        private static void DebugLog(string msg)
        {
            try { File.AppendAllText(LoadLogFile, $"[SetDisplay] {DateTime.Now}: {msg}{Environment.NewLine}"); } catch { }
        }

        public void SetDisplayCount(int count)
        {
            DebugLog($">>> Enter count={count}, currentDisplayCount={_settings.DisplayCount}, imagePaths.Count={_imagePaths.Count}, currentIndex={_currentIndex}");
            if (count <= 0) { DebugLog("count<=0, returning"); return; }
            
            int prevDisplayCount = _settings.DisplayCount;
            _settings.DisplayCount = count;
            DebugLog($"_settings.DisplayCount: {prevDisplayCount} -> {_settings.DisplayCount}");
            
            SettingsManager.Save(_settings);
            DebugLog("Settings saved");
            
            _displayManager.UpdateSettings(_settings);
            DebugLog($"UpdateSettings done, DisplayManager.DisplayCount={_displayManager.DisplayCount}");

            if (_settings.DisplayCount != prevDisplayCount)
            {
                DebugLog("DisplayCount changed, calling InitializePictureBoxes...");
                _displayManager.InitializePictureBoxes();
                DebugLog($"InitializePictureBoxes done, pictureBoxes.Length={_displayManager.pictureBoxes.Length}");
                
                DebugLog("Calling UpdateLayout...");
                UpdateLayout();
                DebugLog("UpdateLayout done");
                
                _currentIndex = 0;
                DebugLog($"_currentIndex set to 0, imagePaths.Count={_imagePaths.Count}");
                
                if (_imagePaths.Count > 0)
                {
                    DebugLog("Calling DisplayImages(0)...");
                    DisplayImages(0);
                    DebugLog("DisplayImages(0) done");
                }
                else
                {
                    DebugLog("_imagePaths.Count is 0, skipping DisplayImages");
                }
            }
            else
            {
                DebugLog("DisplayCount unchanged, skipping redraw");
            }
            
            this.Focus();
            DebugLog("<<< Exit SetDisplayCount complete");
        }

        // N-page jump (for Ctrl/Alt + arrow keys)
        public void NavigateForward(int pageCount)
        {
            if (_imagePaths.Count == 0 || pageCount <= 0) return;

            _currentIndex += pageCount;

            int maxIndex = Math.Max(0, _imagePaths.Count - (_imagePaths.Count % _displayManager.DisplayCount == 0 ? _displayManager.DisplayCount : 1));
            if (maxIndex < 0) maxIndex = 0;

            // CBZ末尾を超えた場合、次のCBZに切り替え
            if (_currentIndex > maxIndex && _cbzManager != null)
            {
                var nextPaths = _cbzManager.MoveToNextCbxIfEndReached();
                if (nextPaths.Any())
                {
                    _imagePaths = nextPaths;
                    _currentIndex = 0;
                }
                else
                {
                    _currentIndex = maxIndex;
                }
            }
            else if (_currentIndex > maxIndex)
            {
                _currentIndex = maxIndex;
            }

            DisplayImages(_currentIndex);
        }

        public void NavigateBackward(int pageCount)
        {
            if (_imagePaths.Count == 0 || pageCount <= 0) return;

            _currentIndex -= pageCount;

            // CBZ先頭を超えた場合、前のCBZに切り替え
            if (_currentIndex < 0 && _cbzManager != null)
            {
                var prevPaths = _cbzManager.MoveToPreviousCbxIfAtStart();
                if (prevPaths.Any())
                {
                    _imagePaths = prevPaths;
                    _currentIndex = Math.Max(0, _imagePaths.Count - (_imagePaths.Count % _displayManager.DisplayCount == 0 ? _displayManager.DisplayCount : 1));
                }
                else
                {
                    _currentIndex = 0;
                }
            }
            else if (_currentIndex < 0)
            {
                _currentIndex = 0;
            }

            DisplayImages(_currentIndex);
        }

        public void NavigateCbzNext()
        {
            if (_cbzManager == null || _cbzManager.CbxFiles.Count <= 1) return;
            
            var nextCbx = _cbzManager.SwitchToNextCbx();
            if (nextCbx != null)
            {
                _imagePaths = _cbzManager.CurrentImagePaths;
                _currentIndex = 0;
                DisplayImages(0);
            }
        }

        public void NavigateCbzPrev()
        {
            if (_cbzManager == null || _cbzManager.CbxFiles.Count <= 1) return;
            
            var prevCbx = _cbzManager.SwitchToPreviousCbx();
            if (prevCbx != null)
            {
                _imagePaths = _cbzManager.CurrentImagePaths;
                _currentIndex = 0;
                DisplayImages(0);
            }
        }



        public void StartSlideshow()
        {
            if (_slideshowTimer == null)
                _slideshowTimer = new Timer { Interval = 3000 };
            
            _slideshowTimer.Tick += (s, e) =>
            {
                NavigateForwardTwoPages();
            };
            
            _slideshowTimer.Start();
        }

        public void StopSlideshow()
        {
            _slideshowTimer?.Stop();
        }

        /// <summary>
        /// 表示モードを切り替え：0=階層順, 1=DBList(CJベース)で評価値8のフォルダのみ表示。
        /// </summary>
        public void ToggleDisplayMode()
        {
            _displayMode = _displayMode == 0 ? 1 : 0;

            if (_displayMode == 1)
            {
                // DBList モード: アクティブ CJ があればそれを、なければダミー rank_display_db.json を使う
                BuildRankFilteredListFromActiveCj();
            }
            else
            {
                // 元の階層順に戻す
                if (!string.IsNullOrEmpty(_rootFolder))
                    BuildSubfolderList(_rootFolder);
            }

            UpdateWindowTitle();
        }

        /// <summary>
        /// rank_display_db.json を読み込み、評価値が Constants.TargetDisplayRating に一致するフォルダをリスト表示する。
        /// </summary>
        private void BuildRankFilteredList()
        {
            const int targetRating = Constants.TargetDisplayRating;
            string dbPath = AppPaths.RankDisplayDbPath;

            labelInfo.Text = $"評価値{targetRating}のフォルダを読み込み中...";
            Application.DoEvents();

            _folderList.Clear();
            listBoxFolders.DataSource = null;
            listBoxFolders.Items.Clear();

            try
            {
                if (!File.Exists(dbPath))
                {
                    labelInfo.Text = $"DBファイルが見つかりません: {dbPath}";
                    return;
                }

                var json = File.ReadAllText(dbPath);
                var options = new System.Text.Json.JsonSerializerOptions();
                options.PropertyNameCaseInsensitive = true;
                var root = System.Text.Json.JsonSerializer.Deserialize<RankDbRoot>(json, options);

                if (root?.Folders == null)
                {
                    labelInfo.Text = "DBファイルの読み込みに失敗しました。";
                    return;
                }

                int count = 0;
                foreach (var folder in root.Folders)
                {
                    string path = folder.Key;
                    var entry = folder.Value;
                    if (entry.Rating == targetRating && !string.IsNullOrEmpty(path))
                    {
                        _folderList.Add(path);
                        string folderName = Path.GetFileName(path);
                        listBoxFolders.Items.Add($"[{entry.Rating}] {folderName} -[{entry.ImageCount}]");
                        count++;
                    }
                }

                if (_folderList.Count > 0)
                {
                    int idx = _folderList.IndexOf(_currentFolder);
                    _currentFolderIndex = idx >= 0 ? idx : 0;
                    listBoxFolders.SelectedIndex = _currentFolderIndex;
                }
                else
                {
                    labelInfo.Text = $"評価値{targetRating}のフォルダが見つかりません。";
                }

                labelInfo.Text = $"評価値{targetRating}: {count}件表示中";
            }
            catch (Exception ex)
            {
                labelInfo.Text = $"DB読み込みエラー: {ex.Message}";
            }
        }

        /// <summary>
        /// Rating をオンメモリ更新＋保存（TASK09.18 §5）。
        /// 今後 Rating 書き込み処理から共用して使用する。
        /// </summary>
        private void UpdateRatingInMemory(string folderPath, int newRating)
        {
            if (_activeCjData == null || string.IsNullOrEmpty(_activeCjParentFolder)) return;
            if (!_activeCjData.Folders.TryGetValue(folderPath, out var entry)) return;

            // オンメモリ更新
            entry.Rating = newRating;

            // ディスク保存
            CjManager.SaveCj(_activeCjParentFolder, _activeCjData);
        }

        /// <summary>
        /// DBList モード用：アクティブ CJ（またはフォールバックの rank_display_db）から評価値フィルタリストを構築。
        /// TASK09.18: オンメモリ _activeCjData を優先し、ファイル再読込を避ける。
        /// </summary>
        private void BuildRankFilteredListFromActiveCj()
        {
            Log("[DB_DEBUG] BuildRankFilteredListFromActiveCj: start");

            // 1) オンメモリ CJ データがあればそれを優先使用
            if (_activeCjData?.Folders != null && _activeCjData.Folders.Count > 0)
            {
                Log($"[DB_DEBUG] using in-memory CJ, folders_count={_activeCjData.Folders.Count}");
                BuildRankFilteredListFromCj(_activeCjData.Folders);
                return;
            }

            // 2) ファイルからフォールバック（_activeDbFile が存在する場合）
            if (!string.IsNullOrEmpty(_activeDbFile) && File.Exists(_activeDbFile))
            {
                try
                {
                    var json = File.ReadAllText(_activeDbFile);
                    var options = new System.Text.Json.JsonSerializerOptions();
                    options.PropertyNameCaseInsensitive = true;
                    var cjRoot = System.Text.Json.JsonSerializer.Deserialize<CjRoot>(json, options);

                    if (cjRoot?.Folders != null)
                    {
                        // 次に備えてオンメモリに保持
                        _activeCjData = cjRoot;
                        BuildRankFilteredListFromCj(cjRoot.Folders);
                        return;
                    }
                }
                catch
                {
                    // 読み取り失敗時は次のフォールバックへ
                }
            }

            // 3) アクティブ CJ が未選択または無効な場合：rank_display_db.json をダミーとして使用
            BuildRankFilteredList();
        }

        /// <summary>
        /// rank_display_db.json のルート構造。
        /// </summary>
        private class RankDbRoot
        {
            public Dictionary<string, RankDbFolderEntry> Folders { get; set; }
        }

        /// <summary>
        /// rank_display_db.json のフォルダエントリ（フォルダパスがキーとして使用される）。
        /// </summary>
        private class RankDbFolderEntry
        {
            public int Rating { get; set; }
            public int ImageCount { get; set; }
        }

        public void ShowSettingsDialog()
        {
            // Before-dialog snapshots (FolderList filters)
            int prevDisplayCount = _settings.DisplayCount;
            int prevMinDisplayCount = _settings.MinDisplayCount;
            int prevMaxDisplayCount = _settings.MaxDisplayCount;
            int prevMinEvaluation = _settings.MinEvaluation;

            // DBList filters
            int prevDbMinDisplayCount = _settings.DbMinDisplayCount;
            int prevDbMaxDisplayCount = _settings.DbMaxDisplayCount;
            int prevDbMinEvaluation = _settings.DbMinEvaluation;

            // Layout ratios
            int prevNormalImageAreaPercent = _settings.NormalModeImageAreaPercent;
            int prevFullScreenImageAreaPercent = _settings.FullScreenModeImageAreaPercent;

            using (var dialog = new SettingsDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    (_settings, _) = SettingsManager.LoadWithValidation();
                    _displayManager.UpdateSettings(_settings);

                    bool displayCountChanged = _settings.DisplayCount != prevDisplayCount;
                    bool layoutRatioChanged = _settings.NormalModeImageAreaPercent != prevNormalImageAreaPercent ||
                                              _settings.FullScreenModeImageAreaPercent != prevFullScreenImageAreaPercent;

                    // FolderList mode filters (MinEvaluation / MinDisplayCount / MaxDisplayCount)
                    bool folderFilterChanged = (_settings.MinDisplayCount != prevMinDisplayCount) ||
                                               (_settings.MaxDisplayCount != prevMaxDisplayCount) ||
                                               (_settings.MinEvaluation != prevMinEvaluation);

                    // DBList mode filters (DbMinEvaluation / DbMinDisplayCount / DbMaxDisplayCount)
                    bool dbFilterChanged = (_settings.DbMinEvaluation != prevDbMinEvaluation) ||
                                           (_settings.DbMinDisplayCount != prevDbMinDisplayCount) ||
                                           (_settings.DbMaxDisplayCount != prevDbMaxDisplayCount);

                    if (!IsDbListMode() && folderFilterChanged)
                    {
                        // FolderList mode: rebuild subfolder list with new filters.
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

                    if (IsDbListMode() && dbFilterChanged)
                    {
                        // DBList mode: rebuild rank-filtered list with new DB filters.
                        BuildRankFilteredListFromActiveCj();

                        // Restore selection to current folder if still present.
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
                        else if (_folderList.Count > 0)
                        {
                            _currentFolderIndex = 0;
                            LoadAndSortImages(_folderList[0]);
                            _currentIndex = 0;
                            DisplayImages(0);
                            listBoxFolders.SelectedIndex = 0;
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