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
        private CheckBox chkMinDisplayCount = null!;
        private NumericUpDown numMinDisplayCount = null!;
        private Label labelMinDisplayUnit = null!;

        // 設定ファイルパス（Form1 と共通）
        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setting.json");

        /// <summary>
        /// 最小表示枚数が有効かどうか
        /// </summary>
        public bool MinDisplayCountEnabled => chkMinDisplayCount.Checked;

        /// <summary>
        /// 最小表示枚数の値
        /// </summary>
        public int MinDisplayCountValue => (int)numMinDisplayCount.Value;

        // デフォルト値
        private const int DefaultMinDisplayCount = 20;
        private const bool DefaultMinDisplayEnabled = false;

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

            // 最小表示枚数 チェックボックス
            chkMinDisplayCount = new CheckBox
            {
                Text = "最小表示枚数：",
                AutoSize = true,
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Meiryo UI", 10F),
                Location = new System.Drawing.Point(20, 60),
            };
            this.Controls.Add(chkMinDisplayCount);

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

            // バージョン表示ラベル
            string version = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "unknown";

            labelVersion = new Label
            {
                Text = $"アプリバージョン: {version}",
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
            bool enabled = DefaultMinDisplayEnabled;
            int count = DefaultMinDisplayCount;

            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("MinDisplayCountEnabled", out var enabledProp))
                    {
                        try { enabled = enabledProp.GetBoolean(); } catch { /* デフォルト値を維持 */ }
                    }

                    if (doc.RootElement.TryGetProperty("MinDisplayCount", out var countProp)
                        && countProp.ValueKind == JsonValueKind.Number)
                    {
                        int val = countProp.GetInt32();
                        if (val >= 2 && val <= 100)
                            count = val;
                    }

                    // 設定値が存在しなかった場合はデフォルト値を保存
                    bool enabledExists = doc.RootElement.TryGetProperty("MinDisplayCountEnabled", out _);
                    bool countExists = doc.RootElement.TryGetProperty("MinDisplayCount", out _);

                    if (!enabledExists || !countExists)
                    {
                        SaveSettingsToFile(DefaultMinDisplayEnabled, DefaultMinDisplayCount);
                    }
                }
                catch
                {
                    // 読み込み失敗時はデフォルト値を使用 + デフォルト保存
                    SaveSettingsToFile(DefaultMinDisplayEnabled, DefaultMinDisplayCount);
                }
            }
            else
            {
                // ファイルがない場合はデフォルト値で新規作成
                SaveSettingsToFile(DefaultMinDisplayEnabled, DefaultMinDisplayCount);
            }

            // UI に反映
            chkMinDisplayCount.Checked = enabled;
            numMinDisplayCount.Value = count;
        }

        /// <summary>
        /// OK ボタンクリック時：設定値を保存してダイアログを閉じる
        /// </summary>
        private void BtnOk_Click(object? sender, EventArgs e)
        {
            bool enabled = chkMinDisplayCount.Checked;
            int count = (int)numMinDisplayCount.Value;

            SaveSettingsToFile(enabled, count);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// 設定値を setting.json に保存する（LastRootFolder は保持）
        /// </summary>
        private void SaveSettingsToFile(bool enabled, int count)
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

                obj["MinDisplayCountEnabled"] = enabled;
                obj["MinDisplayCount"] = count;

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