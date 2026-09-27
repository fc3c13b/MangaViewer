using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    class Program
    {
        [STAThread]
        static void Main()
        {
            LogWriter.WriteStartupLog("=== MangaViewer Starting ===");

            // グローバル例外ハンドラ（UIスレッド以外）
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string msg = $"UnhandledException: {e.ExceptionObject}";
                    LogWriter.WriteErrorLog(msg);
                }
                catch { /* ログ書き込み失敗時は何もしない */ }
            };

            // グローバル例外ハンドラ（UIスレッド）
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    string msg = $"ThreadException: {e.Exception}";
                    LogWriter.WriteErrorLog(msg);
                }
                catch { /* ログ書き込み失敗時は何もしない */ }
            };

            // 起動時: ratings_cache.json をメモリにロード（READ ONLY フォルダ向け）
            try
            {
                LogWriter.WriteStartupLog("Loading rating cache...");
                RatingService.LoadReadOnlyCache();
                LogWriter.WriteStartupLog("Rating cache loaded OK");
            }
            catch (Exception ex)
            {
                LogWriter.WriteErrorLog($"Rating cache load failed: {ex}");
            }

            // アプリケーション終了時にratings_cache.jsonを保存
            Application.ApplicationExit += (s, e) => RatingService.FlushReadOnlyCache();

            // キャッシュは再起動後も維持するため、起動時削除を廃止（TASK09.32）

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Form1? form = null;
            try
            {
                LogWriter.WriteStartupLog("Creating Form1...");
                form = new Form1();
                LogWriter.WriteStartupLog("Form1 created OK");
            }
            catch (Exception ex)
            {
                LogWriter.WriteErrorLog($"Form1 constructor failed: {ex}");
                MessageBox.Show($"起動エラー: {ex.Message}", "MangaViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (form == null)
                return;

            try
            {
                LogWriter.WriteStartupLog("Running Application...");
                Application.Run(form);
                LogWriter.WriteStartupLog("Application exited normally");
            }
            catch (Exception ex)
            {
                LogWriter.WriteErrorLog($"Application.Run failed: {ex}");
                MessageBox.Show($"実行エラー: {ex.Message}", "MangaViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
