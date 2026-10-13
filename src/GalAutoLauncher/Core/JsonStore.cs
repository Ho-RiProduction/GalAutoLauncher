using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalAutoLauncher;

/// <summary>
/// 统一的 JSON 读写（UTF-8、缩进、中文不转义、原子写入）。
/// </summary>
internal static class JsonStore
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new GameEntryConverter());
        return options;
    }

    public static T Load<T>(string path, Func<T> factory) where T : class
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var value = JsonSerializer.Deserialize<T>(json, Options);
                    if (value is not null)
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"读取 {Path.GetFileName(path)} 失败，将使用默认值：{ex.Message}");
        }

        var created = factory();
        try
        {
            Save(path, created);
        }
        catch
        {
            // 首次写入失败不致命
        }

        return created;
    }

    public static void Save<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, Options);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));

        try
        {
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch
        {
            File.Copy(temp, path, true);
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // 忽略
            }
        }
    }
}
