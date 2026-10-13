namespace GalAutoLauncher;

/// <summary>
/// 持久化运行状态（state.json）：冷却、最近一次启动、当日触发次数。
/// </summary>
internal sealed class AppState
{
    public DateTime? LastTriggerUtc { get; set; }

    public DateTime? LastGameLaunchUtc { get; set; }

    public string? LastGamePath { get; set; }

    /// <summary>当日已触发次数，用于“当天只触发一次”策略。</summary>
    public int TriggersToday { get; set; }

    public DateTime? TriggerCountDate { get; set; }

    public DateTime? LastScanUtc { get; set; }

    public int LastScanCount { get; set; }

    public bool IsOnCooldown(DateTime utcNow, int cooldownMinutes)
    {
        if (cooldownMinutes <= 0 || LastTriggerUtc is null)
        {
            return false;
        }

        return utcNow - LastTriggerUtc.Value < TimeSpan.FromMinutes(cooldownMinutes);
    }

    public TimeSpan RemainingCooldown(DateTime utcNow, int cooldownMinutes)
    {
        if (cooldownMinutes <= 0 || LastTriggerUtc is null)
        {
            return TimeSpan.Zero;
        }

        var remaining = TimeSpan.FromMinutes(cooldownMinutes) - (utcNow - LastTriggerUtc.Value);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public int GetTriggersToday(DateTime localNow)
    {
        if (TriggerCountDate is null || TriggerCountDate.Value.Date != localNow.Date)
        {
            return 0;
        }

        return TriggersToday;
    }

    public void RegisterTrigger(DateTime localNow)
    {
        var utcNow = DateTime.UtcNow;
        LastTriggerUtc = utcNow;

        if (TriggerCountDate is null || TriggerCountDate.Value.Date != localNow.Date)
        {
            TriggerCountDate = localNow;
            TriggersToday = 1;
        }
        else
        {
            TriggersToday++;
        }
    }
}
