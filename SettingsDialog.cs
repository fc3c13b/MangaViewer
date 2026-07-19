using System;
using System.Drawing;
using System.IO;
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

        // 画面表示数設定用コントロール
        private ComboBox cboDisplayCount = null!;

        // 最小評価値設定用コントロール
        private NumericUpDown numMinEvaluation = null!;
        private Label labelMinEvaluation = null!;

        // フィルター統計表示ラベル
        private Label lblImageFilterStats = null!;
        private Label lblRatingFilterStats = null!;

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
        /// 画面表示数の値（2 または 8）
        /// </summary>
        public int DisplayCountValue => (int)cboDisplayCount.SelectedValue!;

        /// <summary>
        /// 最小評価値の値（0-10, デフォルト8）
        /// </summary>
        public int MinEvaluationValue => (int)numMinEvaluation.Value;

        // デフォルト値
        private const int DefaultMinDisplayCount = 20;
        private const int DefaultMinEvaluation = 8;
        private const int DefaultDisplayCountVal = 2;

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
            this.Size = new System.Drawing.Size(400, 360);
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
            labelTitle.Location = new System.Drawing.Point(20, 20);
            this.Controls.Add(labelTitle);

            // 最小表示枚数 ラベル
            Label labelMinDisplayCount = new Label
            {
                Text = "最小表示枚数：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 60),
            };
            this.Controls.Add(labelMinDisplayCount);

            // 最小表示枚数 NumericUpDown
            numMinDisplayCount = new NumericUpDown
            {
                Minimum = 2,
                Maximum = 100,
                Value = DefaultMinDisplayCount,
                Location = new System.Drawing.Point(160, 58),
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
                Location = new System.Drawing.Point(245, 62),
            };
            this.Controls.Add(labelMinDisplayUnit);

            // 最小画像数フィルター統計ラベル（「枚」の右隣）
            lblImageFilterStats = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = Color.Cyan,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(270, 60),
            };
            this.Controls.Add(lblImageFilterStats);

            // 最小評価値 ラベル
            labelMinEvaluation = new Label
            {
                Text = "最小評価値：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 100),
            };
            this.Controls.Add(labelMinEvaluation);

            // 最小評価値 NumericUpDown
            numMinEvaluation = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 10,
                Value = DefaultMinEvaluation,
                Location = new System.Drawing.Point(160, 98),
                Size = new System.Drawing.Size(80, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
            };
            this.Controls.Add(numMinEvaluation);

            // 最小評価値フィルター統計ラベル（numMinEvaluation の右隣）
            lblRatingFilterStats = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = Color.Cyan,
                Font = new System.Drawing.Font("Meiryo UI", 9F),
                Location = new System.Drawing.Point(270, 100),
            };
            this.Controls.Add(lblRatingFilterStats);

            // 画面表示数 ラベル
            Label labelDisplayCount = new Label
            {
                Text = "画面表示数：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 140),
            };
            this.Controls.Add(labelDisplayCount);

            // 画面表示数 ComboBox (2 / 8)
            cboDisplayCount = new ComboBox
            {
                Location = new System.Drawing.Point(160, 137),
                Size = new System.Drawing.Size(80, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            cboDisplayCount.Items.Add(new { Text = "2", Value = 2 });
            cboDisplayCount.Items.Add(new { Text = "8", Value = 8 });
            cboDisplayCount.DisplayMember = "Text";
            cboDisplayCount.ValueMember = "Value";
            this.Controls.Add(cboDisplayCount);

            // バージョン表示ラベル
            labelVersion = new Label
            {
                Text = $"アプリバージョン: {Constants.AppVersion}",
                AutoSize = true,
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
            };
            labelVersion.Location = new System.Drawing.Point(20, this.ClientSize.Height - 80);
            this.Controls.Add(labelVersion);

            // OK ボタン
            btnOk = new Button
            {
                Text = "OK",
                Size = new System.Drawing.Size(100, 30),
                Location = new System.Drawing.Point(this.ClientSize.Width / 2 - 110, this.ClientSize.Height - 45),
            };
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            // キャンセル ボタン
            btnCancel = new Button
            {
                Text = "キャンセル",
                Size = new System.Drawing.Size(100, 30),
                Location = new System.Drawing.Point(this.ClientSize.Width / 2 + 10, this.ClientSize.Height - 45),
                DialogResult = DialogResult.Cancel,
            };
            this.Controls.Add(btnCancel);

            // AcceptButton / CancelButton
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            // ValueChanged イベントでリアルタイム統計更新
            numMinDisplayCount.ValueChanged += (_, __) => UpdateFilterStats();
            numMinEvaluation.ValueChanged += (_, __) => UpdateFilterStats();
            cboDisplayCount.SelectedIndexChanged += (_, __) => UpdateFilterStats();

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
            int minRating = (int)numMinEvaluation.Value;

            // UI スレッドで即座に計算（フォルダ数が多い場合は非同期検討）
            var (imagePass, total, ratingPass) = _folderService.ComputeDisplayStats(root, minImages, minRating);

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
            int minEval = DefaultMinEvaluation;
            int displayCnt = DefaultDisplayCountVal;

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
                        if (val >= 2 && val <= 100)
                            count = val;
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

                    // 設定値が存在しなかった場合はデフォルト値を保存
                    bool countExists = doc.RootElement.TryGetProperty("MinDisplayCount", out _);
                    bool evalExists = doc.RootElement.TryGetProperty("MinEvaluation", out _);
                    bool displayExists = doc.RootElement.TryGetProperty("DisplayCount", out _);

                    if (!countExists || !evalExists || !displayExists)
                    {
                        SaveSettingsToFile(DefaultMinDisplayCount, DefaultMinEvaluation, DefaultDisplayCountVal);
                    }
                }
                catch
                {
                    // 読み込み失敗時はデフォルト値を使用 + デフォルト保存
                    SaveSettingsToFile(DefaultMinDisplayCount, DefaultMinEvaluation, DefaultDisplayCountVal);
                }
            }
            else
            {
                // ファイルがない場合はデフォルト値で新規作成
                SaveSettingsToFile(DefaultMinDisplayCount, DefaultMinEvaluation, DefaultDisplayCountVal);
            }

            // UI に反映（ValueChanged イベントでUpdateFilterStats が自動呼び出される）
            numMinDisplayCount.Value = count;
            numMinEvaluation.Value = minEval;

            // ComboBox の SelectedValue を設定後、統計を更新
            cboDisplayCount.SelectedValue = displayCnt;

            // 設定読み込み後に統計を表示
            UpdateFilterStats();
        }

        /// <summary>
        /// 設定値を setting.json に保存する（OK ボタン用）
        /// </summary>
        private void SaveSettingsToFile(int minDisplayCount, int minEvaluation, int displayCount)
        {
            try
            {
                var obj = new System.Collections.Generic.Dictionary<string, object>();

                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateArray())
                    {
                        // 既存プロパティを保持（配列形式の場合）
                    }
                    // オブジェクト形式で上書き保存
                }

                obj["MinDisplayCount"] = minDisplayCount;
                obj["MinEvaluation"] = minEvaluation;
                obj["DisplayCount"] = displayCount;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var newJson = JsonSerializer.Serialize(obj, options);
                File.WriteAllText(SettingsFilePath, newJson);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            // 現在のUI値を取得して保存
            int minDisplayCount = (int)numMinDisplayCount.Value;
            int minEvaluation = (int)numMinEvaluation.Value;
            int displayCount = cboDisplayCount != null ? (int)cboDisplayCount.SelectedValue! : DefaultDisplayCountVal;

            // 設定ファイルを保存
            SaveSettingsToFile(minDisplayCount, minEvaluation, displayCount);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}