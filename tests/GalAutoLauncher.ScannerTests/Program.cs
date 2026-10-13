using System.Text;
using GalAutoLauncher;

namespace GalAutoLauncher.ScannerTests;

/// <summary>
/// 验证扫描识别逻辑：
///   - 真 Galgame（Kirikiri / RenPy / NScripter / BGI / Azarashi / HG3 / Unity 视觉小说）能被识别；
///   - 同一目录存在多个 exe 时，优先选择汉化版 / 真正的游戏主程序；
///   - 普通游戏（英文名 Unity 游戏、Steam 游戏、启动器、工具）不会被误识别。
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var root = Path.Combine(Path.GetTempPath(), "GalAL-Scanner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        // ---------- 真 Galgame ----------
        MakeFile(root, @"WA2\krkr.exe");
        MakeFile(root, @"WA2\data.xp3");

        MakeFile(root, @"SummerPockets\game\script.rpy");
        MakeFile(root, @"SummerPockets\game.exe");

        MakeFile(root, @"Tsukihime\nscript.dat");
        MakeFile(root, @"Tsukihime\game.exe");

        // 樱之诗：BGI 引擎，应优先选择汉化版 BGI.chs.exe（而不是 BGI.exe / BHVC.exe）
        MakeFile(root, @"SakuraNoUta\BGI.exe");
        MakeFile(root, @"SakuraNoUta\BGI.chs.exe");
        MakeFile(root, @"SakuraNoUta\BHVC.exe");
        MakeFile(root, @"SakuraNoUta\BGI.hvl");
        MakeFile(root, @"SakuraNoUta\BGI.kdb");

        // 柚子社：Kirikiri + xp3，应优先选择汉化启动器 lllj_zh.exe（而不是日文 limelight_lj.exe）
        MakeFile(root, @"Limelight\limelight_lj.exe");
        MakeFile(root, @"Limelight\lllj_zh.exe");
        MakeFile(root, @"Limelight\data.xp3");

        // 甜蜜女友3：Azarashi Soft（.pfs）
        MakeFile(root, @"Amakano3\Amakano3.exe");
        MakeFile(root, @"Amakano3\Amakano3.pfs");
        MakeFile(root, @"Amakano3\unins000.exe");

        // 妹调教日记：HG3（*.hg3 / 多个 *.int），应优先汉化版 imouto_CHS.exe
        MakeFile(root, @"Imouto\imouto.exe");
        MakeFile(root, @"Imouto\imouto_CHS.exe");
        MakeFile(root, @"Imouto\bootmenu.exe");
        MakeFile(root, @"Imouto\bootmenu.hg3");
        MakeFile(root, @"Imouto\config.int");
        MakeFile(root, @"Imouto\image.int");
        MakeFile(root, @"Imouto\scene.int");
        MakeFile(root, @"Imouto\uninst.exe");

        // 爱上火车：Kirikiri + xp3，应优先真正的游戏主程序，而不是 BootStrap.exe
        MakeFile(root, @"Maitetsu\まいてつ Last Run!!.exe");
        MakeFile(root, @"Maitetsu\BootStrap.exe");
        MakeFile(root, @"Maitetsu\Uninstall.exe");
        MakeFile(root, @"Maitetsu\data.xp3");

        // 星空列车：Unity 视觉小说（中文主程序名 + Save 目录），应被识别
        MakeFile(root, @"Hoshishiro\星空列车与白的旅行.exe");
        MakeFile(root, @"Hoshishiro\UnityPlayer.dll");
        MakeFile(root, @"Hoshishiro\星空列车与白的旅行_Data\globalgamemanagers");
        MakeFile(root, @"Hoshishiro\Save\save1.dat");

        // ---------- 其他游戏 / 工具（不应被识别）----------
        // 英文名 Unity 普通游戏（无 Save 目录、非中日文名）
        MakeFile(root, @"SomeUnityGame\UnityPlayer.dll");
        MakeFile(root, @"SomeUnityGame\SomeUnityGame_Data\level0");
        MakeFile(root, @"SomeUnityGame\SomeUnityGame.exe");

        // 英文名 Unity 普通游戏 + 崩溃处理器
        MakeFile(root, @"PuzzleGame\UnityPlayer.dll");
        MakeFile(root, @"PuzzleGame\PuzzleGame_Data\globalgamemanagers");
        MakeFile(root, @"PuzzleGame\PuzzleGame.exe");
        MakeFile(root, @"PuzzleGame\UnityCrashHandler64.exe");

        // 通用入口的普通游戏
        MakeFile(root, @"RandomIndieGame\game.exe");
        MakeFile(root, @"RandomIndieGame\data.bin");

        // 非游戏工具
        MakeFile(root, @"SomeTool\vcredist_x64.exe");

        var scanner = new GameScanner();

        // ===== 严格模式（不扫描 Unity）=====
        Console.WriteLine("[严格模式] 只识别高置信 Galgame 引擎（不含 Unity）…");
        var strict = scanner.Scan(root, 8, null, detectUnity: false, detectGeneric: false);
        Print(strict);

        Check("识别到 WA2 (Kirikiri)", Has(strict, "WA2"));
        Check("识别到 SummerPockets (RenPy)", Has(strict, "SummerPockets"));
        Check("识别到 Tsukihime (NScripter)", Has(strict, "Tsukihime"));
        Check("识别到 樱之诗 (BGI)", Has(strict, "SakuraNoUta"));
        Check("识别到 柚子社 (Kirikiri)", Has(strict, "Limelight"));
        Check("识别到 甜蜜女友3 (Azarashi)", Has(strict, "Amakano3"));
        Check("识别到 妹调教日记 (HG3)", Has(strict, "Imouto"));
        Check("识别到 爱上火车 (Kirikiri)", Has(strict, "Maitetsu"));
        Check("未识别 SomeUnityGame", !Has(strict, "SomeUnityGame"));
        Check("未识别 RandomIndieGame", !Has(strict, "RandomIndieGame"));
        Check("未识别 PuzzleGame", !Has(strict, "PuzzleGame"));
        Check("未识别 SomeTool", !Has(strict, "SomeTool"));
        Check("严格模式恰好 8 个", strict.Games.Count == 8);

        // ===== 汉化版 / 主程序优先级 =====
        Console.WriteLine();
        Console.WriteLine("[入口选择] 汉化版与真正主程序优先…");
        Check("樱之诗选择 BGI.chs.exe",
            Path.GetFileName(Entry(strict, "SakuraNoUta")!.Path).Equals("BGI.chs.exe", StringComparison.OrdinalIgnoreCase));
        Check("柚子社选择 lllj_zh.exe",
            Path.GetFileName(Entry(strict, "Limelight")!.Path).Equals("lllj_zh.exe", StringComparison.OrdinalIgnoreCase));
        Check("妹调选择 imouto_CHS.exe",
            Path.GetFileName(Entry(strict, "Imouto")!.Path).Equals("imouto_CHS.exe", StringComparison.OrdinalIgnoreCase));
        Check("爱上火车选择游戏主程序（非 BootStrap）",
            !Path.GetFileName(Entry(strict, "Maitetsu")!.Path).Equals("BootStrap.exe", StringComparison.OrdinalIgnoreCase));

        // ===== 开启 Unity（保守规则）=====
        Console.WriteLine();
        Console.WriteLine("[DetectUnity=true] 保守纳入 Unity 视觉小说…");
        var withUnity = scanner.Scan(root, 8, null, detectUnity: true, detectGeneric: false);
        Print(withUnity);
        Check("开启后识别到 星空列车 (Unity)", Has(withUnity, "Hoshishiro"));
        Check("开启后仍不识别 SomeUnityGame", !Has(withUnity, "SomeUnityGame"));
        Check("开启后仍不识别 PuzzleGame", !Has(withUnity, "PuzzleGame"));
        Check("开启后仍不识别 RandomIndieGame", !Has(withUnity, "RandomIndieGame"));
        Check("开启后恰好 9 个", withUnity.Games.Count == 9);

        // ===== 多根目录 / 全盘扫描 =====
        Console.WriteLine();
        Console.WriteLine("[多根目录] 支持一次扫描多个盘符根目录并去重…");
        RunMultiRootChecks();

        // ===== 随机挑选：权重相同且不连续重复 =====
        Console.WriteLine();
        Console.WriteLine("[随机挑选] 每个游戏权重相同，且不会连续重复…");
        RunSelectionChecks(strict);

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "全部通过" : $"存在 {_failures} 个失败");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 验证“换到新电脑后扫描所有盘”所依赖的多根目录扫描：
    ///   - 多个根目录可以一次扫描；
    ///   - 根目录重叠（父目录 + 子目录）时结果按路径去重；
    ///   - 全盘模式会跳过盘符根目录下的系统目录（Windows / Program Files 等）。
    /// </summary>
    private static void RunMultiRootChecks()
    {
        var rootA = Path.Combine(Path.GetTempPath(), "GalAL-MultiA-" + Guid.NewGuid().ToString("N"));
        var rootB = Path.Combine(Path.GetTempPath(), "GalAL-MultiB-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        // 两个盘各放一个 Kirikiri 游戏。
        MakeFile(rootA, @"GameA\krkr.exe");
        MakeFile(rootA, @"GameA\data.xp3");
        MakeFile(rootB, @"GameB\krkr.exe");
        MakeFile(rootB, @"GameB\data.xp3");

        var scanner = new GameScanner();
        var multi = scanner.ScanMany(new[] { rootA, rootB }, 8, null, detectUnity: false, detectGeneric: false);
        Check("多根目录扫描同时发现两个游戏", Has(multi, "GameA") && Has(multi, "GameB"));
        Check("多根目录结果 RootPaths 记录两个根", multi.RootPaths.Count == 2);

        // 重叠根目录（父 + 子）不应产生重复条目。
        var overlapping = scanner.ScanMany(
            new[] { rootA, Path.Combine(rootA, "GameA"), rootA },
            8,
            null,
            detectUnity: false,
            detectGeneric: false);
        Check("重叠根目录按路径去重，游戏只出现一次",
            overlapping.Games.Count(g => g.Path.Contains("GameA", StringComparison.OrdinalIgnoreCase)) == 1);

        // 全盘模式：盘符根目录下的 Windows / Program Files 等系统目录应被跳过。
        var systemRoot = Path.Combine(Path.GetTempPath(), "GalAL-System-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(systemRoot);
        MakeFile(systemRoot, @"Windows\FakeGame\krkr.exe");
        MakeFile(systemRoot, @"Windows\FakeGame\data.xp3");
        MakeFile(systemRoot, @"Program Files\FakeTool\krkr.exe");
        MakeFile(systemRoot, @"Program Files\FakeTool\data.xp3");
        MakeFile(systemRoot, @"Games\RealGame\krkr.exe");
        MakeFile(systemRoot, @"Games\RealGame\data.xp3");

        var fullScan = scanner.ScanMany(
            new[] { systemRoot },
            8,
            null,
            detectUnity: false,
            detectGeneric: false,
            skipSystemDirectories: true);
        Check("全盘模式跳过 Windows 目录", !Has(fullScan, "FakeGame"));
        Check("全盘模式跳过 Program Files 目录", !Has(fullScan, "FakeTool"));
        Check("全盘模式仍能发现普通目录下的游戏", Has(fullScan, "RealGame"));

        // 固定磁盘枚举至少应返回当前系统盘。
        var roots = GameScanner.GetFixedDriveRoots();
        Check("枚举到至少一个固定磁盘根目录", roots.Count >= 1);

        try { Directory.Delete(rootA, true); } catch { }
        try { Directory.Delete(rootB, true); } catch { }
        try { Directory.Delete(systemRoot, true); } catch { }
    }

    private static void RunSelectionChecks(ScanResult result)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "GalAL-Pick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var libraryPath = Path.Combine(sandbox, "games.json");

        // 复制为可执行的占位文件，使 File.Exists 成立。
        var entries = new List<GameEntry>();
        foreach (var game in result.Games)
        {
            var dest = Path.Combine(sandbox, SafeName(game));
            File.WriteAllText(dest, "stub");
            entries.Add(new GameEntry { Path = dest, Engine = game.Engine, Name = game.Name });
        }

        JsonStore.Save(libraryPath, entries);
        var library = new GameLibrary(libraryPath);
        library.Load();

        // 每个游戏抽 2000 次，统计分布是否均匀（权重相同）。
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? previous = null;
        var consecutiveRepeat = 0;
        for (var i = 0; i < 20000; i++)
        {
            var picked = library.PickRandom();
            if (picked is null)
            {
                Check("随机挑选返回非空", false);
                return;
            }

            counts[picked.Path] = counts.GetValueOrDefault(picked.Path) + 1;
            if (previous is not null && string.Equals(previous, picked.Path, StringComparison.OrdinalIgnoreCase))
            {
                consecutiveRepeat++;
            }

            previous = picked.Path;
        }

        Check("连续重复次数为 0", consecutiveRepeat == 0);
        Check("每个游戏都被抽到过", counts.Count == entries.Count);

        var min = counts.Values.Min();
        var max = counts.Values.Max();
        // 20000 次 / 8 个游戏 ≈ 2500，允许 ±20% 波动。
        Check($"分布均匀（min={min}, max={max}）", min >= 2000 && max <= 3000);

        try { Directory.Delete(sandbox, true); } catch { }
    }

    private static string SafeName(GameEntry entry) =>
        Math.Abs(entry.Path.GetHashCode()).ToString("X8") + ".exe";

    private static GameEntry? Entry(ScanResult result, string folderToken) =>
        result.Games.FirstOrDefault(g => g.Path.Contains(folderToken, StringComparison.OrdinalIgnoreCase));

    private static bool Has(ScanResult result, string folderToken) =>
        result.Games.Any(g => g.Path.Contains(folderToken, StringComparison.OrdinalIgnoreCase));

    private static void Print(ScanResult result)
    {
        foreach (var g in result.Games)
        {
            Console.WriteLine($"    - [{g.Engine}] {g.Path}");
        }

        if (result.Games.Count == 0)
        {
            Console.WriteLine("    （无）");
        }
    }

    private static void MakeFile(string root, string relative)
    {
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "stub");
    }

    private static void Check(string name, bool condition)
    {
        Console.WriteLine(condition ? $"  [PASS] {name}" : $"  [FAIL] {name}");
        if (!condition) _failures++;
    }
}
