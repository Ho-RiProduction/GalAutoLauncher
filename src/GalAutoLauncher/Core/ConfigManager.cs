namespace GalAutoLauncher;

/// <summary>
/// 配置与状态的持有者，负责加载、保存、以及向订阅者广播变更。
/// </summary>
internal sealed class ConfigManager
{
    public AppConfig Config { get; private set; } = new();
    public AppState State { get; private set; } = new();

    public event Action? ConfigChanged;

    public void Load()
    {
        Config = JsonStore.Load(AppInfo.ConfigPath, () => new AppConfig());
        Config.Normalize();

        State = JsonStore.Load(AppInfo.StatePath, () => new AppState());

        Log.Initialize(AppInfo.LogDirectory, Config.EffectiveLogRetentionDays);
        Log.Info($"配置已加载：{AppInfo.ConfigPath}");
    }

    public void Save()
    {
        try
        {
            JsonStore.Save(AppInfo.ConfigPath, Config);
        }
        catch (Exception ex)
        {
            Log.Error("保存 config.json 失败", ex);
        }

        ConfigChanged?.Invoke();
    }

    public void SaveState()
    {
        try
        {
            JsonStore.Save(AppInfo.StatePath, State);
        }
        catch (Exception ex)
        {
            Log.Error("保存 state.json 失败", ex);
        }
    }

    /// <summary>测试注入用：直接替换配置与状态，不触碰磁盘。</summary>
    internal void SetConfigForTesting(AppConfig config, AppState state)
    {
        config.Normalize();
        Config = config;
        State = state;
    }

    public void Update(Action<AppConfig> mutate)
    {
        mutate(Config);
        Config.Normalize();
        Save();
    }
}
