using System.Drawing;
using System.Windows.Forms;

namespace GalAutoLauncher;

/// <summary>
/// 主界面：显示监听状态、游戏数量与常用操作。
/// </summary>
internal sealed class MainForm : Form
{
    private readonly TrayApplicationContext _context;

    private readonly Label _statusLabel = new();
    private readonly Label _gamesLabel = new();
    private readonly Label _detectionLabel = new();
    private readonly Label _autoStartLabel = new();
    private readonly Label _scanPathLabel = new();
    private readonly ListBox _logList = new();
    private readonly System.Windows.Forms.Timer _timer;

    public MainForm(TrayApplicationContext context)
    {
        _context = context;

        Text = $"{AppInfo.ProductName} v{AppInfo.Version}";
        Icon = IconProvider.GetAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(600, 480);
        Size = new Size(660, 540);
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildLayout();

        Log.EntryWritten += OnLogEntry;
        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => RefreshSummary();
        _timer.Start();

        RefreshSummary();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ---- 状态区 ----
        var statusBox = new GroupBox
        {
            Text = "运行状态",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        var statusGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 5,
        };
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(statusGrid, 0, "当前状态：", _statusLabel);
        AddRow(statusGrid, 1, "检测方式：", _detectionLabel);
        AddRow(statusGrid, 2, "发现 Galgame：", _gamesLabel);
        AddRow(statusGrid, 3, "开机自启：", _autoStartLabel);

        statusBox.Controls.Add(statusGrid);
        root.Controls.Add(statusBox, 0, 0);

        // ---- 操作区 ----
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 8),
            WrapContents = true,
        };

        buttonPanel.Controls.Add(MakeButton("重新扫描 Galgame", (_, _) => _context.ScanGamesCommand()));
        buttonPanel.Controls.Add(MakeButton("扫描所有盘", (_, _) => _context.ScanAllDrivesCommand()));
        buttonPanel.Controls.Add(MakeButton("测试随机启动", (_, _) => _context.TestLaunchCommand()));
        buttonPanel.Controls.Add(MakeButton("游戏列表", (_, _) => _context.ShowGameListCommand()));
        buttonPanel.Controls.Add(MakeButton("设置", (_, _) => _context.ShowSettingsCommand()));
        buttonPanel.Controls.Add(MakeButton("切换开机自启", (_, _) => _context.ToggleAutoStartCommand()));
        root.Controls.Add(buttonPanel, 0, 1);

        // ---- 日志区 ----
        var logBox = new GroupBox
        {
            Text = "最近日志",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };

        _logList.Dock = DockStyle.Fill;
        _logList.Font = new Font("Consolas", 9F);
        _logList.IntegralHeight = false;
        logBox.Controls.Add(_logList);
        root.Controls.Add(logBox, 0, 2);

        Controls.Add(root);

        var statusStrip = new StatusStrip();
        var pathItem = new ToolStripStatusLabel($"数据目录：{AppInfo.DataDirectory}")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        statusStrip.Items.Add(pathItem);
        _scanPathLabel.Visible = false;
        Controls.Add(statusStrip);
    }

    private static void AddRow(TableLayoutPanel grid, int row, string caption, Label value)
    {
        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(0x55, 0x55, 0x55),
            Margin = new Padding(0, 4, 6, 4),
        };

        value.AutoSize = true;
        value.Anchor = AnchorStyles.Left;
        value.Margin = new Padding(0, 4, 0, 4);

        grid.Controls.Add(captionLabel, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private static Button MakeButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 8),
        };
        button.Click += handler;
        return button;
    }

    public void RefreshSummary()
    {
        var config = _context.ConfigManager.Config;
        var state = _context.ConfigManager.State;

        _statusLabel.Text = _context.Engine.LastMessage;
        _statusLabel.ForeColor = _context.Engine.Status switch
        {
            EngineStatus.Waiting => Color.FromArgb(0x1B, 0x7F, 0x3B),
            EngineStatus.Delay or EngineStatus.Launching => Color.FromArgb(0xB5, 0x6A, 0x00),
            _ => Color.FromArgb(0x66, 0x66, 0x66),
        };

        _detectionLabel.Text = config.TriggerAnyGame
            ? $"{_context.Engine.DetectionMode}（{string.Join(", ", config.TriggerProcesses)} + 任意游戏）"
            : $"{_context.Engine.DetectionMode}（{string.Join(", ", config.TriggerProcesses)}）";
        _gamesLabel.Text = $"{_context.Library.EligibleCount} 个可用 / 共 {_context.Library.Count} 个";
        _autoStartLabel.Text = StartupManager.IsEnabled() ? "已开启" : "未开启";
        _scanPathLabel.Text = config.ScanPath;

        var lastScan = state.LastScanUtc is { } when
            ? when.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "从未";
        _gamesLabel.Text += $"　（上次扫描：{lastScan}）";
    }

    public void UpdateStatus(EngineStatus status, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateStatus(status, message)));
            return;
        }

        RefreshSummary();
    }

    private void OnLogEntry(LogEntry entry)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => OnLogEntry(entry)));
            }
            catch
            {
                // 窗体正在关闭
            }

            return;
        }

        _logList.Items.Add(entry.ToString());
        while (_logList.Items.Count > 500)
        {
            _logList.Items.RemoveAt(0);
        }

        _logList.TopIndex = _logList.Items.Count - 1;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            // 关闭窗口时最小化到托盘而不是退出。
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Log.EntryWritten -= OnLogEntry;
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
