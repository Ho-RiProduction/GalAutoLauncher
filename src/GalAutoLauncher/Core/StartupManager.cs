using Microsoft.Win32;

namespace GalAutoLauncher;

/// <summary>
/// 开机自启管理：读写 HKCU\...\Run 注册表项。
/// </summary>
internal static class StartupManager
{
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppInfo.RunKeyPath, false);
            var value = key?.GetValue(AppInfo.RunValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            Log.Warn($"读取开机自启状态失败：{ex.Message}");
            return false;
        }
    }

    public static string? CurrentCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppInfo.RunKeyPath, false);
            return key?.GetValue(AppInfo.RunValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool Enable(out string message)
    {
        message = string.Empty;
        try
        {
            var exe = AppInfo.ExePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                message = "无法定位程序自身路径。";
                return false;
            }

            var command = $"\"{exe}\" {AppInfo.StartupArgument}";
            using var key = Registry.CurrentUser.CreateSubKey(AppInfo.RunKeyPath, true);
            key.SetValue(AppInfo.RunValueName, command, RegistryValueKind.String);

            message = "已开启开机自启。";
            Log.Info($"开启开机自启：{command}");
            return true;
        }
        catch (Exception ex)
        {
            message = $"开启失败：{ex.Message}";
            Log.Error("开启开机自启失败", ex);
            return false;
        }
    }

    public static bool Disable(out string message)
    {
        message = string.Empty;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppInfo.RunKeyPath, true);
            if (key is null)
            {
                message = "已关闭开机自启。";
                return true;
            }

            if (key.GetValue(AppInfo.RunValueName) is not null)
            {
                key.DeleteValue(AppInfo.RunValueName, false);
                Log.Info("已关闭开机自启。");
            }

            message = "已关闭开机自启。";
            return true;
        }
        catch (Exception ex)
        {
            message = $"关闭失败：{ex.Message}";
            Log.Error("关闭开机自启失败", ex);
            return false;
        }
    }

    public static bool Toggle(bool enable, out string message) =>
        enable ? Enable(out message) : Disable(out message);
}
