using System;
using System.Collections.Generic;
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

        // 最小評価値設定用コントロール
        private NumericUpDown numMinEvaluation = null!;
        private Label labelMinEvaluation = null!;

        // 設定ファイルパス（Form1 と共通）
        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.json");

        /// <summary>
        /// 最小表示枚数の値
        /// </summary>
        public int MinDisplayCountValue => (int)numMinDisplayCount.Value;

        /// <summary>
        /// 最小評価値の値（0-10, デフォルト8）
        /// </summary>
        public int MinEvaluationValue => (int)numMinEvaluation.Value;

        // デフォルト値
        private const int DefaultMinDisplayCount = 20;
        private const int DefaultMinEvaluation = 8;

        public SettingsDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // ダイアログ基本設定
            this.Text = "設定";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new System.Drawing.Size(400, 320);
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

            // 起動時に設定値を読み込む
            LoadSettings();
        }

        /// <summary>
        /// setting.json から設定値を読み出してダイアログに反映する。
        /// 設定値が存在しない場合はデフォルト値を使用し、setting.json に保存する。
        /// </summary>
        private void LoadSettings()
        {
            int count = DefaultMinDisplayCount;
            int minEval = DefaultMinEvaluation;

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

                    // 設定値が存在しなかった場合はデフォルト値を保存
                    bool countExists = doc.RootElement.TryGetProperty("MinDisplayCount", out _);
                    bool evalExists = doc.RootElement.TryGetProperty("MinEvaluation", out _);

                    if (!countExists || !evalExists)
                    {
                        SaveSettingsToFile(DefaultMinDisplayCount, DefaultMinEvaluation);
                    }
                }
                catch
                {
                    // 読み込み失敗時はデフォルト値を使用 + デフォルト保存
                    SaveSettingsToFile(DefaultMinDisplayCount, DefaultMinEvaluation);
                }
            }
            else
            {
                // ファイルがない場合はデフォルト値で新規作成
                SaveSettingsToFile(DefaultMinDisplayCount, DefaultMinEvaluation);
            }

            // UI に反映
            numMinDisplayCount.Value = count;
            numMinEvaluation.Value = minEval;
        }

        /// <summary>
        /// OK ボタンクリック時：設定値を保存してダイアログを閉じる
        /// </summary>
        private void BtnOk_Click(object? sender, EventArgs e)
        {
            int count = (int)numMinDisplayCount.Value;
            int minEval = (int)numMinEvaluation.Value;

            SaveSettingsToFile(count, minEval);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// 設定値を setting.json に保存する（LastRootFolder は保持）
        /// </summary>
        private void SaveSettingsToFile(int count, int minEval)
        {
            try
            {
                var obj = new Dictionary<string, object>();

                // 既存の LastRootFolder を保持（ある場合）
                if (File.Exists(SettingsFilePath))
                {
                    var existingJson = File.ReadAllText(SettingsFilePath);
                    using var existingDoc = JsonDocument.Parse(existingJson);
                    if (existingDoc.RootElement.TryGetProperty("LastRootFolder", out var lastFolderProp)
                        && lastFolderProp.ValueKind == JsonValueKind.String)
                    {
                        obj["LastRootFolder"] = lastFolderProp.ToString();
                    }
                }

                obj["MinDisplayCount"] = count;
                obj["MinEvaluation"] = minEval;

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };

                string json = JsonSerializer.Serialize(obj, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // 書き込み失敗時は静かに失敗（アプリ自体は続行）
            }
        }
    }
}