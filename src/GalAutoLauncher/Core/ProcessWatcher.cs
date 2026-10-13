using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using Timer = System.Threading.Timer;

namespace GalAutoLauncher;

/// <summary>一次“新进程启动”事件。</summary>
internal readonly record struct ProcessStartEvent(string ProcessName, int Pid, string? ExecutablePath);

/// <summary>
/// 监听进程的“新实例启动”事件。
///
/// 支持两种模式：
///   - 指定进程名模式（War Thunder 等精确监听）；
///   - 全部进程模式（用于“任意非 Galgame 游戏”识别，由上层再过滤）。
///
/// 优先使用 WMI 的 Win32_ProcessStartTrace（内核级、精确），
/// 在不支持（非管理员/受限）时回退到轮询进程快照并做差分，
/// 仅对“新出现”的进程触发，避免开机残留 / 更新器 / 崩溃恢复误触发。
/// </summary>
internal sealed class ProcessWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<string> _targets;
    private readonly bool _monitorAll;
    private readonly HashSet<int> _knownPids = new();
    private readonly ConcurrentQueue<ProcessStartEvent> _pending = new();
    private readonly Timer _pollTimer;

    private ManagementEventWatcher? _wmiWatcher;
    private bool _wmiActive;
    private bool _started;
    private bool _disposed;
    private int _pollIntervalMs;
    private volatile bool _polling;

    public ProcessWatcher(IEnumerable<string> targetProcessNames, int pollIntervalSeconds, bool monitorAllProcesses = false)
    {
        _targets = new HashSet<string>(
            targetProcessNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(Normalize),
            StringComparer.OrdinalIgnoreCase);

        _monitorAll = monitorAllProcesses;
        _pollIntervalMs = Math.Clamp(pollIntervalSeconds, 1, 60) * 1000;
        _pollTimer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>检测到目标进程新实例时触发（已在后台线程，注意 marshal 到 UI）。</summary>
    public event Action<ProcessStartEvent>? ProcessStarted;

    public bool IsWmiActive
    {
        get { lock (_gate) { return _wmiActive; } }
    }

    public string DetectionMode => IsWmiActive ? "WMI 实时事件" : "进程轮询";

    public bool MonitorAllProcesses => _monitorAll;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        // 记录当前已存在的进程，作为“已知基线”，避免把开机残留当成新启动。
        SnapshotInto(_knownPids);

        if (!TryStartWmi())
        {
            Log.Info("WMI 进程事件不可用，改用轮询检测。");
        }
        else
        {
            Log.Info("WMI 进程启动事件已启用。");
        }

        // 轮询始终运行：既是 WMI 的回退，也可弥补 WMI 个别遗漏。
        _pollTimer.Change(_pollIntervalMs, _pollIntervalMs);
    }

    public void UpdatePollInterval(int seconds)
    {
        _pollIntervalMs = Math.Clamp(seconds, 1, 60) * 1000;
        lock (_gate)
        {
            if (_started && !_disposed)
            {
                _pollTimer.Change(_pollIntervalMs, _pollIntervalMs);
            }
        }
    }

    private bool TryStartWmi()
    {
        try
        {
            var query = new WqlEventQuery(
                "SELECT * FROM Win32_ProcessStartTrace");
            var watcher = new ManagementEventWatcher(query);
            watcher.EventArrived += OnWmiProcessStarted;
            watcher.Start();

            lock (_gate)
            {
                _wmiWatcher = watcher;
                _wmiActive = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"启动 WMI 监听失败：{ex.Message}");
            lock (_gate)
            {
                _wmiActive = false;
            }

            return false;
        }
    }

    private void OnWmiProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var name = e.NewEvent.Properties["ProcessName"]?.Value as string;
            var pidValue = e.NewEvent.Properties["ProcessID"]?.Value;
            var pid = pidValue is null ? 0 : Convert.ToInt32(pidValue);

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!_monitorAll && !_targets.Contains(Normalize(name)))
            {
                return;
            }

            // 记录 PID，避免轮询对同一进程二次触发。
            lock (_gate)
            {
                if (pid > 0)
                {
                    if (!_knownPids.Add(pid))
                    {
                        return;
                    }
                }
            }

            _pending.Enqueue(new ProcessStartEvent(name, pid, TryGetExecutablePath(pid)));
            Drain();
        }
        catch (Exception ex)
        {
            Log.Debug($"处理 WMI 事件出错：{ex.Message}");
        }
    }

    private void Poll()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            var current = new HashSet<int>();
            SnapshotInto(current, out var nameByPid);

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                foreach (var pid in current)
                {
                    if (_knownPids.Contains(pid))
                    {
                        continue;
                    }

                    if (!nameByPid.TryGetValue(pid, out var name))
                    {
                        continue;
                    }

                    if (_monitorAll || _targets.Contains(Normalize(name)))
                    {
                        _pending.Enqueue(new ProcessStartEvent(name, pid, TryGetExecutablePath(pid)));
                    }
                }

                _knownPids.Clear();
                foreach (var pid in current)
                {
                    _knownPids.Add(pid);
                }
            }

            Drain();
        }
        catch (Exception ex)
        {
            Log.Debug($"轮询进程失败：{ex.Message}");
        }
        finally
        {
            _polling = false;
        }
    }

    private void Drain()
    {
        while (_pending.TryDequeue(out var started))
        {
            try
            {
                ProcessStarted?.Invoke(started);
            }
            catch (Exception ex)
            {
                Log.Error("进程事件处理异常", ex);
            }
        }
    }

    private static void SnapshotInto(HashSet<int> target) => SnapshotInto(target, out _);

    private static void SnapshotInto(HashSet<int> target, out Dictionary<int, string> nameByPid)
    {
        nameByPid = new Dictionary<int, string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                target.Add(process.Id);
                nameByPid[process.Id] = process.ProcessName;
            }
            catch
            {
                // 某些系统进程无法访问，忽略
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string? TryGetExecutablePath(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string processName)
    {
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _pollTimer.Dispose();

        var watcher = _wmiWatcher;
        if (watcher is not null)
        {
            try
            {
                watcher.EventArrived -= OnWmiProcessStarted;
                watcher.Stop();
                watcher.Dispose();
            }
            catch
            {
                // 忽略清理异常
            }
        }
    }
}
