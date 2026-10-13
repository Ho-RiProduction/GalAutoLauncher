using System.Text.Json.Serialization;

namespace GalAutoLauncher;

/// <summary>
/// 用户配置，落盘为 config.json。
/// </summary>
internal sealed class AppConfig
{
    [JsonPropertyOrder(0)]
    public string ScanPath { get; set; } = @"D:\Files";

    [JsonPropertyOrder(1)]
    public string[] TriggerProcesses { get; set; } = { "aces", "WarThunder" };

    [JsonPropertyOrder(2)]
    public int LaunchDelaySeconds { get; set; } = 3;

    [JsonPropertyOrder(3)]
    public bool RandomLaunch { get; set; } = true;

    /// <summary>
    /// 旧版冷却配置，仅为兼容历史 config.json 保留；新版不再使用（始终按 0 处理）。
    /// </summary>
    [JsonPropertyOrder(4)]
    public int CooldownMinutes { get; set; } = 0;

    [JsonPropertyOrder(5)]
    public int LaunchDelayJitterSeconds { get; set; } = 0;

    [JsonPropertyOrder(6)]
    public int PollIntervalSeconds { get; set; } = 3;

    [JsonPropertyOrder(7)]
    public int MaxScanDepth { get; set; } = 8;

    [JsonPropertyOrder(8)]
    public int LaunchProbabilityPercent { get; set; } = 100;

    /// <summary>
    /// 程序启动后的“开机保护期”（秒）。在此期间忽略一切触发，
    /// 用于避免随开机自启的进程被误判为用户主动启动。
    /// 0 表示关闭该保护。
    /// </summary>
    [JsonPropertyOrder(9)]
    public int StartupGraceSeconds { get; set; } = 60;

    /// <summary>
    /// 是否扫描 Unity 视觉小说。采用保守规则：需要 UnityPlayer.dll + *_Data，
    /// 且主程序名为中文/日文或存在 Save 目录，尽量避免混入普通 Unity 游戏。
    /// </summary>
    [JsonPropertyOrder(10)]
    public bool DetectUnity { get; set; } = true;

    /// <summary>
    /// 是否扫描通用入口（game.exe / start.exe 等）。
    /// 误报率高，默认关闭。
    /// </summary>
    [JsonPropertyOrder(11)]
    public bool DetectGeneric { get; set; }

    [JsonPropertyOrder(12)]
    public bool MinimizeToTrayOnStart { get; set; } = true;

    [JsonPropertyOrder(13)]
    public bool ShowNotifications { get; set; } = true;

    [JsonPropertyOrder(14)]
    public int LogRetentionDays { get; set; } = 14;

    [JsonPropertyOrder(15)]
    public string[] ExcludedDirectories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 除 War Thunder 之外，是否也在“任意非 Galgame 游戏”启动时触发。
    /// 默认开启；识别采用保守规则（可见主窗口 + 非系统进程 + 排除启动器/工具/浏览器等）。
    /// </summary>
    [JsonPropertyOrder(16)]
    public bool TriggerAnyGame { get; set; } = true;

    /// <summary>
    /// “任意游戏”识别的额外进程名黑名单（不需要写 .exe）。
    /// 用于排除浏览器、聊天工具、启动器、工具等。
    /// </summary>
    [JsonPropertyOrder(17)]
    public string[] AnyGameProcessBlacklist { get; set; } = DefaultAnyGameProcessBlacklist();

    /// <summary>
    /// 扫描时是否遍历所有固定磁盘（跳过 Windows / Program Files / ProgramData 等系统目录）。
    /// </summary>
    [JsonPropertyOrder(18)]
    public bool ScanAllDrives { get; set; }

    [JsonIgnore]
    public int EffectivePollIntervalSeconds => Math.Clamp(PollIntervalSeconds, 1, 60);

    [JsonIgnore]
    public int EffectiveMaxScanDepth => Math.Clamp(MaxScanDepth, 1, 24);

    [JsonIgnore]
    public int EffectiveCooldownMinutes => 0;

    [JsonIgnore]
    public int EffectiveLaunchDelaySeconds => Math.Clamp(LaunchDelaySeconds, 0, 600);

    [JsonIgnore]
    public int EffectiveJitterSeconds => Math.Clamp(LaunchDelayJitterSeconds, 0, 120);

    [JsonIgnore]
    public int EffectiveProbabilityPercent => Math.Clamp(LaunchProbabilityPercent, 0, 100);

    [JsonIgnore]
    public int EffectiveLogRetentionDays => Math.Clamp(LogRetentionDays, 1, 365);

    [JsonIgnore]
    public int EffectiveStartupGraceSeconds => Math.Clamp(StartupGraceSeconds, 0, 3600);

    [JsonIgnore]
    public string[] EffectiveAnyGameProcessBlacklist => NormalizeProcesses(AnyGameProcessBlacklist, allowEmpty: true);

    public void Normalize()
    {
        ScanPath ??= string.Empty;
        TriggerProcesses = NormalizeProcesses(TriggerProcesses, allowEmpty: false);
        AnyGameProcessBlacklist = NormalizeProcesses(AnyGameProcessBlacklist, allowEmpty: true);
        ExcludedDirectories ??= Array.Empty<string>();
        ExcludedDirectories = ExcludedDirectories
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeProcesses(string[]? processes, bool allowEmpty)
    {
        var result = (processes ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(TrimExecutableExtension)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (result.Length > 0 || allowEmpty)
        {
            return result;
        }

        return new[] { "aces", "WarThunder" };
    }

    private static string TrimExecutableExtension(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed;
    }

    private static string[] DefaultAnyGameProcessBlacklist() => new[]
    {
        // 系统 / 外壳
        "explorer", "dwm", "csrss", "winlogon", "services", "svchost", "taskhostw",
        "runtimebroker", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "textinputhost", "ctfmon", "conhost", "cmd", "powershell", "pwsh", "windowsterminal",
        "wt", "regedit", "mmc", "taskmgr", "control", "rundll32", "dllhost", "sihost",
        "fontdrvhost", "lsass", "smss", "wininit", "audiodg", "spoolsv", "searchindexer",
        "securityhealthservice", "widgets", "widgetservice", "phoneexperiencehost",
        "applicationframehost", "shellexperiencehost", "usoclient", "musnotification",

        // 浏览器 / 聊天 / 办公 / 媒体
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "iexplore",
        "wechat", "weixin", "qq", "tim", "dingtalk", "discord", "telegram", "slack",
        "zoom", "teams", "ms-teams", "skype", "line", "whatsapp",
        "winword", "excel", "powerpnt", "onenote", "outlook", "wps", "et", "wpp",
        "acrord32", "acrobat", "foxitreader", "sumatrapdf",
        "spotify", "vlc", "mpv", "potplayermini64", "potplayer", "musicbee", "foobar2000",
        "obs64", "obs32", "obs", "bilibili", "cloudmusic",

        // 开发 / 工具
        "devenv", "code", "code-insiders", "idea64", "pycharm64", "rider64", "webstorm64",
        "clion64", "goland64", "sublime_text", "notepad++", "notepad", "git-bash", "git",
        "mintty", "putty", "xshell", "xftp", "winscp", "fiddler", "wireshark", "procmon",
        "procexp", "ida64", "ida", "x64dbg", "ollydbg", "cheatengine-x86_64", "cheatengine",
        "python", "pythonw", "java", "javaw", "node", "dotnet", "msbuild", "nuget",
        "ssms", "mysqlworkbench", "navicat", "dbeaver", "postman", "fiddler everywhere",

        // 启动器 / 平台 / 更新器 / 安装器
        "steam", "steamwebhelper", "steamservice", "steamerrorreporter",
        "epicgameslauncher", "epicwebhelper", "galaxylauncher", "goggalaxy",
        "uplay", "ubisoftconnect", "ubisoftgamelauncher", "battle.net", "agent",
        "riotclientservices", "riotclientux", "origin", "eadesktop", "eabackgroundservice",
        "xboxapp", "gamingapp", "xboxgamebar", "gamebar", "gamebarpresencewriter",
        "wegame", "wegamehelper", "qqgame", "launcher", "updater", "update", "setup",
        "install", "unins", "uninstall", "vcredist", "dxsetup", "directx",

        // 反作弊 / 崩溃 / 辅助
        "easyanticheat", "easyanticheat_eos", "beservice", "battleye", "belauncher",
        "vgtray", "vgc", "vgk", "vanguard", "nprotect", "xhunter", "ace", "aceguard",
        "crashpad_handler", "crashreportclient", "crashhandler", "unitycrashhandler64",
        "unitycrashhandler32", "werfault", "wermgr", "report", "feedback",
        "galautolauncher",
    };
}
