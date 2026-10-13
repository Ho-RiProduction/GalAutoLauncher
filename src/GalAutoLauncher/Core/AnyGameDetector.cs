using System.Diagnostics;

namespace GalAutoLauncher;

/// <summary>
/// “任意非 Galgame 游戏”识别器。
///
/// 采用保守的启发式规则，避免把浏览器、聊天工具、系统进程、启动器、
/// 反作弊、更新器等误判成游戏：
///   1. 命中配置黑名单 / 本程序自身 / 已知 Galgame（路径或文件名）→ 直接排除；
///   2. Galgame 刚启动后的短时抑制窗口内，一切新进程都忽略（防止递归触发）；
///   3. 排除位于 Windows / ProgramData / 常见非游戏厂商目录的进程；
///   4. 要求进程在若干秒内出现“可见主窗口 + 非空标题”；
///   5. 标题命中浏览器 / 工具 / 聊天等关键词时排除。
/// </summary>
internal sealed class AnyGameDetector
{
    private static readonly string[] NonGamePathTokens =
    {
        @"\windows\", @"\$recycle.bin\", @"\programdata\",
        @"\appdata\local\temp\", @"\node_modules\",
        @"\microsoft\", @"\google\", @"\mozilla\", @"\adobe\",
        @"\jetbrains\", @"\notepad++\", @"\wps office\",
        @"\nvidia corporation\", @"\amd\", @"\intel\",
        @"\common files\", @"\windowsapps\",
    };

    private static readonly string[] NonGameTitleTokens =
    {
        "google chrome", "microsoft edge", "firefox", "opera", "brave",
        "visual studio", "visual studio code", "notepad", "sublime text",
        "word", "excel", "powerpoint", "outlook", "onenote",
        "wps", "acrobat", "foxit", "photoshop", "illustrator",
        "steam", "epic games", "gog galaxy", "ubisoft connect", "battle.net",
        "riot client", "ea app", "origin", "wegame", "xbox",
        "discord", "telegram", "wechat", "微信", "qq", "钉钉", "slack", "zoom",
        "task manager", "任务管理器", "control panel", "控制面板",
        "settings", "设置", "explorer", "文件资源管理器",
        "command prompt", "powershell", "terminal", "terminal",
        "obs studio", "vlc", "potplayer", "spotify",
    };

    /// <summary>位于这些路径下时视为“游戏平台库”，可提高置信度（不作为硬性要求）。</summary>
    private static readonly string[] GamePlatformTokens =
    {
        @"\steamapps\common\", @"\epic games\", @"\gog games\",
        @"\gog galaxy\games\", @"\xboxgames\", @"\wegameapps\",
        @"\games\", @"\game\", @"\games library\",
    };

    private readonly object _gate = new();
    private readonly HashSet<string> _blacklist;
    private HashSet<string> _galgameNames;
    private HashSet<string> _galgamePaths;
    private readonly int _selfPid;

    private DateTime _suppressUntilUtc = DateTime.MinValue;
    private string? _lastLaunchedName;
    private string? _lastLaunchedPath;

    public AnyGameDetector(AppConfig config, IEnumerable<GameEntry> library)
    {
        _blacklist = new HashSet<string>(
            config.EffectiveAnyGameProcessBlacklist.Select(Normalize),
            StringComparer.OrdinalIgnoreCase);

        _galgameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _galgamePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        UpdateLibrary(library);

        try
        {
            _selfPid = Environment.ProcessId;
        }
        catch
        {
            _selfPid = 0;
        }
    }

    /// <summary>
    /// 游戏库变化（重新扫描 / 手动增删）后刷新“已知 Galgame”名单，
    /// 避免把新发现的 Galgame 误当成“普通游戏”而递归触发。
    /// </summary>
    public void UpdateLibrary(IEnumerable<GameEntry>? library)
    {
        var entries = (library ?? Array.Empty<GameEntry>())
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Path))
            .ToList();

        var names = new HashSet<string>(
            entries.Select(e => Normalize(Path.GetFileNameWithoutExtension(e.Path))),
            StringComparer.OrdinalIgnoreCase);

        var paths = new HashSet<string>(
            entries.Select(e => NormalizePath(e.Path)),
            StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            _galgameNames = names;
            _galgamePaths = paths;
        }
    }

    public bool IsSuppressed
    {
        get { lock (_gate) { return DateTime.UtcNow < _suppressUntilUtc; } }
    }

    /// <summary>记录刚启动的 Galgame，并在短时间内抑制一切新进程触发。</summary>
    public void SuppressAfterGalgameLaunch(GameEntry entry, TimeSpan duration)
    {
        lock (_gate)
        {
            _suppressUntilUtc = DateTime.UtcNow + duration;
            _lastLaunchedName = Normalize(Path.GetFileNameWithoutExtension(entry.Path));
            _lastLaunchedPath = NormalizePath(entry.Path);
        }
    }

    /// <summary>快速排除：明显不可能是游戏进程时返回 false，不做窗口等待。</summary>
    public bool CouldBeGame(ProcessStartEvent started, out string reason)
    {
        reason = string.Empty;

        if (started.Pid <= 0)
        {
            reason = "无有效 PID";
            return false;
        }

        if (_selfPid > 0 && started.Pid == _selfPid)
        {
            reason = "本程序自身";
            return false;
        }

        var name = Normalize(started.ProcessName);
        if (_blacklist.Contains(name))
        {
            reason = "命中非游戏进程黑名单";
            return false;
        }

        var (galgameNames, galgamePaths) = SnapshotLibrary();

        if (galgameNames.Contains(name))
        {
            reason = "已知 Galgame";
            return false;
        }

        if (IsLastLaunched(started))
        {
            reason = "刚启动的 Galgame";
            return false;
        }

        var path = started.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (galgamePaths.Contains(NormalizePath(path)))
            {
                reason = "已知 Galgame 路径";
                return false;
            }

            var lower = path.ToLowerInvariant();
            if (NonGamePathTokens.Any(token => lower.Contains(token, StringComparison.Ordinal)))
            {
                reason = "位于系统 / 工具目录";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 判定一个候选进程是否为“非 Galgame 游戏”。
    /// 对候选进程最多等待 <paramref name="windowWaitMs"/> 毫秒，等它出现可见主窗口。
    /// </summary>
    public bool IsLikelyGame(ProcessStartEvent started, int windowWaitMs, out string reason)
    {
        reason = string.Empty;

        if (IsSuppressed)
        {
            reason = "Galgame 启动抑制窗口内";
            return false;
        }

        if (!CouldBeGame(started, out reason))
        {
            return false;
        }

        Process? process = null;
        try
        {
            try
            {
                process = Process.GetProcessById(started.Pid);
            }
            catch
            {
                reason = "进程已退出";
                return false;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, windowWaitMs));
            var hasWindow = false;
            var title = string.Empty;
            var path = started.ExecutablePath;

            while (true)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited)
                    {
                        reason = "进程已退出";
                        return false;
                    }

                    hasWindow = process.MainWindowHandle != IntPtr.Zero;
                    title = process.MainWindowTitle ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        try
                        {
                            path = process.MainModule?.FileName;
                        }
                        catch
                        {
                            // 访问受限，保持为 null
                        }
                    }
                }
                catch
                {
                    // 忽略单次读取失败
                }

                if (hasWindow && !string.IsNullOrWhiteSpace(title))
                {
                    break;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                Thread.Sleep(200);
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                var (_, galgamePaths) = SnapshotLibrary();
                if (galgamePaths.Contains(NormalizePath(path)))
                {
                    reason = "已知 Galgame 路径";
                    return false;
                }

                var lowerPath = path.ToLowerInvariant();
                if (NonGamePathTokens.Any(token => lowerPath.Contains(token, StringComparison.Ordinal)))
                {
                    reason = "位于系统 / 工具目录";
                    return false;
                }
            }

            if (!hasWindow || string.IsNullOrWhiteSpace(title))
            {
                reason = "没有可见主窗口";
                return false;
            }

            var lowerTitle = title.ToLowerInvariant();
            if (NonGameTitleTokens.Any(token => lowerTitle.Contains(token, StringComparison.Ordinal)))
            {
                reason = "窗口标题命中非游戏关键词";
                return false;
            }

            // 位于游戏平台库，或存在明显的游戏目录特征，视为高置信。
            var platformMatch = !string.IsNullOrWhiteSpace(path) &&
                GamePlatformTokens.Any(token => path!.ToLowerInvariant().Contains(token, StringComparison.Ordinal));

            reason = platformMatch ? "游戏平台库进程" : $"可见游戏窗口：{title}";
            return true;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private (HashSet<string> Names, HashSet<string> Paths) SnapshotLibrary()
    {
        lock (_gate)
        {
            return (_galgameNames, _galgamePaths);
        }
    }

    private bool IsLastLaunched(ProcessStartEvent started)
    {
        lock (_gate)
        {
            if (_lastLaunchedPath is not null &&
                !string.IsNullOrWhiteSpace(started.ExecutablePath) &&
                string.Equals(_lastLaunchedPath, NormalizePath(started.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _lastLaunchedName is not null &&
                   string.Equals(_lastLaunchedName, Normalize(started.ProcessName), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string processName)
    {
        var name = (processName ?? string.Empty).Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch
        {
            return (path ?? string.Empty).Trim();
        }
    }
}
