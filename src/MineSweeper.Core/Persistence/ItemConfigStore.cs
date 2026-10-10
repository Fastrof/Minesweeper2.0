using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MineSweeper.Core.Items;

namespace MineSweeper.Core.Persistence;

/// <summary>
/// 道具配置的读写（§5.7）。
/// config.json 用文档里的点号键名，手改即可调手感，不需要重新编译：
/// <code>
/// {
///   "item.magnifier.threshold": 20,
///   "item.magnifier.maxHold": 3,
///   "item.magnifier.initialGift": 1,
///   "item.magnifier.radius": 1
/// }
/// </code>
/// 文件不存在或损坏时一律退回默认值并重写一份，不让配置问题阻断游戏启动。
/// </summary>
public static class ItemConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取配置；不存在时创建默认配置并落盘。</summary>
    public static ItemConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            ItemConfig created = ItemConfig.CreateDefault();
            Save(created, path);
            return created;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            ItemConfig? loaded = JsonSerializer.Deserialize<ItemConfig>(json, SerializerOptions);
            if (loaded is null)
            {
                return ItemConfig.CreateDefault();
            }

            loaded.Normalize();
            return loaded;
        }
        catch (JsonException)
        {
            return ItemConfig.CreateDefault();
        }
        catch (IOException)
        {
            return ItemConfig.CreateDefault();
        }
    }

    /// <summary>写回配置。</summary>
    public static void Save(ItemConfig config, string path)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        config.Normalize();

        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, JsonSerializer.Serialize(config, SerializerOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>把配置序列化成字符串（测试与"显示当前参数"用）。</summary>
    public static string ToJson(ItemConfig config) => JsonSerializer.Serialize(config, SerializerOptions);
}
