using System.Windows.Forms;

namespace MangaViewer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            this.Text = "Manga Viewer";
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void InitializeComponent()
        {
            this.Size = new System.Drawing.Size(800, 600);
            this.MinimumSize = new System.Drawing.Size(800, 600);
        }
    }
}