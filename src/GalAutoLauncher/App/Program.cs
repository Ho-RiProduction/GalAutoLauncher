using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GalAutoLauncher;

internal static class Program
{
    private const string MutexName = @"Local\GalAutoLauncher.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        var command = (args.Length > 0 ? args[0] : string.Empty).Trim().ToLowerInvariant();

        // 命令行辅助命令（无需 GUI 会话）
        switch (command)
        {
            case "install":
                return RunInstall(args);
            case "uninstall":
                return RunUninstall();
            case "scan":
                return RunScan(args);
            case "launch":
            case "test":
                return RunTestLaunch();
            case "--help":
            case "-h":
            case "help":
            case "/?":
                ShowHelp();
                return 0;
        }

        return RunTray(args);
    }

    private static int RunTray(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Log.Info("检测到已有实例在运行，本次启动退出。");
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            using var context = new TrayApplicationContext(args);
            Application.Run(context);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("程序异常退出", ex);
            MessageBox.Show(
                $"程序遇到错误并退出：\n{ex.Message}\n\n详见日志：{AppInfo.LogDirectory}",
                AppInfo.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static int RunInstall(string[] args)
    {
        AttachConsole();

        var configManager = new ConfigManager();
        configManager.Load();

        if (!StartupManager.Enable(out var message))
        {
            Console.WriteLine(message);
            return 1;
        }

        Console.WriteLine(message);
        Console.WriteLine($"程序位置：{AppInfo.ExePath}");

        // 安装后顺带启动一次（若已在运行则静默退出）。
        var startTray = args.Any(a =>
            string.Equals(a, "--start", StringComparison.OrdinalIgnoreCase));
        if (startTray)
        {
            TryStartSelfTray();
        }

        return 0;
    }

    private static int RunUninstall()
    {
        AttachConsole();

        if (!StartupManager.Disable(out var message))
        {
            Console.WriteLine(message);
            return 1;
        }

        Console.WriteLine(message);
        return 0;
    }

    private static int RunScan(string[] args)
    {
        AttachConsole();

        var configManager = new ConfigManager();
        configManager.Load();

        var library = new GameLibrary(AppInfo.GamesPath);
        library.Load();

        var allDrives = args.Any(a =>
            string.Equals(a, "--all-drives", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-a", StringComparison.OrdinalIgnoreCase)) ||
            configManager.Config.ScanAllDrives;

        var scanner = new GameScanner();
        ScanResult result;

        if (allDrives)
        {
            Console.WriteLine("正在扫描所有固定磁盘（跳过系统目录）…");
            result = scanner.ScanAllDrives(
                configManager.Config.EffectiveMaxScanDepth,
                configManager.Config.ExcludedDirectories,
                configManager.Config.DetectUnity,
                configManager.Config.DetectGeneric);
            Console.WriteLine($"扫描范围：{string.Join("；", result.RootPaths)}");
        }
        else
        {
            Console.WriteLine($"正在扫描：{configManager.Config.ScanPath}");
            result = scanner.Scan(
                configManager.Config.ScanPath,
                configManager.Config.EffectiveMaxScanDepth,
                configManager.Config.ExcludedDirectories,
                configManager.Config.DetectUnity,
                configManager.Config.DetectGeneric);
        }

        var count = library.Replace(result.Games);
        library.Save();

        configManager.State.LastScanUtc = DateTime.UtcNow;
        configManager.State.LastScanCount = count;
        configManager.SaveState();

        Console.WriteLine($"扫描完成，共发现 {count} 个 Galgame。");
        foreach (var game in result.Games)
        {
            Console.WriteLine($"  [{game.Engine}] {game.Path}");
        }

        return 0;
    }

    private static int RunTestLaunch()
    {
        AttachConsole();

        var library = new GameLibrary(AppInfo.GamesPath);
        library.Load();

        var entry = library.PickRandom();
        if (entry is null)
        {
            Console.WriteLine("游戏库为空，请先执行 scan 或使用托盘菜单扫描。");
            return 1;
        }

        if (GameLauncher.Launch(entry, out var message))
        {
            library.MarkLaunched(entry.Path);
            library.Save();
            Console.WriteLine(message);
            return 0;
        }

        Console.WriteLine(message);
        return 1;
    }

    private static void TryStartSelfTray()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = AppInfo.ExePath,
                Arguments = AppInfo.StartupArgument,
                UseShellExecute = true,
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log.Warn($"启动托盘进程失败：{ex.Message}");
        }
    }

    private static void ShowHelp()
    {
        AttachConsole();
        Console.WriteLine($"{AppInfo.ProductName} v{AppInfo.Version}");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  GalAutoLauncher.exe              启动托盘助手");
        Console.WriteLine("  GalAutoLauncher.exe --tray       静默启动到托盘（自启使用）");
        Console.WriteLine("  GalAutoLauncher.exe install      开启开机自启 [--start]");
        Console.WriteLine("  GalAutoLauncher.exe uninstall    关闭开机自启");
        Console.WriteLine("  GalAutoLauncher.exe scan         扫描 Galgame 并写入 games.json");
        Console.WriteLine("  GalAutoLauncher.exe scan --all-drives  扫描所有固定磁盘");
        Console.WriteLine("  GalAutoLauncher.exe launch       测试随机启动一个 Galgame");
        Console.WriteLine();
        Console.WriteLine($"数据目录：{AppInfo.DataDirectory}");
    }

    // ---- 控制台输出辅助：WinExe 默认无控制台，附加到父进程控制台以便打印 ----
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private static void AttachConsole()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            // 没有可附加的父控制台时（例如双击），不弹黑窗。
        }
    }
}
