using System.Drawing;
using System.Windows.Forms;

namespace GalAutoLauncher;

/// <summary>
/// 设置窗口：编辑 config.json 中的主要选项。
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly Action _onSaved;

    private readonly TextBox _scanPath = new();
    private readonly TextBox _processes = new();
    private readonly NumericUpDown _delaySeconds = new();
    private readonly NumericUpDown _jitterSeconds = new();
    private readonly NumericUpDown _pollSeconds = new();
    private readonly NumericUpDown _maxDepth = new();
    private readonly NumericUpDown _probability = new();
    private readonly CheckBox _randomLaunch = new();
    private readonly CheckBox _detectUnity = new();
    private readonly CheckBox _detectGeneric = new();
    private readonly NumericUpDown _startupGrace = new();
    private readonly CheckBox _minimizeToTray = new();
    private readonly CheckBox _showNotifications = new();
    private readonly NumericUpDown _logRetention = new();
    private readonly TextBox _excluded = new();
    private readonly CheckBox _triggerAnyGame = new();
    private readonly CheckBox _scanAllDrives = new();
    private readonly TextBox _anyGameBlacklist = new();

    public SettingsForm(ConfigManager configManager, Action onSaved)
    {
        _configManager = configManager;
        _onSaved = onSaved;

        Text = "设置";
        Icon = IconProvider.GetAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 660);

        BuildLayout();
        LoadFromConfig();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 21,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // 扫描目录 + 浏览按钮
        _scanPath.Dock = DockStyle.Fill;
        var pathPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathPanel.Controls.Add(_scanPath, 0, 0);
        var browse = new Button { Text = "浏览…", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        browse.Click += (_, _) => BrowseScanPath();
        pathPanel.Controls.Add(browse, 1, 0);
        AddRow(grid, row++, "Galgame 扫描目录：", pathPanel);

        AddRow(grid, row++, "监听进程（逗号分隔）：", _processes);
        AddRow(grid, row++, "启动延迟（秒）：", Numeric(_delaySeconds, 0, 600));
        AddRow(grid, row++, "随机额外延迟（0~N 秒）：", Numeric(_jitterSeconds, 0, 120));
        AddRow(grid, row++, "轮询间隔（秒）：", Numeric(_pollSeconds, 1, 60));
        AddRow(grid, row++, "扫描最大深度：", Numeric(_maxDepth, 1, 24));
        AddRow(grid, row++, "触发概率（%）：", Numeric(_probability, 0, 100));
        AddRow(grid, row++, "开机保护期（秒）：", Numeric(_startupGrace, 0, 3600));

        _randomLaunch.Text = "随机选择 Galgame（关闭则按顺序轮换）";
        _randomLaunch.AutoSize = true;
        AddRow(grid, row++, string.Empty, _randomLaunch);

        _detectUnity.Text = "扫描 Unity 视觉小说（保守识别：中文/日文主程序名或含 Save 目录）";
        _detectUnity.AutoSize = true;
        AddRow(grid, row++, string.Empty, _detectUnity);

        _detectGeneric.Text = "扫描通用入口 game.exe/start.exe（误报率高，不建议）";
        _detectGeneric.AutoSize = true;
        AddRow(grid, row++, string.Empty, _detectGeneric);

        _minimizeToTray.Text = "启动时最小化到托盘";
        _minimizeToTray.AutoSize = true;
        AddRow(grid, row++, string.Empty, _minimizeToTray);

        _showNotifications.Text = "显示气泡通知";
        _showNotifications.AutoSize = true;
        AddRow(grid, row++, string.Empty, _showNotifications);

        AddRow(grid, row++, "日志保留（天）：", Numeric(_logRetention, 1, 365));

        _triggerAnyGame.Text = "打开任意非 Galgame 游戏时也触发（保留 War Thunder 监听）";
        _triggerAnyGame.AutoSize = true;
        AddRow(grid, row++, string.Empty, _triggerAnyGame);

        _scanAllDrives.Text = "扫描时遍历所有固定磁盘（跳过 Windows / Program Files 等系统目录）";
        _scanAllDrives.AutoSize = true;
        AddRow(grid, row++, string.Empty, _scanAllDrives);

        _anyGameBlacklist.Multiline = true;
        _anyGameBlacklist.Height = 70;
        _anyGameBlacklist.Dock = DockStyle.Fill;
        _anyGameBlacklist.ScrollBars = ScrollBars.Vertical;
        AddRow(grid, row++, "任意游戏额外排除进程\n（每行一个，可留空）：", _anyGameBlacklist);
        AddRow(grid, row++, "排除目录（每行一个）：", _excluded);

        root.Controls.Add(grid, 0, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };

        var save = new Button { Text = "保存", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
        save.Click += (_, _) => SaveConfig();
        var cancel = new Button { Text = "取消", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
        cancel.Click += (_, _) => Close();
        var rescan = new Button { Text = "保存并重新扫描", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
        rescan.Click += (_, _) => SaveConfig(rescan: true);

        buttonPanel.Controls.Add(save);
        buttonPanel.Controls.Add(cancel);
        buttonPanel.Controls.Add(rescan);

        root.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(root);

        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel grid, int row, string caption, Control control)
    {
        if (!string.IsNullOrEmpty(caption))
        {
            grid.Controls.Add(new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 7, 6, 7),
            }, 0, row);
        }

        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(control, 1, row);
    }

    private static NumericUpDown Numeric(NumericUpDown control, decimal min, decimal max)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Width = 120;
        control.Anchor = AnchorStyles.Left;
        return control;
    }

    private void LoadFromConfig()
    {
        var config = _configManager.Config;
        _scanPath.Text = config.ScanPath;
        _processes.Text = string.Join(", ", config.TriggerProcesses);
        _delaySeconds.Value = Clamp(config.LaunchDelaySeconds, _delaySeconds);
        _jitterSeconds.Value = Clamp(config.LaunchDelayJitterSeconds, _jitterSeconds);
        _pollSeconds.Value = Clamp(config.PollIntervalSeconds, _pollSeconds);
        _maxDepth.Value = Clamp(config.MaxScanDepth, _maxDepth);
        _probability.Value = Clamp(config.LaunchProbabilityPercent, _probability);
        _startupGrace.Value = Clamp(config.StartupGraceSeconds, _startupGrace);
        _randomLaunch.Checked = config.RandomLaunch;
        _detectUnity.Checked = config.DetectUnity;
        _detectGeneric.Checked = config.DetectGeneric;
        _minimizeToTray.Checked = config.MinimizeToTrayOnStart;
        _showNotifications.Checked = config.ShowNotifications;
        _logRetention.Value = Clamp(config.LogRetentionDays, _logRetention);
        _excluded.Text = string.Join(Environment.NewLine, config.ExcludedDirectories);
        _triggerAnyGame.Checked = config.TriggerAnyGame;
        _scanAllDrives.Checked = config.ScanAllDrives;
        _anyGameBlacklist.Text = string.Join(Environment.NewLine, config.AnyGameProcessBlacklist);
    }

    private static decimal Clamp(int value, NumericUpDown control) =>
        Math.Min(Math.Max(value, control.Minimum), control.Maximum);

    private void BrowseScanPath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 Galgame 扫描目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (Directory.Exists(_scanPath.Text))
        {
            dialog.SelectedPath = _scanPath.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _scanPath.Text = dialog.SelectedPath;
        }
    }

    private void SaveConfig(bool rescan = false)
    {
        if (!string.IsNullOrWhiteSpace(_scanPath.Text) && !Directory.Exists(_scanPath.Text))
        {
            var choice = MessageBox.Show(
                this,
                $"目录不存在：\n{_scanPath.Text}\n\n仍然保存吗？",
                "确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes)
            {
                return;
            }
        }

        _configManager.Update(config =>
        {
            config.ScanPath = _scanPath.Text.Trim();
            config.TriggerProcesses = _processes.Text
                .Split(new[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            config.LaunchDelaySeconds = (int)_delaySeconds.Value;
            config.LaunchDelayJitterSeconds = (int)_jitterSeconds.Value;
            // 冷却已取消：每次检测到目标进程都会尝试启动。
            config.CooldownMinutes = 0;
            config.PollIntervalSeconds = (int)_pollSeconds.Value;
            config.MaxScanDepth = (int)_maxDepth.Value;
            config.LaunchProbabilityPercent = (int)_probability.Value;
            config.StartupGraceSeconds = (int)_startupGrace.Value;
            config.RandomLaunch = _randomLaunch.Checked;
            config.DetectUnity = _detectUnity.Checked;
            config.DetectGeneric = _detectGeneric.Checked;
            config.MinimizeToTrayOnStart = _minimizeToTray.Checked;
            config.ShowNotifications = _showNotifications.Checked;
            config.LogRetentionDays = (int)_logRetention.Value;
            config.ExcludedDirectories = _excluded.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            config.TriggerAnyGame = _triggerAnyGame.Checked;
            config.ScanAllDrives = _scanAllDrives.Checked;
            config.AnyGameProcessBlacklist = _anyGameBlacklist.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        });

        Log.Initialize(AppInfo.LogDirectory, _configManager.Config.EffectiveLogRetentionDays);
        _onSaved?.Invoke();

        if (rescan)
        {
            Close();
            return;
        }

        Close();
    }
}
