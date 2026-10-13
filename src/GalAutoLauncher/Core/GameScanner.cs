using System.Text;

namespace GalAutoLauncher;

/// <summary>
/// Galgame 扫描器。递归遍历根目录，按引擎特征识别可执行入口。
///
/// 识别策略（从严，默认不会把普通游戏误判为 Galgame）：
///   - Kirikiri     ：krkr*.exe，或存在 *.xp3（高置信）
///   - Ren'Py       ：game 目录内含 *.rpy / *.rpyc，或存在 renpy 目录（高置信）
///   - NScripter    ：nscript.dat / 00.txt / *.nsa / *.ns2（高置信）
///   - BGI/Ethornell：BGI*.exe / *.hvl / *.kdb（高置信）
///   - Azarashi Soft：*.pfs（高置信）
///   - HG3（大番長系）：*.hg3 / *.int（高置信）
///   - Unity        ：UnityPlayer.dll + *_Data，且主程序名为中文/日文或存在 Save 目录（较保守）
///   - 通用入口     ：game.exe / start.exe 等（可选，默认关闭，误报率高）
///
/// 同一目录内存在多个 exe 时，会优先选择“汉化版”和与目录同名的主程序，
/// 并排除卸载程序、补丁安装器、启动菜单等辅助程序。
/// </summary>
internal sealed class GameScanner
{
    private static readonly string[] KirikiriExeNames =
    {
        "krkr.exe", "krkr2.exe", "krkrz.exe",
    };

    private static readonly string[] GenericExeNames =
    {
        "game.exe", "start.exe", "launcher.exe", "play.exe",
    };

    /// <summary>汉化 / 本地化版本的常见命名标记。</summary>
    private static readonly string[] ChineseTokens =
    {
        "chs", "cht", "zh", "cn", "sc", "tc", "chinese",
        "simplified", "traditional", "中文", "汉化", "简中", "繁中",
        "简体", "繁体", "簡體", "繁體",
    };

    /// <summary>启动菜单 / 设置工具等辅助程序，命中后降权。</summary>
    private static readonly string[] LauncherTokens =
    {
        "bootstrap", "bootmenu", "boot", "startup", "startmenu",
        "launcher", "config", "setting", "support", "check",
        "patch", "updater", "update", "补丁", "パッチ",
    };

    /// <summary>确定不是游戏主程序的辅助程序，直接排除。</summary>
    private static readonly string[] HardExcludeTokens =
    {
        "unins", "uninstall", "setup", "install", "vcredist", "dxsetup",
        "crashpad", "crashhandler", "easyanticheat", "battleye",
        "delfile", "cs2conf", "report", "破損", "破损", "修復", "修复",
        "アンインストール",
    };

    private static readonly string[] SkipDirectoryNames =
    {
        "savedata", "save", "savegames", "cache", "temp", "tmp",
        "logs", "log", "system volume information", "$recycle.bin",
        "node_modules", ".git", "appdata", "windows", "python",
        "directx", "redist", "_redist", "dotnet", "runtime",
        "mono", "il2cpp", "crashes", "unity", "bepinex",
    };

    /// <summary>
    /// 全盘扫描时需要跳过的系统 / 程序目录（位于盘符根目录下一层时生效）。
    /// </summary>
    private static readonly string[] SystemRootDirectoryNames =
    {
        "windows", "program files", "program files (x86)", "programdata",
        "$recycle.bin", "system volume information", "$windows.~bt", "$windows.~ws",
        "recovery", "perflogs", "msocache", "intel", "amd", "nvidia",
        "config.msi", "onedrivetemp", "boot", "efi",
    };

    public ScanResult Scan(
        string rootPath,
        int maxDepth,
        IEnumerable<string>? excludedDirectories = null,
        bool detectUnity = true,
        bool detectGeneric = false)
    {
        return ScanMany(
            new[] { rootPath },
            maxDepth,
            excludedDirectories,
            detectUnity,
            detectGeneric,
            skipSystemDirectories: false);
    }

    /// <summary>
    /// 扫描所有固定磁盘（本地硬盘）。用于换到新电脑后一次性发现全部 Galgame。
    /// 会跳过 Windows / Program Files / ProgramData 等系统目录，降低误报与耗时。
    /// </summary>
    public ScanResult ScanAllDrives(
        int maxDepth,
        IEnumerable<string>? excludedDirectories = null,
        bool detectUnity = true,
        bool detectGeneric = false)
    {
        var roots = GetFixedDriveRoots();
        return ScanMany(
            roots,
            maxDepth,
            excludedDirectories,
            detectUnity,
            detectGeneric,
            skipSystemDirectories: true);
    }

    /// <summary>返回所有可用的固定磁盘根目录（如 C:\、D:\）。</summary>
    public static IReadOnlyList<string> GetFixedDriveRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(drive.RootDirectory?.FullName))
                    {
                        roots.Add(drive.RootDirectory.FullName);
                    }
                }
                catch
                {
                    // 单个盘不可访问，跳过
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"枚举磁盘失败：{ex.Message}");
        }

        return roots;
    }

    /// <summary>对多个根目录依次扫描，并按路径去重（同一游戏只保留一个入口）。</summary>
    public ScanResult ScanMany(
        IEnumerable<string> rootPaths,
        int maxDepth,
        IEnumerable<string>? excludedDirectories = null,
        bool detectUnity = true,
        bool detectGeneric = false,
        bool skipSystemDirectories = false)
    {
        var roots = (rootPaths ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new ScanResult
        {
            RootPath = roots.Count == 1 ? roots[0] : string.Join("；", roots),
            RootPaths = roots,
        };

        if (roots.Count == 0)
        {
            Log.Warn("没有可扫描的目录。");
            return result;
        }

        var exclusions = (excludedDirectories ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().TrimEnd(Path.DirectorySeparatorChar))
            .ToArray();

        var options = new ScanOptions(detectUnity, detectGeneric);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in roots)
        {
            if (!Directory.Exists(rootPath))
            {
                Log.Warn($"扫描目录不存在：{rootPath}");
                continue;
            }

            Log.Info($"开始扫描：{rootPath}");
            var stack = new Stack<(string Directory, int Depth)>();
            stack.Push((rootPath, 0));

            while (stack.Count > 0)
            {
                var (directory, depth) = stack.Pop();
                if (IsExcluded(directory, exclusions))
                {
                    continue;
                }

                IEnumerable<string> children;
                try
                {
                    children = Directory.EnumerateDirectories(directory);
                }
                catch (Exception ex)
                {
                    Log.Debug($"无法枚举目录 {directory}：{ex.Message}");
                    continue;
                }

                foreach (var child in children)
                {
                    if (IsExcluded(child, exclusions))
                    {
                        continue;
                    }

                    var name = Path.GetFileName(child) ?? string.Empty;
                    if (SkipDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 全盘扫描时，跳过盘符根目录下的系统 / 程序目录。
                    if (skipSystemDirectories && depth == 0 &&
                        SystemRootDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (TryDetectGame(child, options, out var entry))
                    {
                        if (seen.Add(entry.Path))
                        {
                            result.Games.Add(entry);
                            Log.Info($"发现 Galgame：{entry.Path} [{entry.Engine}]");
                        }

                        // 命中后不再深入该游戏目录，避免把内部组件当成独立游戏。
                        continue;
                    }

                    if (depth + 1 < maxDepth)
                    {
                        stack.Push((child, depth + 1));
                    }
                }
            }
        }

        result.Games.Sort((a, b) =>
            string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        return result;
    }

    private readonly record struct ScanOptions(bool DetectUnity, bool DetectGeneric);

    /// <summary>对单个目录尝试识别 Galgame 入口，成功返回 true。</summary>
    public static bool TryDetectGame(string directory, out GameEntry entry) =>
        TryDetectGame(directory, new ScanOptions(false, false), out entry);

    /// <summary>对单个目录尝试识别 Galgame 入口（可指定是否放宽到 Unity / 通用）。</summary>
    public static bool TryDetectGame(string directory, bool detectUnity, bool detectGeneric, out GameEntry entry) =>
        TryDetectGame(directory, new ScanOptions(detectUnity, detectGeneric), out entry);

    private static bool TryDetectGame(string directory, ScanOptions options, out GameEntry entry)
    {
        entry = new GameEntry();

        string[] files;
        string[] subDirectories;
        try
        {
            files = Directory.GetFiles(directory);
            subDirectories = Directory.GetDirectories(directory);
        }
        catch
        {
            return false;
        }

        var fileNames = files.Select(f => Path.GetFileName(f) ?? string.Empty).ToArray();
        var directoryNames = subDirectories.Select(d => Path.GetFileName(d) ?? string.Empty).ToArray();
        var folderName = Path.GetFileName(directory);

        // ---- Ren'Py（高置信）----
        // 特征：game 目录内含 *.rpy / *.rpyc，或存在 renpy 目录。
        if (directoryNames.Any(n => n.Equals("game", StringComparison.OrdinalIgnoreCase)))
        {
            var gameDir = subDirectories.First(n =>
                (Path.GetFileName(n) ?? string.Empty).Equals("game", StringComparison.OrdinalIgnoreCase));

            var renpyMarkers = SafeEnumerateFiles(gameDir, "*.rpy")
                .Concat(SafeEnumerateFiles(gameDir, "*.rpyc"))
                .Any();
            var hasRenpyFolder = directoryNames.Any(n => n.Equals("renpy", StringComparison.OrdinalIgnoreCase));

            if (renpyMarkers || hasRenpyFolder)
            {
                var exe = files.FirstOrDefault(f =>
                    (Path.GetFileName(f) ?? string.Empty).Equals("game.exe", StringComparison.OrdinalIgnoreCase))
                    ?? PickBestExe(files, folderName);

                if (exe is not null)
                {
                    entry = new GameEntry
                    {
                        Path = exe,
                        Engine = GameEngine.RenPy,
                        Name = folderName,
                    };
                    return true;
                }
            }
        }

        // ---- Kirikiri（高置信）----
        // 特征：krkr*.exe，或存在 *.xp3。
        var kirikiriExe = files.FirstOrDefault(f =>
            KirikiriExeNames.Contains(Path.GetFileName(f) ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        var hasXp3 = files.Any(f => f.EndsWith(".xp3", StringComparison.OrdinalIgnoreCase)) ||
                     subDirectories.Any(d => SafeEnumerateFiles(d, "*.xp3").Any());

        if (kirikiriExe is not null || hasXp3)
        {
            var exe = kirikiriExe ?? PickBestExe(files, folderName);
            if (exe is not null)
            {
                entry = new GameEntry
                {
                    Path = exe,
                    Engine = GameEngine.Kirikiri,
                    Name = folderName,
                };
                return true;
            }
        }

        // ---- NScripter（高置信）----
        // 特征：nscript.dat / 00.txt / *.nsa / *.ns2。
        var hasNsMarker =
            fileNames.Any(n => n.Equals("nscript.dat", StringComparison.OrdinalIgnoreCase)) ||
            fileNames.Any(n => n.Equals("00.txt", StringComparison.OrdinalIgnoreCase)) ||
            files.Any(f => f.EndsWith(".nsa", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".ns2", StringComparison.OrdinalIgnoreCase));

        if (hasNsMarker)
        {
            var exe = PickBestExe(files, folderName);
            if (exe is not null)
            {
                entry = new GameEntry
                {
                    Path = exe,
                    Engine = GameEngine.NScripter,
                    Name = folderName,
                };
                return true;
            }
        }

        // ---- BGI / Ethornell（高置信）----
        // 特征：BGI*.exe，或存在 *.hvl / *.kdb（樱之诗等）。
        var hasBgi =
            fileNames.Any(n => n.StartsWith("BGI", StringComparison.OrdinalIgnoreCase) &&
                               n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ||
            files.Any(f => f.EndsWith(".hvl", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".kdb", StringComparison.OrdinalIgnoreCase));

        if (hasBgi)
        {
            var exe = PickBestExe(files, folderName);
            if (exe is not null)
            {
                entry = new GameEntry
                {
                    Path = exe,
                    Engine = GameEngine.Bgi,
                    Name = folderName,
                };
                return true;
            }
        }

        // ---- Azarashi Soft（.pfs，高置信）----
        // 特征：存在 *.pfs（甜蜜女友 / アマカノ 系列等）。
        var hasPfs = files.Any(f => f.EndsWith(".pfs", StringComparison.OrdinalIgnoreCase)) ||
                     subDirectories.Any(d => SafeEnumerateFiles(d, "*.pfs").Any());

        if (hasPfs)
        {
            var exe = PickBestExe(files, folderName);
            if (exe is not null)
            {
                entry = new GameEntry
                {
                    Path = exe,
                    Engine = GameEngine.Azarashi,
                    Name = folderName,
                };
                return true;
            }
        }

        // ---- HG3（大番長 / 妹调教日记等，高置信）----
        // 特征：存在 *.hg3，或存在多个 *.int 数据文件。
        var hg3Count = files.Count(f => f.EndsWith(".hg3", StringComparison.OrdinalIgnoreCase));
        var intCount = files.Count(f => f.EndsWith(".int", StringComparison.OrdinalIgnoreCase));
        if (hg3Count > 0 || intCount >= 3)
        {
            var exe = PickBestExe(files, folderName);
            if (exe is not null)
            {
                entry = new GameEntry
                {
                    Path = exe,
                    Engine = GameEngine.Hg3,
                    Name = folderName,
                };
                return true;
            }
        }

        // ---- Unity（较保守，默认开启）----
        // 特征：UnityPlayer.dll + *_Data，且满足以下任一条件：
        //   - 主程序名为中文 / 日文（日式、国产视觉小说常见）
        //   - 存在 Save / savedata 目录
        // 这样可避免把 Derail Valley、PVZ 等普通 Unity 游戏误判为 Galgame。
        if (options.DetectUnity)
        {
            var hasUnityPlayer = fileNames.Any(n =>
                n.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase));
            var hasDataDir = directoryNames.Any(n => n.EndsWith("_Data", StringComparison.OrdinalIgnoreCase));
            var hasSaveDir = directoryNames.Any(n =>
                n.Equals("save", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("savedata", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("savegames", StringComparison.OrdinalIgnoreCase));

            if (hasUnityPlayer && hasDataDir)
            {
                var exe = PickBestExe(files, folderName);
                var looksLikeVisualNovel = exe is not null &&
                    (HasCjk(Path.GetFileNameWithoutExtension(exe)) || hasSaveDir);

                if (exe is not null && looksLikeVisualNovel)
                {
                    entry = new GameEntry
                    {
                        Path = exe,
                        Engine = GameEngine.Unity,
                        Name = folderName,
                    };
                    return true;
                }
            }
        }

        // ---- 通用回退（可选，默认关闭）----
        if (options.DetectGeneric)
        {
            var generic = files.FirstOrDefault(f =>
                GenericExeNames.Contains(Path.GetFileName(f) ?? string.Empty, StringComparer.OrdinalIgnoreCase));
            if (generic is not null)
            {
                entry = new GameEntry
                {
                    Path = generic,
                    Engine = GameEngine.Generic,
                    Name = folderName,
                };
                return true;
            }
        }

        return false;
    }

    private static bool IsExcluded(string directory, string[] exclusions)
    {
        if (exclusions.Length == 0)
        {
            return false;
        }

        foreach (var exclusion in exclusions)
        {
            if (directory.Equals(exclusion, StringComparison.OrdinalIgnoreCase) ||
                directory.StartsWith(exclusion + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 在目录中挑选最可能的主程序 exe：
    /// 优先汉化版本与和目录同名的主程序，排除卸载 / 补丁 / 启动菜单等辅助程序。
    /// </summary>
    private static string? PickBestExe(IEnumerable<string> files, string? folderName)
    {
        var exes = files
            .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exes.Count == 0)
        {
            return null;
        }

        var scored = exes
            .Select(path => (Path: path, Score: ScoreExe(path, folderName)))
            .Where(item => item.Score > int.MinValue)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => SafeLength(item.Path))
            .ThenBy(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return scored.Count > 0 ? scored[0].Path : null;
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static int ScoreExe(string path, string? folderName)
    {
        var name = Path.GetFileName(path) ?? string.Empty;
        var lower = name.ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(name);
        var stemLower = stem.ToLowerInvariant();

        // 明显的非游戏程序，直接排除。
        if (HardExcludeTokens.Any(token => lower.Contains(token, StringComparison.Ordinal)))
        {
            return int.MinValue;
        }

        var score = 0;

        // 汉化 / 本地化版本优先。
        if (ChineseTokens.Any(token => HasToken(lower, token)))
        {
            score += 80;
        }

        // 主程序名与目录名一致（或去掉空格符号后一致）优先。
        var folderLower = (folderName ?? string.Empty).ToLowerInvariant();
        if (folderLower.Length > 0)
        {
            if (stemLower == folderLower)
            {
                score += 70;
            }
            else if (NormalizeForCompare(stemLower) == NormalizeForCompare(folderLower))
            {
                score += 55;
            }
        }

        if (stemLower.StartsWith("krkr", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (stemLower is "game" or "start" or "play" or "launcher")
        {
            score += 50;
        }

        if (lower.Contains("game") || lower.Contains("gal") || lower.Contains("love"))
        {
            score += 10;
        }

        // 启动菜单 / 设置 / 补丁等辅助程序降权。
        if (LauncherTokens.Any(token => lower.Contains(token, StringComparison.Ordinal)))
        {
            score -= 40;
        }

        // 主程序通常体积较大；补丁 / 小启动器降权。
        var length = SafeLength(path);
        if (length >= 1_000_000)
        {
            score += 15;
        }
        else if (length < 200_000)
        {
            score -= 25;
        }

        return score;
    }

    /// <summary>判断文件名中是否包含某个“独立词”（前后为非字母数字），避免 zh 误命中 zhangsan 之类。</summary>
    private static bool HasToken(string lowerName, string token)
    {
        var index = 0;
        while (true)
        {
            index = lowerName.IndexOf(token, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var beforeOk = index == 0 || !char.IsLetterOrDigit(lowerName[index - 1]);
            var afterIndex = index + token.Length;
            var afterOk = afterIndex >= lowerName.Length || !char.IsLetterOrDigit(lowerName[afterIndex]);

            if (beforeOk && afterOk)
            {
                return true;
            }

            index += token.Length;
        }
    }

    private static string NormalizeForCompare(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static bool HasCjk(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if ((ch >= 0x2E80 && ch <= 0x9FFF) ||
                (ch >= 0xF900 && ch <= 0xFAFF) ||
                (ch >= 0x3040 && ch <= 0x30FF))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ScanResult
{
    public string RootPath { get; set; } = string.Empty;

    /// <summary>本次扫描涉及的根目录列表（单目录扫描时只有一个）。</summary>
    public List<string> RootPaths { get; set; } = new();

    public List<GameEntry> Games { get; } = new();
}