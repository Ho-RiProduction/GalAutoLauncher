using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalAutoLauncher;

internal enum GameEngine
{
    Unknown = 0,
    Kirikiri = 1,
    Unity = 2,
    RenPy = 3,
    Generic = 4,
    NScripter = 5,
    Bgi = 6,
    Azarashi = 7,
    Hg3 = 8,
}

/// <summary>
/// 一个可启动的 Galgame 条目。为兼容旧版纯字符串数组，
/// 序列化时若只有路径则写成字符串，否则写成对象。
/// </summary>
internal sealed class GameEntry
{
    public string Path { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameEngine Engine { get; set; } = GameEngine.Unknown;

    public string Name { get; set; } = string.Empty;

    public bool Blacklisted { get; set; }

    /// <summary>相对权重（&gt;=1）。默认 1。</summary>
    public int Weight { get; set; } = 1;

    public DateTime? LastLaunchedUtc { get; set; }

    public static GameEntry FromPath(string path) => new()
    {
        Path = path,
        Engine = GameEngine.Unknown,
    };

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name)
            ? Name
            : System.IO.Path.GetFileNameWithoutExtension(Path);
}

internal sealed class GameEntryConverter : JsonConverter<GameEntry>
{
    public override GameEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return GameEntry.FromPath(reader.GetString() ?? string.Empty);

            case JsonTokenType.Null:
                return new GameEntry();

            case JsonTokenType.StartObject:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;

                var entry = new GameEntry();
                if (TryGet(root, "Path", out var path)) entry.Path = path.GetString() ?? string.Empty;
                if (TryGet(root, "Engine", out var engine))
                {
                    if (engine.ValueKind == JsonValueKind.Number && engine.TryGetInt32(out var number))
                        entry.Engine = (GameEngine)number;
                    else if (engine.ValueKind == JsonValueKind.String &&
                             Enum.TryParse<GameEngine>(engine.GetString(), true, out var parsed))
                        entry.Engine = parsed;
                }
                if (TryGet(root, "Name", out var name)) entry.Name = name.GetString() ?? string.Empty;
                if (TryGet(root, "Blacklisted", out var black) &&
                    (black.ValueKind == JsonValueKind.True || black.ValueKind == JsonValueKind.False))
                    entry.Blacklisted = black.GetBoolean();
                if (TryGet(root, "Weight", out var weight) && weight.TryGetInt32(out var w))
                    entry.Weight = w;
                if (TryGet(root, "LastLaunchedUtc", out var last) &&
                    last.ValueKind == JsonValueKind.String &&
                    last.TryGetDateTime(out var when))
                    entry.LastLaunchedUtc = when;

                return entry;
            }

            default:
                throw new JsonException($"无法解析 games.json 中的条目（token={reader.TokenType}）。");
        }
    }

    public override void Write(Utf8JsonWriter writer, GameEntry value, JsonSerializerOptions options)
    {
        // 精简输出：仅路径的条目直接写成字符串。
        if (value.Engine == GameEngine.Unknown &&
            string.IsNullOrEmpty(value.Name) &&
            !value.Blacklisted &&
            value.Weight == 1 &&
            value.LastLaunchedUtc is null)
        {
            writer.WriteStringValue(value.Path);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("Path", value.Path);
        writer.WriteString("Engine", value.Engine.ToString());
        if (!string.IsNullOrEmpty(value.Name)) writer.WriteString("Name", value.Name);
        if (value.Blacklisted) writer.WriteBoolean("Blacklisted", true);
        if (value.Weight != 1) writer.WriteNumber("Weight", value.Weight);
        if (value.LastLaunchedUtc is { } last) writer.WriteString("LastLaunchedUtc", last);
        writer.WriteEndObject();
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value) ||
        element.TryGetProperty(name.ToLowerInvariant(), out value);
}
