using System.Text.Json;

namespace GalAutoLauncher;

/// <summary>
/// 游戏库：加载/保存 games.json，提供随机挑选与黑名单过滤。
/// </summary>
internal sealed class GameLibrary
{
    private readonly object _gate = new();
    private readonly List<GameEntry> _entries = new();
    private readonly Random _random = new();

    /// <summary>随机“抽签袋”：每轮把全部可用游戏打乱后依次取用，保证每个游戏权重相同且不会连续重复。</summary>
    private readonly List<string> _bag = new();
    private string? _lastPickedPath;

    public event Action? Changed;

    public string FilePath { get; }

    public GameLibrary(string filePath)
    {
        FilePath = filePath;
    }

    public IReadOnlyList<GameEntry> All
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public int Count
    {
        get { lock (_gate) { return _entries.Count; } }
    }

    public int EligibleCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count(e => !e.Blacklisted && File.Exists(e.Path));
            }
        }
    }

    public void Load()
    {
        var loaded = JsonStore.Load(FilePath, () => new List<GameEntry>());
        lock (_gate)
        {
            _entries.Clear();
            _entries.AddRange(loaded.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Path)));
        }

        Changed?.Invoke();
    }

    public void Save()
    {
        List<GameEntry> snapshot;
        lock (_gate)
        {
            snapshot = _entries.ToList();
        }

        try
        {
            JsonStore.Save(FilePath, snapshot);
        }
        catch (Exception ex)
        {
            Log.Error("保存 games.json 失败", ex);
        }
    }

    /// <summary>用扫描结果替换库（保留已有条目的黑名单/权重/名称设置）。</summary>
    public int Replace(IEnumerable<GameEntry> scanned)
    {
        lock (_gate)
        {
            var existing = _entries.ToDictionary(
                e => e.Path,
                e => e,
                StringComparer.OrdinalIgnoreCase);

            var merged = new List<GameEntry>();
            foreach (var item in scanned)
            {
                if (existing.TryGetValue(item.Path, out var old))
                {
                    // 保留用户自定义字段，但刷新引擎识别结果。
                    old.Engine = item.Engine;
                    if (string.IsNullOrWhiteSpace(old.Name))
                    {
                        old.Name = item.Name;
                    }

                    merged.Add(old);
                }
                else
                {
                    merged.Add(item);
                }
            }

            _entries.Clear();
            _entries.AddRange(merged);
            var count = _entries.Count;
            Changed?.Invoke();
            return count;
        }
    }

    public void Add(GameEntry entry)
    {
        lock (_gate)
        {
            if (_entries.Any(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _entries.Add(entry);
        }

        Changed?.Invoke();
    }

    public void Remove(string path)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        Changed?.Invoke();
    }

    public void SetBlacklisted(string path, bool blacklisted)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                entry.Blacklisted = blacklisted;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// 随机挑选一个可用条目。所有游戏权重相同（忽略 Weight 字段），
    /// 采用“抽签袋”方式：每轮把全部可用游戏打乱后依次取用，
    /// 因此每个游戏被抽到的概率一致，并且在一轮之内不会重复，
    /// 两轮交界处也不会连续抽到同一个游戏。
    /// </summary>
    public GameEntry? PickRandom(Func<GameEntry, bool>? exclude = null)
    {
        lock (_gate)
        {
            var candidates = _entries
                .Where(e => !e.Blacklisted && File.Exists(e.Path))
                .Where(e => exclude is null || !exclude(e))
                .ToList();

            if (candidates.Count == 0)
            {
                _bag.Clear();
                return null;
            }

            // 清掉已失效（被禁用/删除/路径变化）的袋中条目。
            _bag.RemoveAll(path => !candidates.Any(c =>
                string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)));

            if (_bag.Count == 0)
            {
                _bag.AddRange(candidates.Select(c => c.Path));
                Shuffle(_bag);

                // 新一轮的第一个不要和上一轮的最后一个相同，避免跨轮连续重复。
                if (_bag.Count > 1 && _lastPickedPath is not null &&
                    string.Equals(_bag[0], _lastPickedPath, StringComparison.OrdinalIgnoreCase))
                {
                    var swapIndex = _random.Next(1, _bag.Count);
                    (_bag[0], _bag[swapIndex]) = (_bag[swapIndex], _bag[0]);
                }
            }

            var pickedPath = _bag[0];
            _bag.RemoveAt(0);
            _lastPickedPath = pickedPath;

            return candidates.First(c =>
                string.Equals(c.Path, pickedPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void Shuffle(List<string> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>非随机模式下按顺序挑选（优先最近未启动的）。</summary>
    public GameEntry? PickNext(Func<GameEntry, bool>? exclude = null)
    {
        lock (_gate)
        {
            return _entries
                .Where(e => !e.Blacklisted && File.Exists(e.Path))
                .Where(e => exclude is null || !exclude(e))
                .OrderBy(e => e.LastLaunchedUtc ?? DateTime.MinValue)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
    }

    public void MarkLaunched(string path)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                entry.LastLaunchedUtc = DateTime.UtcNow;
            }
        }
    }
}
