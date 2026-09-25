using System;
using System.IO;
using System.Linq;

namespace MangaViewer;

/// <summary>
/// アプリケーションログの一元書き出しモジュール。
/// - 通常/起動ログ: startup-{yyyyMMdd}.log
/// - エラーログ: error.log
/// - error.log が100行以上になったら error-{yyyyMMdd-HHmmss}.log へ退避
/// </summary>
internal static class LogWriter
{
    private const int MaxErrorLogLines = 100;
    private static readonly object _sync = new();
    private static readonly string BaseDirectory;
    private static readonly string ErrorLogFilePath;

    static LogWriter()
    {
        BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        ErrorLogFilePath = Path.Combine(BaseDirectory, "error.log");
    }

    /// <summary>初期化（互換のため残す）</summary>
    public static void Init()
    {
        // no-op
    }

    /// <summary>通常/起動ログを startup-{yyyyMMdd}.log に出力する。</summary>
    public static void WriteStartupLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (_sync)
        {
            string startupPath = Path.Combine(BaseDirectory, $"startup-{DateTime.Now:yyyyMMdd}.log");
            AppendLine(startupPath, message);
        }
    }

    /// <summary>エラー専用ログを error.log に出力する。</summary>
    public static void WriteErrorLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (_sync)
        {
            RotateErrorLogIfNeeded();
            AppendLine(ErrorLogFilePath, message);
        }
    }

    /// <summary>終了処理（互換のため残す）</summary>
    public static void Shutdown() { }

    private static void AppendLine(string path, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        try
        {
            File.AppendAllText(path, line);
        }
        catch
        {
            // ログ書き込み失敗は致命エラーとして扱わない
        }
    }

    private static void RotateErrorLogIfNeeded()
    {
        try
        {
            if (!File.Exists(ErrorLogFilePath))
                return;

            int lineCount = File.ReadLines(ErrorLogFilePath).Take(MaxErrorLogLines + 1).Count();
            if (lineCount < MaxErrorLogLines)
                return;

            string archivedFileName = $"error-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            string archivedPath = Path.Combine(BaseDirectory, archivedFileName);

            if (File.Exists(archivedPath))
                archivedPath = Path.Combine(BaseDirectory, $"error-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

            File.Move(ErrorLogFilePath, archivedPath);
        }
        catch
        {
            // ローテート失敗時はそのまま継続
        }
    }
}
