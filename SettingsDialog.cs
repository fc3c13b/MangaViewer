using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
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

        // 画面表示数設定用コントロール（チェックボックス2つで排他選択）
        private CheckBox cbDisplay2 = null!;
        private CheckBox cbDisplay8 = null!;

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

        // 評価1キーワード設定用コントロール
        private TextBox txtRatingOneKeywords = null!;

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
        /// 画面表示数の値（2 または 8）
        /// </summary>
        public int DisplayCountValue => cbDisplay2.Checked ? 2 : 8;

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
        /// 評価1キーワード（カンマ区切り）
        /// </summary>
        public string RatingOneKeywordsValue => txtRatingOneKeywords.Text?.Trim() ?? "";

        // デフォルト値
        private const int DefaultMinDisplayCount = 20;
        private const int DefaultMaxDisplayCount = 1000;
        private const int DefaultMinEvaluation = 8;
        private const int DefaultDisplayCountVal = 2;
        private const int DefaultNormalModeImageAreaPercent = 72;
        private const int DefaultFullScreenModeImageAreaPercent = 85;

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
            this.Size = new System.Drawing.Size(420, 600);
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

            // 画面表示数 ラベル
            Label labelDisplayCount = new Label
            {
                Text = "画面表示数：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 175),
            };
            this.Controls.Add(labelDisplayCount);

            // 画面表示数チェックボックス（2 / 8 排他選択）
            cbDisplay2 = new CheckBox
            {
                Text = "2",
                Location = new System.Drawing.Point(140, 172),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 40, 40),
                Checked = true, // デフォルトは2
            };
            cbDisplay2.Click += (s, e) => { if (cbDisplay2.Checked) cbDisplay8.Checked = false; UpdateFilterStats(); };
            this.Controls.Add(cbDisplay2);

            cbDisplay8 = new CheckBox
            {
                Text = "8",
                Location = new System.Drawing.Point(190, 172),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 40, 40),
            };
            cbDisplay8.Click += (s, e) => { if (cbDisplay8.Checked) cbDisplay2.Checked = false; UpdateFilterStats(); };
            this.Controls.Add(cbDisplay8);

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

            // 評価1キーワード ラベル
            Label labelRatingOneKeywords = new Label
            {
                Text = "評価1キーワード：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(20, 345),
            };
            this.Controls.Add(labelRatingOneKeywords);

            // 評価1キーワード TextBox（カンマ区切り）
            txtRatingOneKeywords = new TextBox
            {
                Location = new System.Drawing.Point(170, 342),
                Size = new System.Drawing.Size(190, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
            };
            this.Controls.Add(txtRatingOneKeywords);

            // 説明ラベル
            Label labelRatingOneHint = new Label
            {
                Text = "(カンマ区切りで複数指定)",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 8F),
                Location = new System.Drawing.Point(170, 365),
            };
            this.Controls.Add(labelRatingOneHint);

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
        /// フィルター統計を更新してUIに表示（非同期で軽量計算）
        /// </summary>
        private void UpdateFilterStats()
        {
            string root = RootFolder;
            int minImages = (int)numMinDisplayCount.Value;
            int maxImages = (int)numMaxDisplayCount.Value;
            int minRating = (int)numMinEvaluation.Value;

            // UI スレッドで即座に計算（フォルダ数が多い場合は非同期検討）
            var (imagePass, total, ratingPass) = _folderService.ComputeDisplayStats(root, minImages, maxImages, minRating);

            lblImageFilterStats.Text = $"[{imagePass}/{total}]";
            lblRatingFilterStats.Text = $"[{ratingPass}/{imagePass}]";
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
            string ratingOneKeywords = "";

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

                    // 評価1キーワード
                    if (doc.RootElement.TryGetProperty("RatingOneKeywords", out var rkProp)
                        && rkProp.ValueKind == JsonValueKind.String)
                    {
                        ratingOneKeywords = rkProp.ToString();
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
                            ratingOneKeywords);
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
                        "");
                }
            }

            // UI に反映
            numMinDisplayCount.Value = count;
            numMaxDisplayCount.Minimum = count + 1;
            numMaxDisplayCount.Value = Math.Max(maxCount, count + 1);
            cbDisplay2.Checked = displayCnt == 2;
            cbDisplay8.Checked = displayCnt == 8;
            if (!cbDisplay2.Checked && !cbDisplay8.Checked)
            {
                cbDisplay2.Checked = true;
            }
            numMinEvaluation.Value = minEval;

            // レイアウト比率を反映（範囲制限付き）
            if (normalPercent >= (int)numNormalLayoutRatio.Minimum && normalPercent <= (int)numNormalLayoutRatio.Maximum)
                numNormalLayoutRatio.Value = normalPercent;

            if (fullScreenPercent >= (int)numFullScreenLayoutRatio.Minimum && fullScreenPercent <= (int)numFullScreenLayoutRatio.Maximum)
                numFullScreenLayoutRatio.Value = fullScreenPercent;

            // 評価1キーワードを反映
            txtRatingOneKeywords.Text = ratingOneKeywords;

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
            int displayCount = DisplayCountValue; // 2 または 8
            int normalPercent = NormalModeImageAreaPercent;
            int fullScreenPercent = FullScreenModeImageAreaPercent;
            string ratingOneKeywords = RatingOneKeywordsValue;

            SaveSettingsToFile(minDisplayCount, maxDisplayCount, minEvaluation, displayCount, normalPercent, fullScreenPercent, ratingOneKeywords);

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
            string ratingOneKeywords)
        {
            try
            {
                // 既存 JSON を保持（LastRootFolder など他の設定を壊さない）
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

                // 新キーを設定
                existing["MinDisplayCount"] = JsonSerializer.SerializeToElement(minDisplayCount);
                existing["MaxDisplayCount"] = JsonSerializer.SerializeToElement(maxDisplayCount);
                existing["MinEvaluation"] = JsonSerializer.SerializeToElement(minEvaluation);
                existing["DisplayCount"] = JsonSerializer.SerializeToElement(displayCount);
                existing["NormalModeImageAreaPercent"] = JsonSerializer.SerializeToElement(normalPercent);
                existing["FullScreenModeImageAreaPercent"] = JsonSerializer.SerializeToElement(fullScreenPercent);
                existing["RatingOneKeywords"] = JsonSerializer.SerializeToElement(ratingOneKeywords ?? "");

                // JSON を再構築（順番は保証されないが問題なし）
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