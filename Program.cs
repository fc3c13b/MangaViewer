using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    class Program
    {
        private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogFile, $"{DateTime.Now}: {msg}{Environment.NewLine}"); } catch { }
        }

        [STAThread]
        static void Main()
        {
            Log("=== MangaViewer Starting ===");

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

            // 起動時: ratings_cache.json をメモリにロード（READ ONLY フォルダ向け）
            try
            {
                Log("Loading rating cache...");
                RatingService.LoadReadOnlyCache();
                Log("Rating cache loaded OK");
            }
            catch (Exception ex)
            {
                Log($"Rating cache load failed: {ex}");
            }

            // アプリケーション終了時にratings_cache.jsonを保存
            Application.ApplicationExit += (s, e) => RatingService.FlushReadOnlyCache();

            // 起動時: CBZ展開キャッシュ全削除
            try
            {
                Log("Clearing CBZ cache...");
                CbzManager.ClearAllCache();
                Log("CBZ cache cleared OK");
            }
            catch (Exception ex)
            {
                Log($"CBZ cache clear failed: {ex}");
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Form1 form = null;
            try
            {
                Log("Creating Form1...");
                form = new Form1();
                Log("Form1 created OK");
            }
            catch (Exception ex)
            {
                Log($"Form1 constructor failed: {ex}");
                MessageBox.Show($"起動エラー: {ex.Message}", "MangaViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Log("Running Application...");
                Application.Run(form);
                Log("Application exited normally");
            }
            catch (Exception ex)
            {
                Log($"Application.Run failed: {ex}");
                MessageBox.Show($"実行エラー: {ex.Message}", "MangaViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
