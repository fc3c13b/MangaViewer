using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
                File.WriteAllText(logPath, ex.ToString());
                MessageBox.Show($"アプリの起動中にエラーが発生しました。\n\nエラー内容: {logPath}\n\n詳細をコピーしてください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}