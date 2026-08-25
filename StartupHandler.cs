using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// Form1_Load の初期化フローを整理し、Form1 を短く保つ。
    /// 依存関係の構築・初期設定・エラーハンドリングを一元管理。
    /// </summary>
    public static class StartupHandler
    {
        private static readonly string LoadLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");

        public static void Initialize(Form1 form)
        {
            try
            {
                // 1. Settings ロード
                var (settings, errors) = SettingsManager.LoadWithValidation();
                form._settings = settings;

                if (errors.Count > 0)
                {
                    MessageBox.Show(
                        form,
                        "設定に不正な値が含まれているため、該当項目はデフォルト値を使用します。" + Environment.NewLine +
                            Environment.NewLine + string.Join(Environment.NewLine, errors),
                        "設定エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                // 2. DisplayManager / ImageService 初期化
                form._displayManager = new DisplayManager(form, settings, form._imageService);
                form._displayManager.ImagePaths = form._imagePaths;

                // 3. 基本レイアウト確保＋表示
                form.EnsureBasicLayout();
                form.labelInfo.Text = "読み込み中...";
                form.UpdateLayout();
                Application.DoEvents();

                // 4. バックグラウンド初期処理（CJ/DBList）
                RunBackgroundInit(form, settings);
            }
            catch (Exception ex)
            {
                form.EnsureBasicLayout();
                LogError($"Form1_Load error: {ex}");
                form.labelInfo.Text = "初期化エラーが発生しました。";
            }
        }

        private static void RunBackgroundInit(Form1 form, Settings settings)
        {
            string lastRoot = settings.LastRootFolder;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Yield(); // UI描画を優先

                    bool loadedCj = false;

                    if (!string.IsNullOrEmpty(lastRoot) && Directory.Exists(lastRoot))
                    {
                        form.Invoke((Action)(() =>
                        {
                            form.LoadCjForParent(lastRoot);
                            form.BuildRankFilteredListFromActiveCj();
                        }));
                        Log($"Loaded CJ for LastRootFolder. Folders: {form._folderList.Count}, Index: {form._currentFolderIndex}");
                        loadedCj = true;
                    }

                    form._displayManager.UpdateSettings(settings);

                    if (loadedCj && form._folderList.Count > 0 && form._currentFolderIndex >= 0)
                    {
                        form.Invoke((Action)(() =>
                        {
                            form.LoadAndSortImages(form._folderList[form._currentFolderIndex]);
                            form._currentIndex = 0;
                            form._displayManager.ImagePaths = form._imagePaths;
                            form._displayManager.InitializePictureBoxes();

                            form.PerformLayout();
                            form.UpdateLayout();

                            form._displayManager.DisplayImages(0);
                            form.listBoxFolders.SelectedIndex = form._currentFolderIndex;
                            form.labelInfo.Text = "初期化完了";

                            // Set initial window title with mode and path info
                            form.UpdateWindowTitle();
                        }));
                    }
                    else
                    {
                        form.Invoke((Action)(() =>
                        {
                            form._displayManager.InitializePictureBoxes();
                            form.UpdateLayout();
                            form._displayManager.DisplayImages(0);
                            form.labelInfo.Text = "DBList が空です。キー3でフォルダを選択してください。";
                        }));
                    }

                    Log("Form1_Load complete");
                }
                catch (Exception ex)
                {
                    form.Invoke((Action)(() => form.EnsureBasicLayout()));
                    LogError($"Background init error: {ex}");
                    form.Invoke((Action)(() => { form.labelInfo.Text = "初期化エラーが発生しました。"; }));
                }
            });
        }

        private static void Log(string msg)
        {
            try { File.AppendAllText(LoadLogFile, $"{DateTime.Now}: {msg}{Environment.NewLine}"); } catch { }
        }

        private static void LogError(string message)
        {
            try { File.AppendAllText(LoadLogFile, $"{DateTime.Now}: ERROR: {message}{Environment.NewLine}"); } catch { }
        }
    }
}
