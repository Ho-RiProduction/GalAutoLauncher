using System.Diagnostics;
using System.Text;
using GalAutoLauncher;

namespace GalAutoLauncher.BootTests;

/// <summary>
/// 验证“开机自启”不会误启动 Galgame：
///   场景 A：战雷进程在助手启动之前就已存在（开机残留 / 更新器）
///           -> 助手启动后必须【不】启动任何 Galgame。
///   场景 B：助手已在运行，之后用户新启动战雷
///           -> 必须启动 Galgame（对照，证明监听确实工作）。
///   场景 C：开机自启只是监听，绝不主动扫描或主动启动。
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var sandbox = Path.Combine(Path.GetTempPath(), "GalAL-Boot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        Environment.SetEnvironmentVariable("GALAL_DATA_DIR", sandbox);

        var gamesDir = Path.Combine(sandbox, "Games");
        Directory.CreateDirectory(gamesDir);

        var marker = Path.Combine(sandbox, "launched.txt");
        var fakeGame = Path.Combine(gamesDir, "fakegal.cmd");
        File.WriteAllText(fakeGame,
            "@echo off\r\necho launch>>\"" + marker.Replace("%", "%%") + "\"\r\n",
            new UTF8Encoding(false));

        JsonStore.Save(AppInfo.ConfigPath, new AppConfig
        {
            ScanPath = gamesDir,
            TriggerProcesses = new[] { "aces", "WarThunder" },
            LaunchDelaySeconds = 1,
            RandomLaunch = true,
            CooldownMinutes = 60,
            PollIntervalSeconds = 1,
            LaunchProbabilityPercent = 100,
            StartupGraceSeconds = 0,
            ShowNotifications = false,
        });
        JsonStore.Save(AppInfo.GamesPath, new List<GameEntry>
        {
            new() { Path = fakeGame, Engine = GameEngine.Generic, Name = "FakeGal" },
        });
        JsonStore.Save(AppInfo.StatePath, new AppState());

        // ================= 场景 A：战雷先于助手存在 =================
        Console.WriteLine("[场景 A] 先启动 aces.exe（模拟开机残留/更新器），再启动助手监听…");
        var stale = StartFakeProcess(sandbox, "aces", 10);
        Thread.Sleep(1000); // 确保它已存在

        var managerA = new ConfigManager();
        managerA.Load();
        var libraryA = new GameLibrary(AppInfo.GamesPath);
        libraryA.Load();
        using var engineA = new AutomationEngine(managerA, libraryA);
        engineA.Start();

        Console.WriteLine("助手监听中，等待 8 秒…");
        Thread.Sleep(8000);
        Check("场景A：助手启动前已存在的战雷，未触发启动 Galgame", !File.Exists(marker));
        stale?.Kill(true);
        engineA.Stop();

        // ================= 场景 B：助手运行后再启动战雷 =================
        Console.WriteLine("[场景 B] 助手先运行，之后用户新启动 aces.exe…");
        var managerB = new ConfigManager();
        managerB.Load();
        var libraryB = new GameLibrary(AppInfo.GamesPath);
        libraryB.Load();
        using var engineB = new AutomationEngine(managerB, libraryB);
        engineB.Start();
        Thread.Sleep(1500);

        var fresh = StartFakeProcess(sandbox, "aces", 8);
        var triggered = WaitFor(() => File.Exists(marker), TimeSpan.FromSeconds(20));
        Check("场景B：助手运行后新启动的战雷，正确触发了 Galgame", triggered);
        fresh?.Kill(true);
        engineB.Stop();

        // ================= 场景 C：静态检查 =================
        // 仅构造托盘上下文（--tray，即开机自启的实际入口），不进行任何用户交互，
        // 确认它不会因为启动而扫描或启动 Galgame。
        Console.WriteLine("[场景 C] 以 --tray 方式启动托盘上下文（模拟开机自启），不做任何操作…");
        var before = File.Exists(marker) ? File.ReadAllLines(marker).Length : 0;
        var ctx = new TrayApplicationContext(new[] { "--tray" });
        Thread.Sleep(3000);
        var after = File.Exists(marker) ? File.ReadAllLines(marker).Length : 0;
        Check("场景C：托盘自启后未自动启动任何 Galgame", after == before);
        ctx.Dispose();

        // ================= 场景 D：开机保护期 =================
        Console.WriteLine("[场景 D] 开启 8 秒开机保护期，保护期内出现的战雷应被忽略…");
        var graceMarker = Path.Combine(sandbox, "grace.txt");
        var graceGame = Path.Combine(gamesDir, "gracegal.cmd");
        File.WriteAllText(graceGame,
            "@echo off\r\necho launch>>\"" + graceMarker.Replace("%", "%%") + "\"\r\n",
            new UTF8Encoding(false));

        JsonStore.Save(AppInfo.ConfigPath, new AppConfig
        {
            ScanPath = gamesDir,
            TriggerProcesses = new[] { "aces", "WarThunder" },
            LaunchDelaySeconds = 1,
            RandomLaunch = true,
            CooldownMinutes = 60,
            PollIntervalSeconds = 1,
            LaunchProbabilityPercent = 100,
            StartupGraceSeconds = 8,
            ShowNotifications = false,
        });
        JsonStore.Save(AppInfo.GamesPath, new List<GameEntry>
        {
            new() { Path = graceGame, Engine = GameEngine.Generic, Name = "GraceGal" },
        });
        JsonStore.Save(AppInfo.StatePath, new AppState());

        var managerD = new ConfigManager();
        managerD.Load();
        var libraryD = new GameLibrary(AppInfo.GamesPath);
        libraryD.Load();
        using (var engineD = new AutomationEngine(managerD, libraryD))
        {
            engineD.Start();
            Thread.Sleep(500);
            var inGrace = StartFakeProcess(sandbox, "aces", 4);
            Thread.Sleep(6000); // 仍在 8 秒保护期内
            Check("场景D：保护期内出现的战雷未触发 Galgame", !File.Exists(graceMarker));
            inGrace?.Kill(true);

            Thread.Sleep(4000); // 等待保护期结束
            var afterGrace = StartFakeProcess(sandbox, "aces", 6);
            var ok = WaitFor(() => File.Exists(graceMarker), TimeSpan.FromSeconds(20));
            Check("场景D：保护期结束后新启动的战雷正常触发", ok);
            afterGrace?.Kill(true);
        }
        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "全部通过：开机自启不会启动 Galgame" : $"存在 {_failures} 个失败");
        return _failures == 0 ? 0 : 1;
    }

    private static Process? StartFakeProcess(string sandbox, string name, int seconds)
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE");
        var target = Path.Combine(sandbox, name + ".exe");
        if (!File.Exists(target)) File.Copy(source, target, true);

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
            if (condition()) return true;
            Thread.Sleep(200);
        }
        return condition();
    }

    private static void Check(string name, bool condition)
    {
        Console.WriteLine(condition ? $"  [PASS] {name}" : $"  [FAIL] {name}");
        if (!condition) _failures++;
    }
}