using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaViewer
{
    public partial class Form1 : Form
    {
        private PictureBox pictureBoxLeft = null;
        private PictureBox pictureBoxRight = null;

        public Form1()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // フォーム基本設定
            this.Text = "Manga Viewer";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(1200, 800);
            this.MinimumSize = new Size(800, 600);
            this.BackColor = Color.Black;

            // 右側 PictureBox（小さい数字の画像用）
            pictureBoxRight = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
            };

            // 左側 PictureBox（大きい数字の画像用）
            pictureBoxLeft = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
            };

            this.Controls.Add(pictureBoxRight);
            this.Controls.Add(pictureBoxLeft);

            // 初期レイアウト適用
            UpdatePictureBoxLayout();

            // イベント
            this.Resize += (s, e) => UpdatePictureBoxLayout();
        }

        private void UpdatePictureBoxLayout()
        {
            if (pictureBoxRight == null || pictureBoxLeft == null) return;

            int midX = this.ClientSize.Width / 2;
            int height = this.ClientSize.Height;

            // 右側
            pictureBoxRight.Bounds = new Rectangle(midX, 0, midX - 5, height);

            // 左側
            pictureBoxLeft.Bounds = new Rectangle(0, 0, midX - 5, height);
        }
    }
}