using System;
using System.Windows.Forms;

namespace MangaViewer
{
    class Program
    {
        public const string AppVersion = "3.5.0";

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}