using System.Globalization;
using System.Text.Json;

namespace MineSweeper.Core.Updates;

/// <summary>发行版附带的一个可下载文件（GitHub release 的 assets 里的一项）。</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>
/// 一次「检查更新」拿到的发行信息（对应 GitHub releases/latest 的一份响应）。
/// <see cref="Version"/> 是规范化之后的版本号（"v1.1.0" → "1.1.0"），
/// 比较版本、判断"是否提示过"都只用它，<see cref="TagName"/> 只作展示与回查。
/// </summary>
public sealed record ReleaseInfo(
    string Version,
    string TagName,
    string DisplayName,
    string Notes,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// GitHub <c>GET /repos/{owner}/{repo}/releases/latest</c> 响应的解析。
///
/// 这是整个"检查更新"里唯一直接吃外部输入的地方，所以按<b>最坏情况</b>设计：
/// 玩家的网络可能经过代理（塞 BOM、改大小写、返回错误页），GitHub 也可能改字段。
/// 因此这里刻意不用 <c>JsonSerializer.Deserialize&lt;T&gt;</c> 直接映射 DTO —— 那样任何一个
/// 字段类型不符都会整体抛异常，检查更新就变成"崩给玩家看"。改成走 <see cref="JsonDocument"/>：
/// 逐字段取、取得出来就用、取不出来就退化，只有"根本不是 JSON 对象"才算失败。
/// </summary>
public static class ReleaseJson
{
    /// <summary>解析文档时的宽容设置：注释与尾逗号（自动化工具改过的响应）都放过。</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// 解析 GitHub latest release 的 JSON 文本。
    /// 成功返回 true 并给出 <paramref name="release"/>；失败返回 false 并把中文原因写进
    /// <paramref name="error"/>（可直接展示给玩家），<b>任何输入都不会抛异常</b>。
    ///
    /// 容错范围：BOM、键名大小写不同、缺少可选字段（name/body/html_url/published_at/assets/url 字段类型不符）、
    /// 版本号里带 "v" 前缀或预发布后缀。
    /// 只有这几种情况算失败：内容为空、不是合法 JSON、根节点不是对象、连 tag_name 与 name 都没有。
    /// </summary>
    public static bool TryParse(string? json, out ReleaseInfo? release, out string? error)
    {
        release = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "更新响应为空，无法解析发行信息。";
            return false;
        }

        // 有些代理/CDN 会把 UTF-8 BOM 原样留在响应体里；对字符串输入来说那是一个 U+FEFF 字符，
        // System.Text.Json 视其为非法起始字符 —— 明明合法的 JSON 会被判成损坏，所以先剥掉。
        string text = json.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        try
        {
            using JsonDocument document = JsonDocument.Parse(text, DocumentOptions);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "更新响应的根节点不是对象（可能是错误页或数组），无法解析发行信息。";
                return false;
            }

            string? tagName = ReadString(root, "tag_name");
            string? name = ReadString(root, "name");

            // 两个版本来源都没有（或都不是字符串）时，拿到的就是一条无法判断新旧的记录，
            // 与其返回一个空版本让上层全靠兜底，不如在这里明确报错。
            if (string.IsNullOrWhiteSpace(tagName) && string.IsNullOrWhiteSpace(name))
            {
                error = "更新响应里缺少 tag_name（也没有 name），无法识别版本号。";
                return false;
            }

            string version = NormalizeTag(FirstNonEmpty(tagName, name));

            release = new ReleaseInfo(
                version,
                tagName ?? string.Empty,
                FirstNonEmpty(name, tagName),
                ReadString(root, "body") ?? string.Empty,
                ReadString(root, "html_url") ?? string.Empty,
                ReadDate(root, "published_at"),
                ReadAssets(root));

            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"更新响应不是合法 JSON：{ex.Message}";
            return false;
        }
    }

    /// <summary>tag 名去前缀 + 规范化：只有 "v" 后面紧跟数字时才算前缀，避免把 "version-2" 砍成 "ersion-2"。</summary>
    private static string NormalizeTag(string? tag)
    {
        string text = (tag ?? string.Empty).Trim();
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V') && char.IsAsciiDigit(text[1]))
        {
            text = text[1..];
        }

        return AppVersion.Normalize(text);
    }

    private static string FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first! : second ?? string.Empty;

    /// <summary>
    /// 大小写不敏感地取属性。GitHub 目前全用小写下划线键名，但代理或将来改名都可能变大小写，
    /// 所以这里手写枚举比较，而不是依赖序列化器的 case-insensitive 开关。
    /// </summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>取字符串字段；字段缺失或类型不符都返回 null（由调用方退化）。</summary>
    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>取时间字段；缺失或格式不认识都返回 null。</summary>
    private static DateTimeOffset? ReadDate(JsonElement element, string name)
    {
        string? text = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    /// <summary>取下载列表；不是数组、或某一项不是对象时，跳过坏的、保留好的。</summary>
    private static IReadOnlyList<ReleaseAsset> ReadAssets(JsonElement root)
    {
        if (!TryGetProperty(root, "assets", out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ReleaseAsset>();
        }

        var assets = new List<ReleaseAsset>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            assets.Add(new ReleaseAsset(
                ReadString(item, "name") ?? string.Empty,
                ReadString(item, "browser_download_url") ?? string.Empty,
                ReadLong(item, "size")));
        }

        return assets;
    }

    /// <summary>取整数字段；GitHub 给的是数字，但被文本化（"123"）也认。</summary>
    private static long ReadLong(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed;
        }

        return 0;
    }
}
