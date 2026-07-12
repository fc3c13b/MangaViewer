using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// 設定ダイアログ（Ctrl+0 で表示）
    /// </summary>
    public partial class SettingsDialog : Form
    {
        private Label labelTitle = null!;
        private Label labelVersion = null!;
        private Button btnOk = null!;
        private Button btnCancel = null!;

        public SettingsDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // ダイアログ基本設定
            this.Text = "設定";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new System.Drawing.Size(400, 250);
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
                DialogResult = DialogResult.OK,
            };
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
        }
    }
}