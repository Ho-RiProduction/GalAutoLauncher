using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GalAutoLauncher;

/// <summary>
/// 负责在目标 exe 所在目录启动进程，并处理目录名含中文/空格等情况。
/// </summary>
internal static class GameLauncher
{
    public static bool Launch(GameEntry entry, out string message)
    {
        message = string.Empty;

        if (entry is null || string.IsNullOrWhiteSpace(entry.Path))
        {
            message = "游戏路径为空。";
            return false;
        }

        if (!File.Exists(entry.Path))
        {
            message = $"文件不存在：{entry.Path}";
            return false;
        }

        // 同一个 Galgame 同时只允许一个实例：已在运行则不再启动第二个。
        if (IsRunning(entry))
        {
            message = $"已在运行，不重复启动：{entry.DisplayName}";
            Log.Info(message);
            return false;
        }

        try
        {
            var workingDirectory = Path.GetDirectoryName(entry.Path);
            var startInfo = new ProcessStartInfo
            {
                FileName = entry.Path,
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory)
                    ? AppContext.BaseDirectory
                    : workingDirectory,
                UseShellExecute = true,
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                message = "系统未能创建进程。";
                return false;
            }

            message = $"已启动：{entry.DisplayName}";
            Log.Info($"启动 Galgame：{entry.Path} (PID={process.Id})");
            return true;
        }
        catch (Exception ex)
        {
            message = $"启动失败：{ex.Message}";
            Log.Error($"启动 Galgame 失败：{entry.Path}", ex);
            return false;
        }
    }

    /// <summary>
    /// 判断某个 Galgame 是否已经在运行。
    ///
    /// 优先按完整可执行路径比对（最准确，能区分同目录下的日文版 / 汉化版）；
    /// 当无法读取进程路径（权限不足）时，回退为按进程名比对。
    /// 注意 Windows 的 Process.ProcessName 可能被截断为 15 个字符，
    /// 因此进程名比对只在两者都较短、或前缀一致时采用。
    /// </summary>
    public static bool IsRunning(GameEntry entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Path))
        {
            return false;
        }

        string targetPath;
        try
        {
            targetPath = Path.GetFullPath(entry.Path);
        }
        catch
        {
            targetPath = entry.Path;
        }

        var targetName = Path.GetFileNameWithoutExtension(entry.Path) ?? string.Empty;
        if (targetName.Length == 0)
        {
            return false;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // 1) 路径精确比对（能区分日文版与汉化版）。
                string? processPath = null;
                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch
                {
                    // 权限不足 / 进程已退出，忽略路径比对。
                }

                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    try
                    {
                        if (string.Equals(
                                Path.GetFullPath(processPath),
                                targetPath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // 路径无法规范化，退回名称比对。
                    }
                }

                // 2) 名称回退比对。ProcessName 可能被截断到 15 字符，做双向前缀判断。
                var name = process.ProcessName;
                if (NameMatches(name, targetName))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略无法访问的进程。
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static bool NameMatches(string processName, string targetName)
    {
        if (string.Equals(processName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 进程名被截断到 15 字符时，用前缀匹配兜底（仅在较长一侧确实被截断时）。
        const int maxProcessNameLength = 15;
        if (targetName.Length > maxProcessNameLength)
        {
            return string.Equals(
                processName,
                targetName[..maxProcessNameLength],
                StringComparison.OrdinalIgnoreCase);
        }

        if (processName.Length > maxProcessNameLength)
        {
            return string.Equals(
                targetName,
                processName[..maxProcessNameLength],
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
