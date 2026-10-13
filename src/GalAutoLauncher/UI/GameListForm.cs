using System.Drawing;
using System.Windows.Forms;

namespace GalAutoLauncher;

/// <summary>
/// 游戏列表：查看扫描结果、切换黑名单、手动启动、添加/移除条目。
/// </summary>
internal sealed class GameListForm : Form
{
    private readonly GameLibrary _library;
    private readonly ConfigManager _configManager;

    private readonly ListView _list = new();
    private readonly Label _summary = new();

    public GameListForm(GameLibrary library, ConfigManager configManager)
    {
        _library = library;
        _configManager = configManager;

        Text = "Galgame 列表";
        Icon = IconProvider.GetAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 520);
        MinimumSize = new Size(680, 400);
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildLayout();
        Reload();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _summary.AutoSize = true;
        _summary.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(_summary, 0, 0);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Columns.Add("状态", 60);
        _list.Columns.Add("引擎", 80);
        _list.Columns.Add("名称", 200);
        _list.Columns.Add("路径", 420);
        _list.DoubleClick += (_, _) => LaunchSelected();
        root.Controls.Add(_list, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
        };

        buttons.Controls.Add(MakeButton("启动选中", (_, _) => LaunchSelected()));
        buttons.Controls.Add(MakeButton("加入/移出黑名单", (_, _) => ToggleBlacklist()));
        buttons.Controls.Add(MakeButton("添加程序…", (_, _) => AddProgram()));
        buttons.Controls.Add(MakeButton("移除选中", (_, _) => RemoveSelected()));
        buttons.Controls.Add(MakeButton("打开所在目录", (_, _) => OpenSelectedFolder()));
        buttons.Controls.Add(MakeButton("保存", (_, _) => Save()));

        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    private static Button MakeButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 8),
        };
        button.Click += handler;
        return button;
    }

    public void Reload()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(Reload));
            return;
        }

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var entry in _library.All.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            var exists = File.Exists(entry.Path);
            var status = entry.Blacklisted ? "已禁用" : exists ? "可用" : "缺失";

            var item = new ListViewItem(status)
            {
                Tag = entry,
                ForeColor = !exists
                    ? Color.Gray
                    : entry.Blacklisted ? Color.FromArgb(0x99, 0x33, 0x33) : Color.FromArgb(0x1B, 0x5E, 0x20),
            };
            item.SubItems.Add(entry.Engine.ToString());
            item.SubItems.Add(entry.DisplayName);
            item.SubItems.Add(entry.Path);
            _list.Items.Add(item);
        }

        _list.EndUpdate();
        _summary.Text = $"共 {_library.Count} 个条目，其中可用 {_library.EligibleCount} 个。" +
                        $"　扫描目录：{_configManager.Config.ScanPath}";
    }

    private GameEntry? SelectedEntry =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as GameEntry : null;

    private void LaunchSelected()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            return;
        }

        var ok = GameLauncher.Launch(entry, out var message);
        if (ok)
        {
            _library.MarkLaunched(entry.Path);
            _library.Save();
            Reload();
        }

        MessageBox.Show(this, message, AppInfo.ProductName,
            MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ToggleBlacklist()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            return;
        }

        _library.SetBlacklisted(entry.Path, !entry.Blacklisted);
        _library.Save();
        Reload();
    }

    private void AddProgram()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 Galgame 主程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var engine = GameEngine.Generic;
        var directory = Path.GetDirectoryName(dialog.FileName);
        if (!string.IsNullOrEmpty(directory) && GameScanner.TryDetectGame(directory, out var detected))
        {
            engine = detected.Engine;
        }

        _library.Add(new GameEntry
        {
            Path = dialog.FileName,
            Engine = engine,
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
        });
        _library.Save();
        Reload();
    }

    private void RemoveSelected()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"确定要从列表中移除吗？\n{entry.Path}\n\n（不会删除磁盘上的文件）",
            "确认移除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (choice != DialogResult.Yes)
        {
            return;
        }

        _library.Remove(entry.Path);
        _library.Save();
        Reload();
    }

    private void OpenSelectedFolder()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(entry.Path);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.ProductName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Save()
    {
        _library.Save();
        Reload();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
