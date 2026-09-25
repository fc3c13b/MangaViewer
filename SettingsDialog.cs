using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// 設定ダイアログ（Ctrl+0 で表示）
    /// setting.json の読み出し・保存を自動処理
    /// </summary>
    public partial class SettingsDialog : Form
    {
        private Label labelTitle = null!;
        private Label labelVersion = null!;
        private Button btnOk = null!;
        private Button btnCancel = null!;

        // 最小表示枚数設定用コントロール
        private NumericUpDown numMinDisplayCount = null!;
        private Label labelMinDisplayUnit = null!;

        // 最大表示画像数設定用コントロール
        private NumericUpDown numMaxDisplayCount = null!;

        // 最小評価値設定用コントロール
        private NumericUpDown numMinEvaluation = null!;
        private Label labelMinEvaluation = null!;

        // フィルター統計表示ラベル
        private Label lblImageFilterStats = null!;
        private Label lblRatingFilterStats = null!;

        // レイアウト比率設定用コントロール（ノーマル）：数値入力
        private NumericUpDown numNormalLayoutRatio = null!;

        // レイアウト比率設定用コントロール（全画面）：数値入力
        private NumericUpDown numFullScreenLayoutRatio = null!;

        // ルートフォルダパス（統計計算用）
        private string RootFolder => GetRootFolder();

        private FolderService _folderService = null!;

        // 設定ファイルパス（Form1 と共通）
        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.json");

        /// <summary>
        /// 最小表示枚数の値
        /// </summary>
        public int MinDisplayCountValue => (int)numMinDisplayCount.Value;

        /// <summary>
        /// 最大表示画像数の値
        /// </summary>
        public int MaxDisplayCountValue => (int)numMaxDisplayCount.Value;

        /// <summary>
        /// 最小評価値の値（0-10, デフォルト8）
        /// </summary>
        public int MinEvaluationValue => (int)numMinEvaluation.Value;

        /// <summary>
        /// ノーマル表示時の画像領域幅比率（%）
        /// </summary>
        public int NormalModeImageAreaPercent => (int)numNormalLayoutRatio.Value;

        /// <summary>
        /// 全画面表示時の画像領域幅比率（%）
        /// </summary>
        public int FullScreenModeImageAreaPercent => (int)numFullScreenLayoutRatio.Value;

        /// <summary>
        /// 評価値フィルタ有効/無効（デフォルト true）
        /// </summary>
        public bool MinEvaluationFilterEnabledValue => cbMinEvalFilterEnabled.Checked;

        /// <summary>
        /// 評価値フィルタの比較モード（true=等しい, false=以上）
        /// </summary>
        public bool MinEvaluationEqualFilterValue => cbMinEvalEqualFilter.Checked;

        /// <summary>
        /// 読了位置から読み始めるかどうか
        /// </summary>
        public bool StartFromLastViewedPositionValue => cbStartFromLastViewedPosition.Checked;

        // デフォルト値
        private const int DefaultMinDisplayCount = 20;
        private const int DefaultMaxDisplayCount = 1000;
        private const int DefaultMinEvaluation = 8;
        private const int DefaultDisplayCountVal = 2;
        private const int DefaultNormalModeImageAreaPercent = 72;
        private const int DefaultFullScreenModeImageAreaPercent = 85;

        // 新フィルタ用コントロール
        private CheckBox cbMinEvalFilterEnabled = null!;
        private CheckBox cbMinEvalEqualFilter = null!;
        private CheckBox cbStartFromLastViewedPosition = null!;

        public SettingsDialog()
        {
            _folderService = new FolderService(new Settings());
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // ダイアログ基本設定
            this.Text = "設定";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new System.Drawing.Size(420, 540);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.BackColor = Color.FromArgb(40, 40, 40);

            // タイトルラベル
            labelTitle = new Label
            {
                Text = "Manga Viewer 設定",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 12F, FontStyle.Bold),
            };
            labelTitle.Location = new System.Drawing.Point(20, 15);
            this.Controls.Add(labelTitle);

            // 最小表示枚数 ラベル
            Label labelMinDisplayCount = new Label
            {
                Text = "最小表示枚数：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 55),
            };
            this.Controls.Add(labelMinDisplayCount);

            // 最小表示枚数 NumericUpDown
            numMinDisplayCount = new NumericUpDown
            {
                Minimum = 2,
                Maximum = 99999,
                Value = DefaultMinDisplayCount,
                Location = new System.Drawing.Point(140, 53),
                Size = new System.Drawing.Size(80, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
            };
            this.Controls.Add(numMinDisplayCount);

            // 「枚」ラベル
            labelMinDisplayUnit = new Label
            {
                Text = "枚",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(225, 57),
            };
            this.Controls.Add(labelMinDisplayUnit);

            // 「枚」の右隣：統計表示
            lblImageFilterStats = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = Color.Cyan,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(250, 57),
            };
            this.Controls.Add(lblImageFilterStats);

            // 最大表示画像数 ラベル
            Label labelMaxDisplayCount = new Label
            {
                Text = "最大表示画像数：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 95),
            };
            this.Controls.Add(labelMaxDisplayCount);

            // 最大表示画像数 NumericUpDown
            numMaxDisplayCount = new NumericUpDown
            {
                Minimum = DefaultMinDisplayCount + 1,
                Maximum = 100000,
                Value = DefaultMaxDisplayCount,
                Location = new System.Drawing.Point(140, 93),
                Size = new System.Drawing.Size(80, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
            };
            this.Controls.Add(numMaxDisplayCount);

            Label labelMaxDisplayUnit = new Label
            {
                Text = "枚",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(225, 97),
            };
            this.Controls.Add(labelMaxDisplayUnit);

            // 最小評価値 ラベル
            labelMinEvaluation = new Label
            {
                Text = "最小評価値：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 135),
            };
            this.Controls.Add(labelMinEvaluation);

            // 最小評価値 NumericUpDown
            numMinEvaluation = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 10,
                Value = DefaultMinEvaluation,
                Location = new System.Drawing.Point(140, 133),
                Size = new System.Drawing.Size(80, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
            };
            this.Controls.Add(numMinEvaluation);

            // numMinEvaluation の右隣：統計表示
            lblRatingFilterStats = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = Color.Cyan,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(250, 137),
            };
            this.Controls.Add(lblRatingFilterStats);

            // セクション区切り（レイアウト比率）
            Label labelLayoutSection = new Label
            {
                Text = "画像領域の横幅比",
                AutoSize = true,
                ForeColor = Color.LimeGreen,
                Font = new System.Drawing.Font("Meiryo UI", 10F, FontStyle.Bold),
                Location = new System.Drawing.Point(20, 215),
            };
            this.Controls.Add(labelLayoutSection);

            // ノーマル表示：画像領域幅比率（数値入力）
            Label labelNormalLayoutRatioHint = new Label
            {
                Text = "ノーマル表示の画像領域幅:",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(20, 240),
            };
            this.Controls.Add(labelNormalLayoutRatioHint);

            numNormalLayoutRatio = new NumericUpDown
            {
                Minimum = 50,
                Maximum = 95,
                Value = DefaultNormalModeImageAreaPercent,
                Location = new System.Drawing.Point(170, 238),
                Size = new System.Drawing.Size(80, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
            };
            this.Controls.Add(numNormalLayoutRatio);

            Label labelNormalPercentUnit = new Label
            {
                Text = "%",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(255, 242),
            };
            this.Controls.Add(labelNormalPercentUnit);

            // ノーマル説明
            Label labelNormalHint = new Label
            {
                Text = "(残り % はフォルダリスト領域)",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 8F),
                Location = new System.Drawing.Point(20, 270),
            };
            this.Controls.Add(labelNormalHint);

            // 全画面表示：画像領域幅比率（数値入力）
            Label labelFullScreenLayoutRatioHint = new Label
            {
                Text = "全画面表示の画像領域幅:",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(20, 305),
            };
            this.Controls.Add(labelFullScreenLayoutRatioHint);

            numFullScreenLayoutRatio = new NumericUpDown
            {
                Minimum = 70,
                Maximum = 98,
                Value = DefaultFullScreenModeImageAreaPercent,
                Location = new System.Drawing.Point(170, 303),
                Size = new System.Drawing.Size(80, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
            };
            this.Controls.Add(numFullScreenLayoutRatio);

            Label labelFullPercentUnit = new Label
            {
                Text = "%",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(255, 307),
            };
            this.Controls.Add(labelFullPercentUnit);

            // 読了位置から読み始める
            cbStartFromLastViewedPosition = new CheckBox
            {
                Text = "読了位置から読み始める",
                Location = new System.Drawing.Point(20, 340),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 40, 40),
                Checked = true,
            };
            this.Controls.Add(cbStartFromLastViewedPosition);

            // 評価値フィルタ設定セクション
            Label labelFilterSection = new Label
            {
                Text = "評価値フィルタ設定",
                AutoSize = true,
                ForeColor = Color.LimeGreen,
                Font = new System.Drawing.Font("Meiryo UI", 10F, FontStyle.Bold),
                Location = new System.Drawing.Point(20, 380),
            };
            this.Controls.Add(labelFilterSection);

            // 評価値フィルタ有効/無効
            cbMinEvalFilterEnabled = new CheckBox
            {
                Text = "評価値フィルタを有効にする",
                Location = new System.Drawing.Point(20, 410),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 40, 40),
                Checked = true, // デフォルト有効
            };
            this.Controls.Add(cbMinEvalFilterEnabled);

            // 評価値フィルタの比較モード（「以上」vs「等しい」）
            cbMinEvalEqualFilter = new CheckBox
            {
                Text = "評価値を『等しい』でフィルタする（OFF=「以上」）",
                Location = new System.Drawing.Point(20, 440),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 40, 40),
                Checked = false, // デフォルト「以上」
            };
            this.Controls.Add(cbMinEvalEqualFilter);

            // バージョン表示ラベル（下部：Constants から一元取得）
            labelVersion = new Label
            {
                Text = $"アプリバージョン: Ver{Constants.AppVersion}",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
            };
            labelVersion.Location = new System.Drawing.Point(20, this.ClientSize.Height - 90);
            this.Controls.Add(labelVersion);

            // OK ボタン
            btnOk = new Button
            {
                Text = "OK",
                Size = new System.Drawing.Size(100, 30),
                Location = new System.Drawing.Point(this.ClientSize.Width / 2 - 120, this.ClientSize.Height - 45),
            };
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            // キャンセル ボタン
            btnCancel = new Button
            {
                Text = "キャンセル",
                Size = new System.Drawing.Size(100, 30),
                Location = new System.Drawing.Point(this.ClientSize.Width / 2 + 20, this.ClientSize.Height - 45),
                DialogResult = DialogResult.Cancel,
            };
            this.Controls.Add(btnCancel);

            // AcceptButton / CancelButton
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            // ValueChanged イベントでリアルタイム統計更新
            numMinDisplayCount.ValueChanged += (_, __) =>
            {
                // 最大値の最小制限を最小値+1に連動させる
                numMaxDisplayCount.Minimum = numMinDisplayCount.Value + 1;
                if (numMaxDisplayCount.Value <= numMinDisplayCount.Value)
                    numMaxDisplayCount.Value = numMinDisplayCount.Value + 1;
                UpdateFilterStats();
            };
            numMaxDisplayCount.ValueChanged += (_, __) => UpdateFilterStats();
            numMinEvaluation.ValueChanged += (_, __) => UpdateFilterStats();

            // 起動時に設定値を読み込む
            LoadSettings();
        }


        private string GetRootFolder()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("LastRootFolder", out var prop)
                        && prop.ValueKind == JsonValueKind.String)
                    {
                        string path = prop.ToString();
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            return path;
                    }
                }
            }
            catch { /* ルートフォルダ取得失敗時はデフォルトにフォールバック */ }
            // デフォルト: MyDocuments フォルダ
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>
        /// フィルター統計を更新してUIに表示（非同期で30秒タイムアウト付き）
        /// </summary>
        private void UpdateFilterStats()
        {
            // 前回の非同期計算をキャンセルせず、新しい計算を開始
            Task.Run(() =>
            {
                string root = RootFolder;
                int minImages = (int)numMinDisplayCount.Value;
                int maxImages = (int)numMaxDisplayCount.Value;
                int minRating = (int)numMinEvaluation.Value;

                var (imagePass, total, ratingPass) = _folderService.ComputeDisplayStats(root, minImages, maxImages, minRating);

                this.Invoke(new Action(() =>
                {
                    lblImageFilterStats.Text = $"[{imagePass}/{total}]";
                    lblRatingFilterStats.Text = $"[{ratingPass}/{imagePass}]";
                }));
            });
        }

        /// <summary>
        /// setting.json から設定値を読み出してダイアログに反映する。
        /// 設定値が存在しない場合はデフォルト値を使用し、setting.json に保存する。
        /// </summary>
        private void LoadSettings()
        {
            int count = DefaultMinDisplayCount;
            int maxCount = DefaultMaxDisplayCount;
            int minEval = DefaultMinEvaluation;
            int displayCnt = DefaultDisplayCountVal;
            int normalPercent = DefaultNormalModeImageAreaPercent;
            int fullScreenPercent = DefaultFullScreenModeImageAreaPercent;

            bool startFromLastViewedPosition = true;
            bool minEvalFilterEnabled = true;
            bool minEvalEqualFilter = false;

            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("MinDisplayCount", out var countProp)
                        && countProp.ValueKind == JsonValueKind.Number)
                    {
                        int val = countProp.GetInt32();
                        if (val >= 2 && val <= 99999)
                            count = val;
                    }

                    if (doc.RootElement.TryGetProperty("MaxDisplayCount", out var maxCountProp)
                        && maxCountProp.ValueKind == JsonValueKind.Number)
                    {
                        int val = maxCountProp.GetInt32();
                        if (val > count)
                            maxCount = val;
                    }

                    if (doc.RootElement.TryGetProperty("MinEvaluation", out var evalProp)
                        && evalProp.ValueKind == JsonValueKind.Number)
                    {
                        int val = evalProp.GetInt32();
                        if (val >= 0 && val <= 10)
                            minEval = val;
                    }

                    if (doc.RootElement.TryGetProperty("DisplayCount", out var displayProp)
                        && displayProp.ValueKind == JsonValueKind.Number)
                    {
                        int val = displayProp.GetInt32();
                        if (val == 2 || val == 8)
                            displayCnt = val;
                    }

                    // レイアウト比率：存在すれば使用、なければデフォルト
                    if (doc.RootElement.TryGetProperty("NormalModeImageAreaPercent", out var normalProp)
                        && normalProp.ValueKind == JsonValueKind.Number)
                    {
                        int val = normalProp.GetInt32();
                        if (val >= 50 && val <= 95)
                            normalPercent = val;
                    }

                    if (doc.RootElement.TryGetProperty("FullScreenModeImageAreaPercent", out var fullScreenProp)
                        && fullScreenProp.ValueKind == JsonValueKind.Number)
                    {
                        int val = fullScreenProp.GetInt32();
                        if (val >= 70 && val <= 98)
                            fullScreenPercent = val;
                    }

                    if (doc.RootElement.TryGetProperty("StartFromLastViewedPosition", out var startFromProp)
                        && (startFromProp.ValueKind == JsonValueKind.True || startFromProp.ValueKind == JsonValueKind.False))
                    {
                        startFromLastViewedPosition = startFromProp.GetBoolean();
                    }

                    // 評価値フィルタ設定読み込み（旧 DbFilterGreaterOrEqual から移行）
                    if (doc.RootElement.TryGetProperty("MinEvaluationFilterEnabled", out var mefProp) && (mefProp.ValueKind == JsonValueKind.True || mefProp.ValueKind == JsonValueKind.False))
                    {
                        minEvalFilterEnabled = mefProp.GetBoolean();
                    }
                    else if (doc.RootElement.TryGetProperty("DbFilterGreaterOrEqual", out var oldProp) && (oldProp.ValueKind == JsonValueKind.True || oldProp.ValueKind == JsonValueKind.False))
                    {
                        // 旧キーからの移行：DbFilterGreaterOrEqual=false → MinEvaluationEqualFilter=true
                        minEvalFilterEnabled = true;
                        minEvalEqualFilter = !oldProp.GetBoolean();
                    }

                    if (doc.RootElement.TryGetProperty("MinEvaluationEqualFilter", out var meqProp) && (meqProp.ValueKind == JsonValueKind.True || meqProp.ValueKind == JsonValueKind.False))
                    {
                        minEvalEqualFilter = meqProp.GetBoolean();
                    }

                    // 設定値が存在しなかった場合はデフォルト値を保存（新旧互換対応）
                    bool countExists = doc.RootElement.TryGetProperty("MinDisplayCount", out _);
                    bool maxCountExists = doc.RootElement.TryGetProperty("MaxDisplayCount", out _);
                    bool evalExists = doc.RootElement.TryGetProperty("MinEvaluation", out _);
                    bool displayExists = doc.RootElement.TryGetProperty("DisplayCount", out _);

                    if (!countExists || !maxCountExists || !evalExists || !displayExists)
                    {
                        SaveSettingsToFile(
                            count,
                            maxCount,
                            minEval,
                            displayCnt,
                            normalPercent,
                            fullScreenPercent,
                            startFromLastViewedPosition);
                    }
                }
                catch
                {
                    // 読み込み失敗時はデフォルト値を使用 + デフォルト保存
                    SaveSettingsToFile(
                        DefaultMinDisplayCount,
                        DefaultMaxDisplayCount,
                        DefaultMinEvaluation,
                        DefaultDisplayCountVal,
                        DefaultNormalModeImageAreaPercent,
                        DefaultFullScreenModeImageAreaPercent,
                        true);
                }
            }

            // UI に反映
            numMinDisplayCount.Value = count;
            numMaxDisplayCount.Minimum = count + 1;
            numMaxDisplayCount.Value = Math.Max(maxCount, count + 1);
            numMinEvaluation.Value = minEval;

            // レイアウト比率を反映（範囲制限付き）
            if (normalPercent >= (int)numNormalLayoutRatio.Minimum && normalPercent <= (int)numNormalLayoutRatio.Maximum)
                numNormalLayoutRatio.Value = normalPercent;

            if (fullScreenPercent >= (int)numFullScreenLayoutRatio.Minimum && fullScreenPercent <= (int)numFullScreenLayoutRatio.Maximum)
                numFullScreenLayoutRatio.Value = fullScreenPercent;

            cbStartFromLastViewedPosition.Checked = startFromLastViewedPosition;

            // 評価値フィルタ設定を反映
            cbMinEvalFilterEnabled.Checked = minEvalFilterEnabled;
            cbMinEvalEqualFilter.Checked = minEvalEqualFilter;

            // 初期値でフィルター統計を表示
            UpdateFilterStats();
        }

        /// <summary>
        /// OK ボタン押下処理：新しい設定を保存
        /// </summary>
        private void BtnOk_Click(object? sender, EventArgs e)
        {
            int minDisplayCount = (int)numMinDisplayCount.Value;
            int maxDisplayCount = MaxDisplayCountValue;
            int minEvaluation = (int)numMinEvaluation.Value;

            // DisplayCount は UI に表示しないが JSON には保存
            int displayCount = DefaultDisplayCountVal;

            int normalPercent = NormalModeImageAreaPercent;
            int fullScreenPercent = FullScreenModeImageAreaPercent;
            bool startFromLastViewedPosition = StartFromLastViewedPositionValue;

            SaveSettingsToFile(
                minDisplayCount, maxDisplayCount, minEvaluation, displayCount,
                normalPercent, fullScreenPercent, startFromLastViewedPosition);

            this.DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// 設定を JSON ファイルに保存（既存キーを保持、必要に応じて上書き）
        /// </summary>
        private void SaveSettingsToFile(
            int minDisplayCount,
            int maxDisplayCount,
            int minEvaluation,
            int displayCount,
            int normalPercent,
            int fullScreenPercent,
            bool startFromLastViewedPosition)
        {
            try
            {
                var existing = new Dictionary<string, JsonElement>();

                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            existing[prop.Name] = prop.Value.Clone();
                        }
                    }
                }

                // 設定値を保存（DisplayCount は UI に表示しないが JSON キーは維持）
                existing["MinDisplayCount"] = JsonSerializer.SerializeToElement(minDisplayCount);
                existing["MaxDisplayCount"] = JsonSerializer.SerializeToElement(maxDisplayCount);
                existing["MinEvaluation"] = JsonSerializer.SerializeToElement(minEvaluation);
                existing["DisplayCount"] = JsonSerializer.SerializeToElement(displayCount);
                existing["NormalModeImageAreaPercent"] = JsonSerializer.SerializeToElement(normalPercent);
                existing["FullScreenModeImageAreaPercent"] = JsonSerializer.SerializeToElement(fullScreenPercent);
                existing["StartFromLastViewedPosition"] = JsonSerializer.SerializeToElement(startFromLastViewedPosition);

                // 評価値フィルタ設定
                existing["MinEvaluationFilterEnabled"] = JsonSerializer.SerializeToElement(cbMinEvalFilterEnabled.Checked);
                existing["MinEvaluationEqualFilter"] = JsonSerializer.SerializeToElement(cbMinEvalEqualFilter.Checked);

                // 旧キーの削除（RatingOneKeywords, DbFilterGreaterOrEqual, DbMinDisplayCount, etc.）
                existing.Remove("RatingOneKeywords");
                existing.Remove("DbFilterGreaterOrEqual");
                existing.Remove("DbMinDisplayCount");
                existing.Remove("DbMaxDisplayCount");
                existing.Remove("DbMinEvaluation");

                var options = new JsonSerializerOptions { WriteIndented = true };
                string resultJson = JsonSerializer.Serialize(existing, options);
                File.WriteAllText(SettingsFilePath, resultJson);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"設定の保存に失敗しました：{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
