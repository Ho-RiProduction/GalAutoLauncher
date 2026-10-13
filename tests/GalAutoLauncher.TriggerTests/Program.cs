using System.Diagnostics;
using System.Text;
using GalAutoLauncher;

namespace GalAutoLauncher.TriggerTests;

/// <summary>
/// 端到端集成测试：
///   阶段 1（主功能回归）：启动 aces.exe 模拟 War Thunder，应触发一次 Galgame 启动，
///                        且【无冷却】——结束后再次启动 aces.exe 仍会触发。
///   阶段 1b（单实例）  ：某个 Galgame 已在运行时，不会再启动它的第二个实例。
///   阶段 2（任意游戏） ：打开任意“非 Galgame 游戏”也应触发 Galgame；
///                        并验证 Galgame 启动后的抑制窗口内不会递归触发。
/// </summary>
internal static class Program
{
    private const string WindowHelperArgument = "--window-helper";

    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 作为“假游戏”窗口辅助进程被拉起时，只显示一个可见窗口并常驻。
        if (args.Any(a => string.Equals(a, WindowHelperArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return RunWindowHelper();
        }

        var sandbox = Path.Combine(
            Path.GetTempPath(),
            "GalAL-TriggerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);

        // 让 AppInfo 把 config/games/state 写到沙箱（必须在首次访问 AppInfo 前设置）。
        Environment.SetEnvironmentVariable("GALAL_DATA_DIR", sandbox);
        Console.WriteLine($"沙箱目录：{sandbox}");

        var gamesDir = Path.Combine(sandbox, "Games");
        Directory.CreateDirectory(gamesDir);

        // ================= 阶段 1：战争雷霆主功能回归 + 无冷却 =================
        var marker = Path.Combine(sandbox, "launched.txt");
        var fakeGame = CreateFakeGame(gamesDir, marker, "fakegal.cmd");

        JsonStore.Save(AppInfo.ConfigPath, new AppConfig
        {
            ScanPath = gamesDir,
            TriggerProcesses = new[] { "aces", "WarThunder" },
            LaunchDelaySeconds = 1,
            LaunchDelayJitterSeconds = 0,
            RandomLaunch = true,
            // 故意保留一个很大的冷却值，验证新版代码会忽略它（无冷却）。
            CooldownMinutes = 60,
            PollIntervalSeconds = 1,
            LaunchProbabilityPercent = 100,
            StartupGraceSeconds = 0,
            ShowNotifications = false,
            // 阶段 1 专门验证“战雷精确监听”这条主链路，先关闭任意游戏触发。
            TriggerAnyGame = false,
        });

        JsonStore.Save(AppInfo.GamesPath, new List<GameEntry>
        {
            new() { Path = fakeGame, Engine = GameEngine.Generic, Name = "FakeGal" },
        });

        JsonStore.Save(AppInfo.StatePath, new AppState());

        var manager = new ConfigManager();
        manager.Load();
        Check("配置按沙箱路径加载", manager.Config.ScanPath == gamesDir);

        var library = new GameLibrary(AppInfo.GamesPath);
        library.Load();
        Check("游戏库加载 1 个条目", library.Count == 1);

        using (var engine = new AutomationEngine(manager, library))
        {
            GameEntry? launched = null;
            engine.GameLaunched += entry => launched = entry;

            engine.Start();
            Console.WriteLine($"检测模式：{engine.DetectionMode}");
            Thread.Sleep(1500); // 等待监听就绪

            // ---- 触发 1：应启动 Galgame ----
            Console.WriteLine("[阶段1] 启动模拟 War Thunder（aces.exe）…");
            var aces = StartFakeProcess(sandbox, "aces", 8);

            var ok1 = WaitFor(() => File.Exists(marker), TimeSpan.FromSeconds(20));
            Check("检测到 aces.exe 后自动启动了 Galgame", ok1);
            Check("触发了 GameLaunched 事件", launched is not null);
            Check("state 记录了触发时间", manager.State.LastTriggerUtc is not null);
            aces?.Kill(true);

            // ---- 触发 2：无冷却，结束后再次启动战雷应再次触发 ----
            var countBefore = ReadLaunchCount(marker);
            Thread.Sleep(500);
            Console.WriteLine("[阶段1] 无冷却：再次启动 aces.exe…");
            var aces2 = StartFakeProcess(sandbox, "aces", 8);
            var ok2 = WaitFor(() => ReadLaunchCount(marker) > countBefore, TimeSpan.FromSeconds(20));
            Check("无冷却：再次启动战雷仍会启动 Galgame", ok2);
            aces2?.Kill(true);

            engine.Stop();
        }

        // ================= 阶段 1b：同一个 Galgame 只允许一个实例 =================
        Console.WriteLine();
        Console.WriteLine("[阶段1b] 某个 Galgame 已在运行时，不应再启动它的第二个实例…");

        var markerA = Path.Combine(sandbox, "single-a.txt");
        var markerB = Path.Combine(sandbox, "single-b.txt");
        var gameA = CreateFakeGame(gamesDir, markerA, "singleA.cmd");
        var gameB = CreateFakeGame(gamesDir, markerB, "singleB.cmd");

        // 预先把一个“正在运行”的进程命名为 singleA，模拟 Galgame A 已在运行。
        var runningA = StartNamedFakeProcess(sandbox, "singleA", 40);
        Check("成功预启动模拟运行中的 Galgame A", runningA is not null);

        JsonStore.Save(AppInfo.ConfigPath, new AppConfig
        {
            ScanPath = gamesDir,
            TriggerProcesses = new[] { "aces", "WarThunder" },
            LaunchDelaySeconds = 1,
            LaunchDelayJitterSeconds = 0,
            RandomLaunch = true,
            CooldownMinutes = 0,
            PollIntervalSeconds = 1,
            LaunchProbabilityPercent = 100,
            StartupGraceSeconds = 0,
            ShowNotifications = false,
            TriggerAnyGame = false,
        });
        JsonStore.Save(AppInfo.GamesPath, new List<GameEntry>
        {
            new() { Path = gameA, Engine = GameEngine.Generic, Name = "SingleA" },
            new() { Path = gameB, Engine = GameEngine.Generic, Name = "SingleB" },
        });
        JsonStore.Save(AppInfo.StatePath, new AppState());

        var managerSingle = new ConfigManager();
        managerSingle.Load();
        var librarySingle = new GameLibrary(AppInfo.GamesPath);
        librarySingle.Load();

        using (var engineSingle = new AutomationEngine(managerSingle, librarySingle))
        {
            engineSingle.Start();
            Thread.Sleep(1500);

            var aces3 = StartFakeProcess(sandbox, "aces", 8);
            var launchedB = WaitFor(() => File.Exists(markerB), TimeSpan.FromSeconds(20));
            aces3?.Kill(true);

            Check("已运行的 Galgame 被跳过，改启动另一个未运行的游戏", launchedB);
            Check("没有为已在运行的 Galgame 启动第二个实例", !File.Exists(markerA));
            engineSingle.Stop();
        }

        runningA?.Kill(true);

        // ================= 阶段 2：任意非 Galgame 游戏触发 =================
        Console.WriteLine();
        Console.WriteLine("[阶段2] 打开任意非 Galgame 游戏也应触发 Galgame…");

        var anyMarker = Path.Combine(sandbox, "any-launched.txt");
        var anyGame = CreateFakeGame(gamesDir, anyMarker, "anygal.cmd");

        JsonStore.Save(AppInfo.ConfigPath, new AppConfig
        {
            ScanPath = gamesDir,
            TriggerProcesses = new[] { "aces", "WarThunder" },
            LaunchDelaySeconds = 1,
            LaunchDelayJitterSeconds = 0,
            RandomLaunch = true,
            // 冷却设为 0，让“抑制窗口”成为唯一可能挡住第二次触发的因素，便于单独验证递归防护。
            CooldownMinutes = 0,
            PollIntervalSeconds = 1,
            LaunchProbabilityPercent = 100,
            StartupGraceSeconds = 0,
            ShowNotifications = false,
            TriggerAnyGame = true,
        });

        JsonStore.Save(AppInfo.GamesPath, new List<GameEntry>
        {
            new() { Path = anyGame, Engine = GameEngine.Generic, Name = "AnyGal" },
        });
        JsonStore.Save(AppInfo.StatePath, new AppState());

        var manager2 = new ConfigManager();
        manager2.Load();
        var library2 = new GameLibrary(AppInfo.GamesPath);
        library2.Load();

        using (var engine2 = new AutomationEngine(manager2, library2))
        {
            engine2.Start();
            Thread.Sleep(1500);

            var helper1 = StartWindowHelper();
            Check("成功启动模拟普通游戏的可见窗口进程", helper1 is not null);

            var anyOk = WaitFor(() => File.Exists(anyMarker), TimeSpan.FromSeconds(25));
            Check("识别到普通游戏窗口后自动启动了 Galgame", anyOk);

            // ---- 递归防护：Galgame 启动后的抑制窗口内，不应再次触发 ----
            var anyCountBefore = ReadLaunchCount(anyMarker);
            var helper2 = StartWindowHelper();
            Thread.Sleep(8000);
            var anyCountAfter = ReadLaunchCount(anyMarker);
            Check("Galgame 启动后的抑制窗口内不会递归触发", anyCountAfter == anyCountBefore);

            helper1?.Kill(true);
            helper2?.Kill(true);
            engine2.Stop();
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "全部测试通过" : $"存在 {_failures} 个失败");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>以一个可见窗口进程的形式运行，模拟“非 Galgame 游戏”。</summary>
    private static int RunWindowHelper()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new Form
        {
            Text = "Fake Non-Galgame Game",
            Width = 460,
            Height = 260,
            StartPosition = FormStartPosition.CenterScreen,
        };
        Application.Run(form);
        return 0;
    }

    private static Process? StartWindowHelper()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) ||
            Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            exe = Path.Combine(AppContext.BaseDirectory, "GalAutoLauncher.TriggerTests.exe");
        }

        if (!File.Exists(exe))
        {
            Console.WriteLine($"  （找不到测试辅助程序：{exe}）");
            return null;
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = WindowHelperArgument,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static string CreateFakeGame(string directory, string marker, string fileName)
    {
        // Windows 下 GameLauncher 使用 UseShellExecute=true，可直接启动 .cmd。
        var cmd = Path.Combine(directory, fileName);
        var escaped = marker.Replace("%", "%%");
        File.WriteAllText(
            cmd,
            "@echo off\r\necho launch>>\"" + escaped + "\"\r\n",
            new UTF8Encoding(false));
        return cmd;
    }

    private static int ReadLaunchCount(string marker)
    {
        if (!File.Exists(marker))
        {
            return 0;
        }

        try
        {
            return File.ReadAllLines(marker).Count(line => line.Contains("launch"));
        }
        catch
        {
            return 0;
        }
    }

    private static Process? StartFakeProcess(string sandbox, string processName, int seconds)
        => StartNamedFakeProcess(sandbox, processName, seconds);

    private static Process? StartNamedFakeProcess(string sandbox, string processName, int seconds)
    {
        var source = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "PING.EXE");

        var target = Path.Combine(sandbox, processName + ".exe");
        if (!File.Exists(target))
        {
            File.Copy(source, target, true);
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = target,
            Arguments = $"-n {seconds} 127.0.0.1",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        });
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(200);
        }

        return condition();
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            Console.WriteLine($"  [FAIL] {name}");
            _failures++;
        }
    }
}