using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// キー3で新しいフォルダを読み込む（CJ 作成）際に、
    /// 経過時間とフォルダ読み出しの進行状況を表示するモードレスダイアログ。
    /// </summary>
    public sealed class CjProgressDialog : Form
    {
        private readonly Label _labelFolder;
        private readonly Label _labelProgress;
        private readonly Label _labelElapsed;
        private readonly ProgressBar _progressBar;
        private readonly System.Windows.Forms.Timer _elapsedTimer;
        private readonly Stopwatch _stopwatch = new();

        public CjProgressDialog(string parentFolder)
        {
            Text = "フォルダ読み込み中…";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 150);
            Font = new Font("Yu Gothic UI", 9F);

            var labelTitle = new Label
            {
                Text = "新しいフォルダを読み込んでいます:",
                AutoSize = false,
                Location = new Point(16, 12),
                Size = new Size(428, 20)
            };

            var labelParent = new Label
            {
                Text = parentFolder,
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(16, 32),
                Size = new Size(428, 20)
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(16, 58),
                Size = new Size(428, 18),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };

            _labelFolder = new Label
            {
                Text = "準備中…",
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(16, 84),
                Size = new Size(428, 20)
            };

            _labelProgress = new Label
            {
                Text = "",
                AutoSize = false,
                Location = new Point(16, 108),
                Size = new Size(240, 20)
            };

            _labelElapsed = new Label
            {
                Text = "経過時間: 0.0 秒",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(256, 108),
                Size = new Size(188, 20)
            };

            Controls.Add(labelTitle);
            Controls.Add(labelParent);
            Controls.Add(_progressBar);
            Controls.Add(_labelFolder);
            Controls.Add(_labelProgress);
            Controls.Add(_labelElapsed);

            _elapsedTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _elapsedTimer.Tick += (_, _) => UpdateElapsed();

            _stopwatch.Start();
            _elapsedTimer.Start();
        }

        private void UpdateElapsed()
        {
            _labelElapsed.Text = $"経過時間: {_stopwatch.Elapsed.TotalSeconds:0.0} 秒";
        }

        /// <summary>
        /// 進行状況を更新する。UI スレッド以外から呼ばれても安全に処理する。
        /// </summary>
        public void ReportProgress(int current, int total, string folderName)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ReportProgress(current, total, folderName)));
                return;
            }

            if (total > 0)
            {
                if (_progressBar.Style != ProgressBarStyle.Continuous)
                    _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Maximum = total;
                _progressBar.Value = Math.Min(current, total);
                _labelProgress.Text = $"{current} / {total} フォルダ";
            }

            _labelFolder.Text = string.IsNullOrEmpty(folderName) ? "準備中…" : folderName;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _elapsedTimer.Stop();
            _elapsedTimer.Dispose();
            _stopwatch.Stop();
            base.OnFormClosed(e);
        }
    }
}
