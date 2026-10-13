using System.Text;

namespace GalAutoLauncher;

internal enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

internal readonly record struct LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    public override string ToString() =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level.ToString().ToUpperInvariant(),-5}] {Message}";
}

/// <summary>
/// 极简线程安全文件日志，按天切分，自动清理过期文件。
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string _directory = AppInfo.LogDirectory;
    private static int _retentionDays = 14;

    public static event Action<LogEntry>? EntryWritten;

    public static void Initialize(string directory, int retentionDays = 14)
    {
        lock (Gate)
        {
            _directory = directory;
            _retentionDays = Math.Max(1, retentionDays);
        }

        CleanupOldLogs();
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(string message, Exception ex) =>
        Write(LogLevel.Error, $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, $"GalAutoLauncher-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, entry + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志失败绝不能影响主流程
        }

        try
        {
            EntryWritten?.Invoke(entry);
        }
        catch
        {
            // 订阅方异常同样忽略
        }
    }

    public static void CleanupOldLogs()
    {
        try
        {
            string directory;
            int retention;
            lock (Gate)
            {
                directory = _directory;
                retention = _retentionDays;
            }

            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTime.Now.Date.AddDays(-retention);
            foreach (var file in Directory.EnumerateFiles(directory, "GalAutoLauncher-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // 单个文件失败无所谓
                }
            }
        }
        catch
        {
            // 忽略
        }
    }
}
