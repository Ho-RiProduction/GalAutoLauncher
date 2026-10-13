using System.Drawing;
using System.Windows.Forms;

namespace GalAutoLauncher;

/// <summary>
/// 托盘应用上下文：管理 NotifyIcon、菜单、核心服务与生命周期。
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ConfigManager _configManager = new();
    private readonly GameLibrary _library;
    private readonly AutomationEngine _engine;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly bool _silentStart;

    private MainForm? _mainForm;
    private SettingsForm? _settingsForm;
    private GameListForm? _gameListForm;
    private bool _scanning;

    public TrayApplicationContext(string[] args)
    {
        _silentStart = args.Any(a =>
            string.Equals(a, AppInfo.StartupArgument, StringComparison.OrdinalIgnoreCase));

        _configManager.Load();
        _library = new GameLibrary(AppInfo.GamesPath);
        _library.Load();

        _engine = new AutomationEngine(_configManager, _library);
        _engine.StatusChanged += OnEngineStatusChanged;
        _library.Changed += _engine.RefreshLibrary;
        _engine.GameLaunched += OnGameLaunched;
        _engine.Notify += OnNotify;

        _notifyIcon = new NotifyIcon
        {
            Icon = IconProvider.GetAppIcon(),
            Text = AppInfo.ProductName,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += (_, _) => UpdateTrayText();
        _refreshTimer.Start();

        // 开机自启（--tray）时保留开机保护期；用户手动双击打开时立即监听、不做保护期等待。
        _engine.Start(applyStartupGrace: _silentStart);
        UpdateTrayText();

        if (!_silentStart && !_configManager.Config.MinimizeToTrayOnStart)
        {
            ShowMainForm();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var header = new ToolStripMenuItem(AppInfo.ProductName) { Enabled = false };
        var status = new ToolStripMenuItem("等待 War Thunder") { Name = "status", Enabled = false };
        var gamesCount = new ToolStripMenuItem("发现 Galgame：0 个") { Name = "games", Enabled = false };

        menu.Items.Add(header);
        menu.Items.Add(status);
        menu.Items.Add(gamesCount);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Create("重新扫描 Galgame", (_, _) => ScanGames()));
        menu.Items.Add(Create("扫描所有盘寻找 Galgame", (_, _) => ScanGames(allDrives: true)));
        menu.Items.Add(Create("测试随机启动", (_, _) => TestLaunch()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Create("打开主界面", (_, _) => ShowMainForm()));
        menu.Items.Add(Create("游戏列表", (_, _) => ShowGameList()));
        menu.Items.Add(Create("设置", (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());

        var autoStart = new ToolStripMenuItem("开机自启") { Name = "autostart" };
        autoStart.Click += (_, _) => ToggleAutoStart();
        menu.Items.Add(autoStart);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Create("退出", (_, _) => ExitApp()));

        menu.Opening += (_, _) => RefreshMenuState(menu);
        return menu;
    }

    private static ToolStripMenuItem Create(string text, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    private void RefreshMenuState(ContextMenuStrip menu)
    {
        if (menu.Items["status"] is ToolStripMenuItem status)
        {
            status.Text = $"状态：{_engine.LastMessage}";
        }

        if (menu.Items["games"] is ToolStripMenuItem games)
        {
            games.Text = $"发现 Galgame：{_library.EligibleCount} 个";
        }

        if (menu.Items["autostart"] is ToolStripMenuItem autoStart)
        {
            var enabled = StartupManager.IsEnabled();
            autoStart.Checked = enabled;
            autoStart.Text = enabled ? "开机自启（已开启）" : "开机自启（已关闭）";
        }
    }

    private void UpdateTrayText()
    {
        var text = $"{AppInfo.ProductName} - {_engine.LastMessage}";

        // NotifyIcon.Text 上限 63 个字符
        if (text.Length > 62)
        {
            text = text[..62];
        }

        _notifyIcon.Text = text;
    }

    private void OnEngineStatusChanged(EngineStatus status, string message)
    {
        if (_notifyIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _notifyIcon.ContextMenuStrip.BeginInvoke(new Action(() => UpdateTrayText()));
        }
        else
        {
            UpdateTrayText();
        }

        if (_mainForm is { IsDisposed: false } form)
        {
            form.UpdateStatus(status, message);
        }
    }

    private void OnGameLaunched(GameEntry entry)
    {
        if (!_configManager.Config.ShowNotifications)
        {
            return;
        }

        ShowBalloon("已启动 Galgame", $"{entry.DisplayName}\n{entry.Path}", ToolTipIcon.Info);
    }

    private void OnNotify(string message)
    {
        if (!_configManager.Config.ShowNotifications)
        {
            return;
        }

        ShowBalloon(AppInfo.ProductName, message, ToolTipIcon.Info);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, text, icon);
        }
        catch
        {
            // 忽略
        }
    }

    private void ScanGames(bool allDrives = false)
    {
        if (_scanning)
        {
            return;
        }

        var config = _configManager.Config;
        var useAllDrives = allDrives || config.ScanAllDrives;

        _scanning = true;
        ShowBalloon(
            AppInfo.ProductName,
            useAllDrives ? "开始扫描所有固定磁盘…（可能耗时较久）" : "开始扫描 Galgame…",
            ToolTipIcon.Info);

        Task.Run(() =>
        {
            try
            {
                var scanner = new GameScanner();
                var result = useAllDrives
                    ? scanner.ScanAllDrives(
                        config.EffectiveMaxScanDepth,
                        config.ExcludedDirectories,
                        config.DetectUnity,
                        config.DetectGeneric)
                    : scanner.Scan(
                        config.ScanPath,
                        config.EffectiveMaxScanDepth,
                        config.ExcludedDirectories,
                        config.DetectUnity,
                        config.DetectGeneric);

                var count = _library.Replace(result.Games);
                _library.Save();

                _configManager.State.LastScanUtc = DateTime.UtcNow;
                _configManager.State.LastScanCount = count;
                _configManager.SaveState();

                var location = useAllDrives
                    ? $"所有固定磁盘（{result.RootPaths.Count} 个）"
                    : config.ScanPath;

                BeginInvokeOnUi(() =>
                {
                    _gameListForm?.Reload();
                    _mainForm?.RefreshSummary();
                    UpdateTrayText();
                    ShowBalloon(
                        "扫描完成",
                        $"在 {location} 下发现 {count} 个 Galgame。",
                        ToolTipIcon.Info);
                });
            }
            catch (Exception ex)
            {
                Log.Error("扫描失败", ex);
                BeginInvokeOnUi(() =>
                    ShowBalloon("扫描失败", ex.Message, ToolTipIcon.Error));
            }
            finally
            {
                _scanning = false;
            }
        });
    }

    private void TestLaunch()
    {
        var entry = _configManager.Config.RandomLaunch ? _library.PickRandom() : _library.PickNext();
        if (entry is null)
        {
            ShowBalloon(
                "无法测试",
                "游戏库为空，请先扫描，或检查 config.json 中的 ScanPath。",
                ToolTipIcon.Warning);
            return;
        }

        if (GameLauncher.Launch(entry, out var message))
        {
            _library.MarkLaunched(entry.Path);
            _library.Save();
            _engine.NotifyExternalGalgameLaunch(entry);
            ShowBalloon("测试启动", message, ToolTipIcon.Info);
        }
        else
        {
            ShowBalloon("测试失败", message, ToolTipIcon.Error);
        }
    }

    private void ToggleAutoStart()
    {
        var enable = !StartupManager.IsEnabled();
        StartupManager.Toggle(enable, out var message);
        ShowBalloon(AppInfo.ProductName, message, ToolTipIcon.Info);
        if (_mainForm is { IsDisposed: false } form)
        {
            form.RefreshSummary();
        }
    }

    private void ShowMainForm()
    {
        if (_mainForm is null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(this);
        }

        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
        _mainForm.RefreshSummary();
    }

    private void ShowSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_configManager, OnConfigSaved);
        }

        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OnConfigSaved()
    {
        _engine.Reload();
        _library.Load();
        _mainForm?.RefreshSummary();
        _gameListForm?.Reload();
        UpdateTrayText();
    }

    private void ShowGameList()
    {
        if (_gameListForm is null || _gameListForm.IsDisposed)
        {
            _gameListForm = new GameListForm(_library, _configManager);
        }

        _gameListForm.Reload();
        _gameListForm.Show();
        _gameListForm.Activate();
    }

    private void BeginInvokeOnUi(Action action)
    {
        var target = _mainForm as Control;
        if (target is { IsHandleCreated: true })
        {
            target.BeginInvoke(action);
        }
        else
        {
            // 没有窗口时，NotifyIcon 所在的 UI 线程用同步上下文兜底
            action();
        }
    }

    internal void ExitApp()
    {
        _refreshTimer.Stop();
        _notifyIcon.Visible = false;

        try
        {
            _configManager.SaveState();
        }
        catch
        {
            // 忽略
        }

        _engine.Dispose();
        _notifyIcon.Dispose();

        _mainForm?.Dispose();
        _settingsForm?.Dispose();
        _gameListForm?.Dispose();

        ExitThread();
    }

    // 供窗体调用的公开包装
    public void ScanGamesCommand() => ScanGames();
    public void ScanAllDrivesCommand() => ScanGames(allDrives: true);
    public void TestLaunchCommand() => TestLaunch();
    public void ToggleAutoStartCommand() => ToggleAutoStart();
    public void ShowSettingsCommand() => ShowSettings();
    public void ShowGameListCommand() => ShowGameList();

    public ConfigManager ConfigManager => _configManager;
    public GameLibrary Library => _library;
    public AutomationEngine Engine => _engine;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _notifyIcon.Dispose();
            _engine.Dispose();
        }

        base.Dispose(disposing);
    }
}
