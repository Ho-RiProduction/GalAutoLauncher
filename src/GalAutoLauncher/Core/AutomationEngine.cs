namespace GalAutoLauncher;

internal enum EngineStatus
{
    Stopped,
    Waiting,
    Delay,
    Launching,
    Disabled,
}

/// <summary>
/// 核心编排器：把“检测到目标进程 / 任意游戏新进程”转换为“延迟后随机启动一个 Galgame”。
/// 不使用冷却：每次检测到新的目标进程都会尝试启动；
/// 但同一个 Galgame 同时只会运行一个实例。
///
/// 触发来源有两类：
///   1. TriggerProcesses 中精确指定的进程（默认 aces / WarThunder）；
///   2. TriggerAnyGame 开启时，任意被识别为“非 Galgame 游戏”的新进程。
/// </summary>
internal sealed class AutomationEngine : IDisposable
{
    /// <summary>Galgame 启动后抑制新进程触发的时长，避免其子进程再次触发。</summary>
    private static readonly TimeSpan GalgameLaunchSuppression = TimeSpan.FromSeconds(20);

    private const int AnyGameWindowWaitMs = 5000;

    private readonly ConfigManager _configManager;
    private readonly GameLibrary _library;
    private readonly object _gate = new();

    private ProcessWatcher? _watcher;
    private AnyGameDetector? _anyGameDetector;
    private System.Threading.Timer? _launchTimer;
    private DateTime _graceUntilUtc = DateTime.MinValue;
    private volatile bool _busy;
    private string _lastMessage = "等待 War Thunder";
    private bool _disposed;
    private bool _applyStartupGrace = true;

    public AutomationEngine(ConfigManager configManager, GameLibrary library)
    {
        _configManager = configManager;
        _library = library;
    }

    public event Action<EngineStatus, string>? StatusChanged;
    public event Action<GameEntry>? GameLaunched;
    public event Action<string>? Notify;

    public EngineStatus Status { get; private set; } = EngineStatus.Stopped;

    public string LastMessage
    {
        get { lock (_gate) { return _lastMessage; } }
    }

    public string DetectionMode => _watcher?.DetectionMode ?? "未启动";

    public bool IsRunning => _watcher is not null;

    public void Start()
    {
        Start(_applyStartupGrace);
    }

    /// <summary>
    /// 启动监听。<paramref name="applyStartupGrace"/> 为 false 时跳过开机保护期，
    /// 用于用户手动打开程序：用户刚启动程序后立刻打开游戏也应该正常触发。
    /// </summary>
    public void Start(bool applyStartupGrace)
    {
        if (_watcher is not null)
        {
            return;
        }

        _applyStartupGrace = applyStartupGrace;
        var config = _configManager.Config;

        _anyGameDetector = config.TriggerAnyGame
            ? new AnyGameDetector(config, _library.All)
            : null;

        _watcher = new ProcessWatcher(
            config.TriggerProcesses,
            config.EffectivePollIntervalSeconds,
            monitorAllProcesses: config.TriggerAnyGame);
        _watcher.ProcessStarted += OnProcessStarted;
        _watcher.Start();

        var grace = applyStartupGrace ? config.EffectiveStartupGraceSeconds : 0;
        _graceUntilUtc = grace > 0 ? DateTime.UtcNow.AddSeconds(grace) : DateTime.MinValue;

        SetStatus(EngineStatus.Waiting, WaitingMessage(config));
        Log.Info($"监听已启动（{_watcher.DetectionMode}），目标进程：{string.Join(", ", config.TriggerProcesses)}" +
                 (config.TriggerAnyGame ? "，并监听任意非 Galgame 游戏。" : "。") +
                 (grace > 0 ? $"开机保护期 {grace} 秒。" : "手动启动，不启用开机保护期。"));
    }

    public void Stop()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Dispose();
        _watcher = null;
        _anyGameDetector = null;

        lock (_gate)
        {
            _launchTimer?.Dispose();
            _launchTimer = null;
        }

        SetStatus(EngineStatus.Stopped, "已停止监听");
        Log.Info("监听已停止。");
    }

    /// <summary>配置变更后调用：重启监听以应用新的进程名 / 轮询间隔 / 触发策略。</summary>
    public void Reload()
    {
        if (_watcher is null)
        {
            return;
        }

        Stop();
        Start();
    }

    /// <summary>Galgame 由用户手动启动后，也要刷新抑制窗口，避免误触发递归。</summary>
    public void NotifyExternalGalgameLaunch(GameEntry entry)
    {
        _anyGameDetector?.UpdateLibrary(_library.All);
        _anyGameDetector?.SuppressAfterGalgameLaunch(entry, GalgameLaunchSuppression);
    }

    /// <summary>
    /// 游戏库变化（重新扫描 / 手动增删 / 切换黑名单）后刷新“任意游戏”识别器里的
    /// 已知 Galgame 名单，避免把刚发现的 Galgame 当成普通游戏而递归触发。
    /// </summary>
    public void RefreshLibrary()
    {
        _anyGameDetector?.UpdateLibrary(_library.All);
    }

    private void OnProcessStarted(ProcessStartEvent started)
    {
        if (_disposed)
        {
            return;
        }

        var config = _configManager.Config;
        var isExactTarget = config.TriggerProcesses.Any(target =>
            string.Equals(Normalize(target), Normalize(started.ProcessName), StringComparison.OrdinalIgnoreCase));

        if (isExactTarget)
        {
            // 战雷等精确目标：不要求窗口，立即按原逻辑处理，保证主功能不受影响。
            HandleTrigger(started.ProcessName);
            return;
        }

        if (!config.TriggerAnyGame || _anyGameDetector is null)
        {
            return;
        }

        // 任意游戏识别需要等待主窗口，放到后台线程，避免阻塞 WMI / 轮询线程。
        var detector = _anyGameDetector;
        _ = Task.Run(() =>
        {
            try
            {
                if (_disposed || !ReferenceEquals(detector, _anyGameDetector))
                {
                    return;
                }

                if (!detector.IsLikelyGame(started, AnyGameWindowWaitMs, out var reason))
                {
                    Log.Debug($"进程 {started.ProcessName} (PID={started.Pid}) 未视为游戏：{reason}");
                    return;
                }

                Log.Info($"识别到游戏进程：{started.ProcessName} (PID={started.Pid})，{reason}");
                HandleTrigger(started.ProcessName);
            }
            catch (Exception ex)
            {
                Log.Debug($"任意游戏识别失败：{ex.Message}");
            }
        });
    }

    private void HandleTrigger(string processName)
    {
        if (_disposed)
        {
            return;
        }

        var config = _configManager.Config;
        var now = DateTime.UtcNow;

        // 开机保护期：忽略程序刚启动时出现的进程，避免随系统自启的进程被误判。
        if (now < _graceUntilUtc)
        {
            var left = (int)Math.Ceiling((_graceUntilUtc - now).TotalSeconds);
            Log.Info($"检测到 {processName}，但仍在开机保护期内（剩余 {left} 秒），忽略。");
            return;
        }

        if (_busy)
        {
            Log.Debug($"忽略重复触发（正在处理中）：{processName}");
            return;
        }

        var delaySeconds = config.EffectiveLaunchDelaySeconds +
                           (config.EffectiveJitterSeconds > 0
                               ? Random.Shared.Next(0, config.EffectiveJitterSeconds + 1)
                               : 0);

        _busy = true;

        if (delaySeconds > 0)
        {
            SetStatus(EngineStatus.Delay, $"检测到 {processName}，{delaySeconds} 秒后启动 Galgame");
            Log.Info($"检测到 {processName}，将于 {delaySeconds} 秒后尝试启动 Galgame。");
            lock (_gate)
            {
                _launchTimer?.Dispose();
                _launchTimer = new System.Threading.Timer(
                    _ => TryLaunch(processName),
                    null,
                    TimeSpan.FromSeconds(delaySeconds),
                    Timeout.InfiniteTimeSpan);
            }
        }
        else
        {
            SetStatus(EngineStatus.Launching, "立即启动 Galgame");
            _ = Task.Run(() => TryLaunch(processName));
        }
    }

    private void TryLaunch(string triggerProcess)
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            var config = _configManager.Config;

            // 概率判断
            var probability = config.EffectiveProbabilityPercent;
            if (probability < 100)
            {
                var roll = Random.Shared.Next(0, 100);
                if (roll >= probability)
                {
                    Log.Info($"本次按概率跳过（{roll} >= {probability}%）。");
                    SetStatus(EngineStatus.Waiting, WaitingMessage(config));
                    return;
                }
            }

            // 每个 Galgame 同时只允许一个实例：已在运行的游戏不参与本次挑选。
            var entry = config.RandomLaunch
                ? _library.PickRandom(GameLauncher.IsRunning)
                : _library.PickNext(GameLauncher.IsRunning);
            if (entry is null)
            {
                var message = _library.EligibleCount == 0
                    ? "游戏库为空或所有游戏不可用，请先扫描或检查路径。"
                    : "所有可用 Galgame 都已在运行，本次不重复启动。";
                Log.Warn(message);
                SetStatus(EngineStatus.Waiting, WaitingMessage(config));
                Notify?.Invoke(message);
                return;
            }

            SetStatus(EngineStatus.Launching, $"正在启动：{entry.DisplayName}");

            if (GameLauncher.Launch(entry, out var launchMessage))
            {
                _library.MarkLaunched(entry.Path);
                _library.Save();

                // 防止刚启动的 Galgame 及其子进程被当成“任意游戏”再次触发。
                _anyGameDetector?.SuppressAfterGalgameLaunch(entry, GalgameLaunchSuppression);

                var now = DateTime.Now;
                _configManager.State.RegisterTrigger(now);
                _configManager.State.LastGameLaunchUtc = DateTime.UtcNow;
                _configManager.State.LastGamePath = entry.Path;
                _configManager.SaveState();

                GameLaunched?.Invoke(entry);
                Notify?.Invoke(launchMessage);
            }
            else
            {
                Notify?.Invoke(launchMessage);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"触发启动流程失败（来源：{triggerProcess}）", ex);
        }
        finally
        {
            _busy = false;
            SetStatus(EngineStatus.Waiting, WaitingMessage(_configManager.Config));
        }
    }

    internal static string WaitingMessage(AppConfig config) =>
        config.TriggerAnyGame ? "等待游戏启动（War Thunder / 任意游戏）" : "等待 War Thunder";

    private void SetStatus(EngineStatus status, string message)
    {
        Status = status;
        lock (_gate)
        {
            _lastMessage = message;
        }

        try
        {
            StatusChanged?.Invoke(status, message);
        }
        catch
        {
            // 忽略订阅方异常
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

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
