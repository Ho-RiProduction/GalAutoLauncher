# GalAutoLauncher

Windows 后台助手：监听《战争雷霆》(War Thunder) 或其他**非 Galgame 游戏**的**新启动**事件，并在其后随机启动本机的一个 Galgame。

> 默认只监听 War Thunder 的精确进程；如需“打开任意非 Galgame 游戏也触发”，可在设置中开启“任意游戏触发”。
> 本程序**不是**游戏启动器，不会启动战争雷霆/其他游戏，也不会开机或定时自动启动 Galgame。

---

## 功能一览

| 需求 | 实现 |
| --- | --- |
| 托盘驻留、隐藏运行 | ✅ `NotifyIcon`，关闭主窗口仅最小化到托盘 |
| 监听 `aces.exe` / `WarThunder.exe` | ✅ 默认 WMI 实时事件，失败自动回退到进程轮询 |
| 监听任意非 Galgame 游戏（可选） | ⚙️ `TriggerAnyGame` **默认关闭**，只监听战争雷霆；需要时可手动开启，要求可见游戏窗口，并排除系统进程、工具、浏览器、启动器和已知 Galgame |
| 只响应“新启动”的进程 | ✅ 启动时记录基线快照，仅对**新出现**的 PID 触发 |
| 等待若干秒再启动（可配置） | ✅ `LaunchDelaySeconds`，另有 `LaunchDelayJitterSeconds` 随机延迟 |
| 随机启动一个 Galgame | ✅ `RandomLaunch`，**每个游戏权重相同**，且不会连续重复 |
| Galgame 扫描（Kirikiri / Ren'Py / NScripter / BGI / Azarashi / HG3 / Unity 视觉小说） | ✅ 递归扫描，**仅高置信特征**，结果写入 `games.json` |
| 扫描所有固定磁盘 | ✅ 托盘「扫描所有盘寻找 Galgame」/ `scan --all-drives`，跳过 Windows / Program Files 等系统目录 |
| 触发后行为 | ✅ **无冷却**：每次检测到战雷/非 Galgame 游戏新启动都执行；同一 Galgame 已在运行则自动改开另一个 |
| 防递归触发 | ✅ Galgame 启动后 20 秒内忽略其子进程，避免“启动 Galgame → 又被当成普通游戏” |
| 开机自启（注册表 HKCU\...\Run） | ✅ `install` / `uninstall` 命令与 `install.cmd` / `uninstall.cmd` |
| 托盘菜单 | ✅ 重新扫描 / 扫描所有盘 / 测试随机启动 / 开关自启 / 游戏列表 / 设置 / 退出 |
| 可选增强 | ✅ 黑名单、启动日志、启动概率、排除目录、GUI 设置界面 |

---

## 目录结构

```
GalAutoLauncher/
├── GalAutoLauncher.sln
├── build.cmd                       # 一键构建 + 发布为独立单文件 exe
├── src/GalAutoLauncher/
│   ├── GalAutoLauncher.csproj
│   ├── app.manifest                # 普通权限运行 / DPI / 长路径
│   ├── config.json                 # 默认配置（首次运行自动复制）
│   ├── games.json                  # 默认空游戏库
│   ├── install.cmd                 # 开启开机自启
│   ├── uninstall.cmd               # 关闭开机自启
│   ├── Assets/app.ico
│   ├── App/
│   │   ├── Program.cs              # 入口：托盘 / install / uninstall / scan / launch
│   │   ├── TrayApplicationContext.cs
│   │   └── IconProvider.cs
│   ├── Core/
│   │   ├── AppInfo.cs              # 路径解析（便携 / 回退到 %APPDATA%）
│   │   ├── AppConfig.cs            # config.json 模型
│   │   ├── AppState.cs             # state.json 模型（最近启动 / 当日次数 / 上次扫描）
│   │   ├── ConfigManager.cs
│   │   ├── JsonStore.cs            # UTF-8 / 中文不转义 / 原子写入
│   │   ├── Log.cs                  # 按天切分、自动清理的日志
│   │   ├── GameEntry.cs            # 兼容旧版字符串数组的 JSON 转换器
│   │   ├── GameLibrary.cs          # 加载 / 保存 / 等概率随机挑选（不连续重复）
│   │   ├── GameScanner.cs          # 引擎识别
│   │   ├── GameLauncher.cs
│   │   ├── ProcessWatcher.cs       # WMI + 轮询双通道
│   │   ├── AnyGameDetector.cs      # 任意非 Galgame 游戏识别 / 防递归
│   │   ├── StartupManager.cs       # HKCU Run 读写
│   │   └── AutomationEngine.cs     # 核心编排 + 防重复
│   └── UI/
│       ├── MainForm.cs
│       ├── SettingsForm.cs
│       └── GameListForm.cs
└── tests/GalAutoLauncher.TriggerTests/   # 端到端集成测试
```

---

## 构建

前置：安装 [.NET 8 SDK](https://dotnet.microsoft.com/download)（或更高版本 SDK）。

```powershell
# 方式一：一键脚本（构建 + 发布独立单文件 exe 到 publish\）
.\build.cmd

# 方式二：手动
dotnet build GalAutoLauncher.sln -c Release
dotnet publish src\GalAutoLauncher\GalAutoLauncher.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish
```

发布后 `publish\` 内含 `GalAutoLauncher.exe` 与配置文件，**无需目标机器安装 .NET 运行时**。

---

## 使用

1. 把 `publish\` 里的全部文件放到一个固定目录（例如 `C:\Tools\GalAutoLauncher`）。
2. 双击 `install.cmd` → 写入开机自启并启动托盘程序。
3. 右键托盘图标 → **重新扫描 Galgame**；换到新电脑或游戏分散在多个盘时，选择 **扫描所有盘寻找 Galgame**。
4. 之后正常使用：开机 → 托盘程序在后台 → 你打开战争雷霆或其他游戏 → 程序自动随机打开一个 Galgame。

取消自启：双击 `uninstall.cmd`。

### 命令行

```text
GalAutoLauncher.exe              启动托盘助手
GalAutoLauncher.exe --tray       静默启动到托盘（自启用）
GalAutoLauncher.exe install      开启开机自启（加 --start 则同时启动）
GalAutoLauncher.exe uninstall    关闭开机自启
GalAutoLauncher.exe scan         扫描 ScanPath 并写入 games.json
GalAutoLauncher.exe scan --all-drives  扫描所有固定磁盘（跳过系统目录）
GalAutoLauncher.exe launch       测试随机启动一个 Galgame
```

---

## 配置文件

### `config.json`

```json
{
  "ScanPath": "D:\\Files",
  "TriggerProcesses": ["aces", "WarThunder"],
  "LaunchDelaySeconds": 3,
  "RandomLaunch": true,
  "CooldownMinutes": 0,
  "LaunchDelayJitterSeconds": 0,
  "PollIntervalSeconds": 3,
  "MaxScanDepth": 8,
  "LaunchProbabilityPercent": 100,
  "StartupGraceSeconds": 60,
  "DetectUnity": true,
  "DetectGeneric": false,
  "MinimizeToTrayOnStart": true,
  "ShowNotifications": true,
  "LogRetentionDays": 14,
  "ExcludedDirectories": [],
  "TriggerAnyGame": false,
  "AnyGameProcessBlacklist": ["explorer", "chrome", "steam", "updater"],
  "ScanAllDrives": false
}
```

| 字段 | 说明 |
| --- | --- |
| `ScanPath` | Galgame 根目录（递归扫描） |
| `TriggerProcesses` | 监听的进程名（可带或不带 `.exe`） |
| `LaunchDelaySeconds` | 检测到后等待的秒数 |
| `LaunchDelayJitterSeconds` | 额外随机 0~N 秒延迟 |
| `RandomLaunch` | `true` 等概率随机（不会连续重复）；`false` 按“最近未启动”顺序轮换 |
| `CooldownMinutes` | 兼容旧配置保留；当前恒按 0 处理（**不冷却**，每次新启动都会执行） |
| `PollIntervalSeconds` | 轮询检测间隔（1~60） |
| `MaxScanDepth` | 扫描最大递归深度 |
| `LaunchProbabilityPercent` | 触发概率，100 表示必定触发 |
| `StartupGraceSeconds` | **开机保护期**：程序启动后 N 秒内忽略一切触发，避免随系统自启的战雷被误判 |
| `DetectUnity` | 是否扫描 Unity 视觉小说（保守规则，默认开启） |
| `DetectGeneric` | 是否扫描通用入口 `game.exe` 等（误报率高，默认关闭） |
| `ExcludedDirectories` | 扫描时跳过的目录（绝对路径） |
| `TriggerAnyGame` | 是否在任意非 Galgame 游戏启动时也触发；**默认 `false`（只监听 War Thunder）**，开启后额外监听其他游戏 |
| `AnyGameProcessBlacklist` | “任意游戏”识别时额外排除的进程名（浏览器、聊天工具、启动器、工具等） |
| `ScanAllDrives` | 是否让 `scan` 默认扫描所有固定磁盘；托盘菜单也可临时选择全盘扫描 |

> 也可直接使用托盘菜单中的 **设置** 图形界面修改，保存后立即生效。

### `games.json`

兼容两种写法（旧版纯字符串数组会自动升级为对象）：

```json
[
  "D:\\Files\\WA2\\krkr.exe",
  { "Path": "D:\\Files\\SummerPockets\\game.exe", "Engine": "RenPy", "Blacklisted": false, "Weight": 1 }
]
```

`Blacklisted: true` 表示不参与随机启动。随机启动时**所有游戏权重相同**，`Weight` 字段仅为兼容旧配置保留，不再影响概率。

### `state.json`

程序自动维护（最近启动、当日次数、上次扫描时间），一般无需手动修改。

---

## 数据存放位置

优先与 `GalAutoLauncher.exe` **同目录**（便携模式）。若该目录不可写，
自动回退到 `%APPDATA%\GalAutoLauncher`。也可用环境变量 `GALAL_DATA_DIR` 强制指定。

日志位于数据目录下的 `logs\GalAutoLauncher-YYYYMMDD.log`，默认保留 14 天。

---

## 关于进程检测

* 优先使用 **WMI `Win32_ProcessStartTrace`**：内核级、实时、精确。
  该方式在普通用户权限下可能返回“拒绝访问”，此时程序会**自动回退**到进程轮询，功能不受影响。
* 若希望获得最实时的 WMI 检测，可让程序以管理员身份运行；
  但轮询模式（默认每 3 秒）已足够可靠，**无需管理员权限**。
* 无论哪种方式，程序都会在启动时记录一次进程基线，只对**之后新出现**的进程触发，
  因此不会因为开机残留、更新器或崩溃恢复而误触发。
* `TriggerAnyGame` 开启时，程序还会监听全部新进程；候选进程必须在约 5 秒内出现
  **可见主窗口和标题**，并且不命中进程黑名单、系统/工具目录和非游戏标题，才会被视为普通游戏。
  这仍是保守的启发式识别，遇到误报可在设置中把进程名加入 `AnyGameProcessBlacklist`。

---

## 关于 Galgame 识别（重要）

为避免把**其他游戏**误识别成 Galgame，默认只识别具有**高置信引擎特征**的目录：

| 引擎 | 识别特征 | 默认 |
| --- | --- | --- |
| Kirikiri | `krkr.exe` / `krkr2.exe` / `krkrz.exe`，或存在 `*.xp3` | ✅ 开启 |
| Ren'Py | `game\` 内含 `*.rpy` / `*.rpyc`，或存在 `renpy\` 目录 | ✅ 开启 |
| NScripter | 存在 `nscript.dat` / `00.txt` / `*.nsa` / `*.ns2` | ✅ 开启 |
| BGI / Ethornell | `BGI*.exe`，或存在 `*.hvl` / `*.kdb` | ✅ 开启 |
| Azarashi Soft | 存在 `*.pfs`（甜蜜女友 / アマカノ 系列等） | ✅ 开启 |
| HG3（大番長系） | 存在 `*.hg3`，或存在多个 `*.int` | ✅ 开启 |
| Unity 视觉小说 | `UnityPlayer.dll` + `*_Data`，**且**主程序名为中文/日文或存在 `Save` 目录 | ✅ 开启（保守） |
| 通用入口 | `game.exe` / `start.exe` / `launcher.exe` / `play.exe` | ⛔ 默认关闭 |

**同一目录里有多个 exe 时如何选择？**
程序会优先选择**汉化版**（`chs` / `cht` / `zh` / `中文` / `汉化` 等标记），
再优先与目录同名的主程序，并**排除**卸载程序、补丁安装器、启动菜单
（`BootStrap.exe`、`bootmenu.exe`、`uninstall.exe`、`cs2conf.exe` 等）。
例如：

* 柚子社「橘光柠水随想曲」→ 启动 `lllj_zh.exe`（汉化版），而不是日文 `limelight_lj.exe`。
* 樱之诗 → 启动 `BGI.chs.exe`（汉化版），而不是 `BGI.exe`。
* 爱上火车 → 启动 `まいてつ Last Run!!.exe`（真正主程序），而不是 `BootStrap.exe`。
* 妹调教日记 → 启动 `imouto_CHS（win10兼容）.exe`（汉化版）。

**为什么 Unity 用保守规则？**
Unity 的目录结构对所有 Unity 游戏都完全一致，程序无法仅凭结构区分 Galgame 和普通游戏。
因此只有同时满足“主程序名为中文/日文”或“存在 `Save` 目录”时才纳入，
这样能识别《星空列车与白的旅行》这类视觉小说，同时排除 Derail Valley、IRON NEST、
MahjongSoul、PVZ 等普通 Unity 游戏。若误识别仍发生，可在「游戏列表」中将其**禁用**，
或把目录加入 `ExcludedDirectories`。

通用入口误报率高，**不建议开启**。

> 另外，扫描时会跳过 `savedata`、`runtime`、`il2cpp`、`mono`、`crashes`、
> `BepInEx`、`redist` 等非游戏目录，也不会深入已识别的游戏目录。

---

## 关于“开机自启”与随机启动

**开机自启只做一件事：把程序放进托盘开始监听。它不会启动任何 Galgame。**

* 开机自启走的是 `--tray` 入口，只会读配置、读游戏列表、启动监听，
  **不存在**任何“开机扫描”或“开机启动 Galgame”的逻辑。
* 程序启动时会先记录一次进程基线，之后**只对之后新出现的进程**反应。
  因此开机时若战雷已经存在（更新器 / 崩溃恢复），不会触发。
* 额外提供 **开机保护期** `StartupGraceSeconds`（默认 60 秒）：
  程序刚启动的这段时间内忽略一切触发，进一步防止随系统自启的战雷被误判。
* Galgame 只会在三种情况下启动：① 你手动点「测试随机启动」；② 检测到战雷**新启动**；
  ③ `TriggerAnyGame` 开启时检测到其他**非 Galgame 游戏新启动**。
* Galgame 启动后的 20 秒内，程序会忽略新出现的子进程，避免自己启动的 Galgame 又被当成普通游戏递归触发。

---

## 测试

```powershell
# 1) 端到端触发测试（检测 -> 延迟 -> 启动 -> 单实例）
dotnet run --project tests\GalAutoLauncher.TriggerTests -c Release

# 2) 界面冒烟测试（实际构造并显示每个窗体）
dotnet run --project tests\GalAutoLauncher.UiSmoke -c Release

# 3) 开机自启行为测试（确认不会误启动 Galgame）
dotnet run --project tests\GalAutoLauncher.BootTests -c Release

# 4) 识别逻辑测试（确认不会误识别其他游戏）
dotnet run --project tests\GalAutoLauncher.ScannerTests -c Release
```

触发测试分为两阶段：
1. **阶段 1（主功能回归）**：在临时目录搭建沙箱与一个“假 Galgame”，启动真实 `AutomationEngine`，
   拉起一个名为 `aces.exe` 的进程模拟战争雷霆，断言 Galgame 被自动启动、事件触发、状态被记录，
   并断言同一 Galgame 已在运行时会改开另一个、不会重复开同一个；
2. **阶段 2（新增功能）**：模拟一个可见窗口的普通游戏进程，断言它也会触发 Galgame，
   并验证 Galgame 启动后的抑制窗口内不会递归触发。

扫描测试还会覆盖多根目录去重、全盘模式跳过 Windows / Program Files 等系统目录，
以及所有游戏等概率随机挑选的分布。

> 注：测试项目自带 `RollForward`，因此在只装了更高版本 .NET 运行时的机器上也能直接运行。

---

## 许可

仅供个人使用。
