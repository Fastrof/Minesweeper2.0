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
/// 文件不存在时创建默认值并落盘；内容损坏时先备份成 config.corrupt.&lt;时间戳&gt;.json、再重建默认配置，
/// 不让配置问题阻断游戏启动，也不静默丢掉玩家手改坏了的内容。
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

    /// <summary>读取配置；不存在时创建默认配置并落盘，损坏时先备份再重建默认配置。</summary>
    public static ItemConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            ItemConfig created = ItemConfig.CreateDefault();
            Save(created, path);
            return created;
        }

        string json;
        try
        {
            json = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (IOException)
        {
            // 读不出来（被占用等）就先按默认值跑，不阻断启动
            return ItemConfig.CreateDefault();
        }

        ItemConfig? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<ItemConfig>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            // 损坏的文件不能静默丢弃：先留一份 config.corrupt.*.json，再重建默认配置写回
            return RecoverFromCorrupt(path);
        }

        if (loaded is null)
        {
            return RecoverFromCorrupt(path);
        }

        bool migrated = Migrate(loaded);
        loaded.Normalize();

        // 迁移过就立刻写回，否则玩家每次启动都要重新迁一遍，而且看不到新增的键。
        // 写回失败只吞掉写文件本身的异常，**不能整体回默认**：磁盘上只是还留着旧结构，
        // 内存里的迁移结果是对的；原来的写法把 IOException 也算作"读失败"返回默认，
        // 等于因为写不进去就把玩家手改的配置全清了。
        if (migrated)
        {
            try
            {
                Save(loaded, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Core 层没有日志：下次启动会再迁移一次，本次照常使用迁移后的内存配置。
            }
        }

        return loaded;
    }

    /// <summary>
    /// 把旧版配置迁到当前结构（返回是否改过东西）。
    ///
    /// 为什么必须显式迁移：反序列化只会给"文件里没有的键"用默认值，
    /// 而 v1.4 的两个关键变化一个在**旧文件里已经存在**（`maxHold: 3` 会被原样读进来，
    /// 于是商店永远"满仓买不了"），另一个**改了键名**（`radius` → `width`/`height`）。
    /// 光靠默认值兜不住，必须按 schemaVersion 判断后覆盖。
    ///
    /// 注意 <see cref="ItemConfig.SchemaVersion"/> 的默认值是 0（不是当前版本），
    /// 否则"老文件没有这个键 → 读出来就是当前版本 → 不迁移"，这条链会静默失效。
    /// </summary>
    private static bool Migrate(ItemConfig config)
    {
        if (config.SchemaVersion >= ItemConfig.CurrentSchemaVersion)
        {
            return false;
        }

        // v1 → v2：持有上限 3 → 9（让金币商店对老玩家也真正可用）
        if (config.SchemaVersion < 2 && config.MagnifierMaxHold < ItemConfig.DefaultMaxHold)
        {
            config.MagnifierMaxHold = ItemConfig.DefaultMaxHold;
        }

        // 旧键 item.magnifier.radius 已废弃；宽高缺失时由属性默认值补 2×2，无需额外处理。

        config.SchemaVersion = ItemConfig.CurrentSchemaVersion;
        return true;
    }

    /// <summary>
    /// 写回配置。
    ///
    /// 走 <see cref="AtomicFile"/> 原子写盘（先写 .tmp 再替换），不再直接覆盖目标文件：
    /// 手改的配置中途被杀进程写坏，会连带把"新玩家第一次启动就创建默认文件"这条路径搞成永久损坏。
    /// </summary>
    public static void Save(ItemConfig config, string path)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        config.Normalize();
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(config, SerializerOptions));
    }

    /// <summary>
    /// 损坏分支：备份原文件后重建一份默认配置。
    ///
    /// 只备份不重建的话原文件会一直"坏着"：每次启动都再备份一份（corrupt 文件越攒越多），
    /// 玩家也永远看不到一份可用的新配置。写回失败只影响下次启动是否还走损坏分支，
    /// 不能影响本次返回默认值。这个流程与其它存档的 RecoverFromCorrupt 一致。
    /// </summary>
    private static ItemConfig RecoverFromCorrupt(string path)
    {
        ItemConfig fresh = ItemConfig.CreateDefault();

        BackupCorrupt(path);

        try
        {
            Save(fresh, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Core 层没有日志：本次照常按默认值跑，下次启动再尝试重建
        }

        return fresh;
    }

    /// <summary>
    /// 把损坏的配置文件备份成 config.corrupt.&lt;时间戳&gt;.json。
    /// 命名与 <see cref="ProgressStore"/> 等其它存档保持一致，方便玩家按同一个规律找回。
    /// </summary>
    private static void BackupCorrupt(string path)
    {
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            string name = Path.GetFileNameWithoutExtension(path);
            File.Copy(path, Path.Combine(directory, $"{name}.corrupt.{stamp}.json"), overwrite: true);
        }
        catch (IOException)
        {
            // 备份失败不应该阻断游戏启动
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>把配置序列化成字符串（测试与"显示当前参数"用）。</summary>
    public static string ToJson(ItemConfig config) => JsonSerializer.Serialize(config, SerializerOptions);
}
