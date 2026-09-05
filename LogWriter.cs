using System;
using System.IO;
using System.Text;

namespace MangaViewer;

/// <summary>
/// アプリケーションログの一元書き出しモジュール。
/// - RingBuffer で byte バッファリング（8KB）
/// - 4KB を超えたら直近の改行位置で切り出し、ファイルにフラッシュ
/// - ログディレクトリの下には app-live.{YYYY-MM-DD-HH-mm-ss}.log を作成
/// - 最新ログのみ app-live.log にコピーして AI が読めるようにする
/// </summary>
internal static class LogWriter
{
    private const int FlushThresholdBytes = 4096;       // フラッシュ判定（byte）
    private const int MaxBufferSizeBytes = 8192;         // バッファ上限（byte）
    private static readonly object _lock = new();
    private static RingBuffer<byte[]>? _ring;
    private static string _lastLogDate = DateTime.Now.ToString("yyyy-MM-dd");
    private static readonly string LogDirectory;

    static LogWriter()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        LogDirectory = Path.Combine(baseDir, "log");
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>初期化（Form1 の起動時に 1 回呼び出す）</summary>
    public static void Init()
    {
        lock (_lock)
            _ring = new RingBuffer<byte[]>(MaxBufferSizeBytes / 16); // エントリ数
    }

    /// <summary>ログ追加（各モジュールから呼出）</summary>
    public static void Log(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n";
        PushLine(line);

        // 日付変更 → フラッシュ＋ファイル分割
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (today != _lastLogDate)
        {
            Flush();
            _lastLogDate = today;
        }

        // byte 数チェック → フラッシュ
        if (GetTotalBytes() + Encoding.UTF8.GetByteCount(line) > FlushThresholdBytes)
        {
            Flush();
        }
    }

    /// <summary>エラーログ追加</summary>
    public static void LogError(string msg) => Log("ERROR: " + msg);

    /// <summary>アプリ終了時に残留バッファをフラッシュ</summary>
    public static void Shutdown() => Flush();

    private static void PushLine(string line)
    {
        if (_ring == null) return;

        lock (_lock)
            _ring.Push(Encoding.UTF8.GetBytes(line));
    }

    private static int GetTotalBytes()
    {
        if (_ring == null) return 0;

        // RingBuffer の Length は byte 数を返す（byte[] エントリを合計）
        int total = 0;
        foreach (var chunk in _ring)
            total += chunk.Length;

        return total;
    }

    private static void Flush()
    {
        if (_ring == null) return;

        StringBuilder sb = new();
        string logText;
        int totalBytes;

        lock (_lock)
        {
            // RingBuffer から全エントリを文字列に変換して結合
            foreach (var chunk in _ring)
                sb.Append(Encoding.UTF8.GetString(chunk));

            if (sb.Length == 0) return;
            logText = sb.ToString();
            totalBytes = Encoding.UTF8.GetByteCount(logText);

            // バッファのクリア（RingBuffer のエントリを空に）
            _ring.Clear();

            // 4KB を超える場合は直近の改行位置で切り出し
            if (totalBytes > FlushThresholdBytes)
            {
                int cutAt = logText.LastIndexOf('\n') + 1;
                if (cutAt == 0) cutAt = logText.Length - 1; // フラグメント → とりあえず truncation
                logText = logText.Substring(0, cutAt);
            }
        }

        if (logText.Length == 0) return;

        // ファイル名: app-live.{YYYY-MM-DD-HH-mm-ss}.log
        string filename = $"app-live.{DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")}.log";
        string filepath = Path.Combine(LogDirectory, filename);

        try
        {
            File.AppendAllText(filepath, logText, Encoding.UTF8);

            // 最新ログを app-live.log にコピー（AI 読込用）
            string latestPath = Path.Combine(LogDirectory, "app-live.log");
            File.Copy(filepath, latestPath, overwrite: true);
        }
        catch { /* ログの書き出し失敗は不可逆エラーではないので無視 */ }
    }
}
