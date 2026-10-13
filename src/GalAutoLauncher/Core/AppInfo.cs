using System.Reflection;

namespace GalAutoLauncher;

/// <summary>
/// 全局常量与路径解析。数据默认与 exe 同目录（便携模式），
/// 若该目录不可写则回退到 %APPDATA%\GalAutoLauncher。
/// </summary>
internal static class AppInfo
{
    public const string ProductName = "GalAutoLauncher";
    public const string RunValueName = "GalAutoLauncher";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>开机自启时附加的参数，用于静默进入托盘。</summary>
    public const string StartupArgument = "--tray";

    private static readonly Lazy<string> LazyDataDirectory = new(ResolveDataDirectory);

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static string ExePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string DataDirectory => LazyDataDirectory.Value;

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");
    public static string GamesPath => Path.Combine(DataDirectory, "games.json");
    public static string StatePath => Path.Combine(DataDirectory, "state.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    private static string ResolveDataDirectory()
    {
        var custom = Environment.GetEnvironmentVariable("GALAL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try
            {
                Directory.CreateDirectory(custom);
                return custom;
            }
            catch
            {
                // 忽略，继续使用默认逻辑
            }
        }

        var exeDir = BaseDirectory;
        if (IsWritable(exeDir))
        {
            return exeDir;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductName);
        Directory.CreateDirectory(appData);
        return appData;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".galal_write_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
