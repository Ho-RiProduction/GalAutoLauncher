using System.Text;
using GalAutoLauncher;

namespace GalAutoLauncher.UiSmoke;

/// <summary>
/// 在 STA 线程上构造并短暂显示每个窗体，验证没有布局 / 资源异常。
/// </summary>
internal static class Program
{
    private static int _failures;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var sandbox = Path.Combine(
            Path.GetTempPath(),
            "GalAL-UiSmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        Environment.SetEnvironmentVariable("GALAL_DATA_DIR", sandbox);

        JsonStore.Save(AppInfo.ConfigPath, new AppConfig
        {
            ScanPath = sandbox,
            TriggerProcesses = new[] { "aces", "WarThunder" },
        });
        JsonStore.Save(AppInfo.GamesPath, new List<GameEntry>
        {
            new() { Path = Path.Combine(sandbox, "ghost.exe"), Engine = GameEngine.Generic, Name = "Ghost" },
        });
        JsonStore.Save(AppInfo.StatePath, new AppState());

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        TrayApplicationContext? context = null;
        try
        {
            context = new TrayApplicationContext(new[] { "--tray" });
            Check("TrayApplicationContext 构造成功", true);
        }
        catch (Exception ex)
        {
            Check($"TrayApplicationContext 构造失败：{ex.Message}", false);
            return 1;
        }

        var configManager = context.ConfigManager;
        var library = context.Library;

        Exercise("MainForm", () => new MainForm(context));
        Exercise("SettingsForm", () => new SettingsForm(configManager, () => { }));
        Exercise("GameListForm", () => new GameListForm(library, configManager));

        context.Dispose();

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "UI 冒烟测试全部通过" : $"存在 {_failures} 个失败");
        return _failures == 0 ? 0 : 1;
    }

    private static void Exercise(string name, Func<Form> factory)
    {
        try
        {
            using var form = factory();
            form.CreateControl();
            form.Show();
            Application.DoEvents();
            form.Hide();
            Check($"{name} 构造 / 显示成功", true);
        }
        catch (Exception ex)
        {
            Check($"{name} 失败：{ex.GetType().Name}: {ex.Message}", false);
        }
    }

    private static void Check(string name, bool condition)
    {
        Console.WriteLine(condition ? $"  [PASS] {name}" : $"  [FAIL] {name}");
        if (!condition)
        {
            _failures++;
        }
    }
}