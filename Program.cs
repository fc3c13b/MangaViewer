using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    class Program
    {
        private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        [STAThread]
        static void Main()
        {
            // グローバル例外ハンドラ（UIスレッド以外）
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string msg = $"UnhandledException: {e.ExceptionObject}";
                    File.AppendAllText(LogFile, $"{DateTime.Now}: {msg}{Environment.NewLine}");
                }
                catch { /* ログ書き込み失敗時は何もしない */ }
            };

            // グローバル例外ハンドラ（UIスレッド）
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    string msg = $"ThreadException: {e.Exception}";
                    File.AppendAllText(LogFile, $"{DateTime.Now}: {msg}{Environment.NewLine}");
                }
                catch { /* ログ書き込み失敗時は何もしない */ }
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}