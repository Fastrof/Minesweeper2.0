using System.Globalization;
using MineSweeper.Core.Persistence;
using MineSweeper.Core.Updates;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// v1.1「检查更新」：版本号解析与比较、GitHub 响应容错解析、检查/提示策略、状态存档。
///
/// 这里的用例集中在<b>不该打扰玩家的那几种情况</b>上 —— 假更新提示（1.1 被当成旧版本）、
/// 同一版本反复弹窗、被跳过的版本又回来，全都由这些用例守着。
/// </summary>
public sealed class UpdateTests
{
    /// <summary>一份标准 GitHub releases/latest 响应（裁剪到本模块用得到的字段）。</summary>
    private const string SampleJson = """
        {
          "url": "https://api.github.com/repos/example/minesweeper/releases/1",
          "html_url": "https://github.com/example/minesweeper/releases/tag/v1.1.0",
          "id": 101,
          "tag_name": "v1.1.0",
          "name": "MineSweeper 1.1.0",
          "draft": false,
          "prerelease": false,
          "created_at": "2026-01-01T00:00:00Z",
          "published_at": "2026-01-02T03:04:05Z",
          "body": "### 新增\n- 检查更新\n- 关卡包",
          "assets": [
            {
              "name": "MineSweeper-1.1.0-x64.zip",
              "browser_download_url": "https://example.com/x64.zip",
              "size": 12345678
            },
            {
              "name": "MineSweeper-1.1.0-arm64.zip",
              "browser_download_url": "https://example.com/arm64.zip",
              "size": 22334455
            }
          ]
        }
        """;

    // ------------------------------------------------------------ 版本号解析

    [Fact]
    public void 版本号解析一律补齐到四段()
    {
        Assert.True(AppVersion.TryParse("1", out Version one));
        Assert.Equal(new Version(1, 0, 0, 0), one);

        Assert.True(AppVersion.TryParse("1.1", out Version two));
        Assert.Equal(new Version(1, 1, 0, 0), two);

        Assert.True(AppVersion.TryParse("1.1.0", out Version three));
        Assert.Equal(new Version(1, 1, 0, 0), three);

        Assert.True(AppVersion.TryParse("1.1.0.0", out Version four));
        Assert.Equal(new Version(1, 1, 0, 0), four);

        Assert.True(AppVersion.TryParse("1.2.3.4", out Version full));
        Assert.Equal(new Version(1, 2, 3, 4), full);
    }

    [Fact]
    public void 版本号解析接受v前缀与首尾空白()
    {
        Assert.True(AppVersion.TryParse("v1.1.0", out Version prefixed));
        Assert.Equal(new Version(1, 1, 0, 0), prefixed);

        Assert.True(AppVersion.TryParse("V2.0", out Version upper));
        Assert.Equal(new Version(2, 0, 0, 0), upper);

        Assert.True(AppVersion.TryParse("  1.1.0  ", out Version padded));
        Assert.Equal(new Version(1, 1, 0, 0), padded);
    }

    /// <summary>预发布后缀忽略掉：beta 版与正式版同号时按同一版本对待，避免 beta 之后再提示一次。</summary>
    [Fact]
    public void 版本号解析忽略预发布后缀()
    {
        Assert.True(AppVersion.TryParse("1.1.0-beta.2", out Version beta));
        Assert.Equal(new Version(1, 1, 0, 0), beta);

        Assert.True(AppVersion.TryParse("v1.1.0+build.7", out Version build));
        Assert.Equal(new Version(1, 1, 0, 0), build);

        Assert.True(AppVersion.TryParse("1.1-rc1", out Version rc));
        Assert.Equal(new Version(1, 1, 0, 0), rc);
    }

    [Fact]
    public void 版本号解析拒绝空串与乱码()
    {
        Assert.False(AppVersion.TryParse(null, out _));
        Assert.False(AppVersion.TryParse(string.Empty, out _));
        Assert.False(AppVersion.TryParse("   ", out _));
        Assert.False(AppVersion.TryParse("乱写的版本", out _));
        Assert.False(AppVersion.TryParse("1.1.x", out _));
        Assert.False(AppVersion.TryParse("1..0", out _));
        Assert.False(AppVersion.TryParse("1.1.0.0.0", out _));
        Assert.False(AppVersion.TryParse("version2", out _));
        Assert.False(AppVersion.TryParse("-1.0", out _));
    }

    /// <summary>核心防呆：只写了 "1.1" 的程序不该把线上的 "1.1.0" 当成新版本。</summary>
    [Fact]
    public void 一点一与一点一点零必须相等()
    {
        Assert.True(AppVersion.TryParse("1.1", out Version shortForm));
        Assert.True(AppVersion.TryParse("1.1.0", out Version longForm));

        Assert.Equal(longForm, shortForm);
        Assert.Equal(0, shortForm.CompareTo(longForm));

        Assert.False(AppVersion.IsNewer("1.1.0", "1.1"));
        Assert.False(AppVersion.IsNewer("1.1", "1.1.0"));
        Assert.False(AppVersion.IsNewer("v1.1.0.0", "1.1"));
    }

    // ------------------------------------------------------------ 新旧比较

    [Fact]
    public void 更高的版本号判定为新版本()
    {
        Assert.True(AppVersion.IsNewer("1.1", "1.0"));
        Assert.True(AppVersion.IsNewer("1.0.1", "1.0"));
        Assert.True(AppVersion.IsNewer("2.0.0", "1.9.9"));
        Assert.True(AppVersion.IsNewer("1.1.0-beta.2", "1.0.9"));
    }

    [Fact]
    public void 相等的版本不算新版本()
    {
        Assert.False(AppVersion.IsNewer("1.1.0", "1.1.0"));
        Assert.False(AppVersion.IsNewer("v1.1.0", "1.1.0"));
        Assert.False(AppVersion.IsNewer("1.1.0.0", "1.1.0"));
    }

    [Fact]
    public void 更低的版本不算新版本()
    {
        Assert.False(AppVersion.IsNewer("1.0", "1.1"));
        Assert.False(AppVersion.IsNewer("1.0.9", "1.0.10"));
        Assert.False(AppVersion.IsNewer("0.9.9", "1.0"));
    }

    /// <summary>宁可漏报不要误报：任一侧解析失败都必须判定为"没有新版本"。</summary>
    [Fact]
    public void 版本号无法解析时不误报新版本()
    {
        // 线上版本坏了
        Assert.False(AppVersion.IsNewer("乱写的版本", "1.0"));
        Assert.False(AppVersion.IsNewer(string.Empty, "1.0"));
        Assert.False(AppVersion.IsNewer(null, "1.0"));

        // 本地版本坏了（比线上旧都不知道，只能不提示）
        Assert.False(AppVersion.IsNewer("1.1", "乱写的版本"));
        Assert.False(AppVersion.IsNewer("1.1", string.Empty));
        Assert.False(AppVersion.IsNewer("1.1", null));

        // 两边都坏
        Assert.False(AppVersion.IsNewer(null, null));
    }

    // ------------------------------------------------------------ 规范化

    [Fact]
    public void 规范化统一为至少三段并去掉多余的零()
    {
        Assert.Equal("1.1.0", AppVersion.Normalize("1.1"));
        Assert.Equal("1.1.0", AppVersion.Normalize("v1.1"));
        Assert.Equal("1.2.0", AppVersion.Normalize("1.2.0.0"));
        Assert.Equal("1.0.0", AppVersion.Normalize("1.0.0.0"));
        Assert.Equal("1.2.3", AppVersion.Normalize("1.2.3.0"));
        Assert.Equal("1.2.3.4", AppVersion.Normalize("  v1.2.3.4  "));
        Assert.Equal("2.0.0", AppVersion.Normalize("2.0.0-beta.1"));
    }

    [Fact]
    public void 规范化失败时返回去空白的原串()
    {
        Assert.Equal("乱写的版本", AppVersion.Normalize("  乱写的版本  "));
        Assert.Equal(string.Empty, AppVersion.Normalize(null));
        Assert.Equal(string.Empty, AppVersion.Normalize("   "));
    }

    // ------------------------------------------------------------ JSON 解析

    [Fact]
    public void 解析标准GitHub响应()
    {
        Assert.True(ReleaseJson.TryParse(SampleJson, out ReleaseInfo? release, out string? error));
        Assert.Null(error);
        Assert.NotNull(release);

        Assert.Equal("1.1.0", release!.Version);
        Assert.Equal("v1.1.0", release.TagName);
        Assert.Equal("MineSweeper 1.1.0", release.DisplayName);
        Assert.Contains("检查更新", release.Notes);
        Assert.Equal("https://github.com/example/minesweeper/releases/tag/v1.1.0", release.HtmlUrl);
        Assert.Equal(
            DateTimeOffset.Parse("2026-01-02T03:04:05Z", CultureInfo.InvariantCulture),
            release.PublishedAt);

        Assert.Equal(2, release.Assets.Count);
        Assert.Equal("MineSweeper-1.1.0-x64.zip", release.Assets[0].Name);
        Assert.Equal("https://example.com/x64.zip", release.Assets[0].DownloadUrl);
        Assert.Equal(12345678, release.Assets[0].Size);
        Assert.Equal(22334455, release.Assets[1].Size);
    }

    /// <summary>可选字段缺失一律降级成空值，不能让一条不完整的响应把检查更新打断。</summary>
    [Fact]
    public void 解析缺字段的响应时降级为默认值()
    {
        const string sparse = """{ "tag_name": "v1.2.0" }""";

        Assert.True(ReleaseJson.TryParse(sparse, out ReleaseInfo? release, out _));
        Assert.NotNull(release);

        Assert.Equal("1.2.0", release!.Version);
        Assert.Equal("v1.2.0", release.TagName);
        Assert.Equal("v1.2.0", release.DisplayName); // name 缺失时退化成 tag
        Assert.Equal(string.Empty, release.Notes);
        Assert.Equal(string.Empty, release.HtmlUrl);
        Assert.Null(release.PublishedAt);
        Assert.Empty(release.Assets);
    }

    [Fact]
    public void 解析时字段类型不符也能降级()
    {
        const string wrongTypes = """
            {
              "tag_name": "v1.3.0",
              "name": 130,
              "body": { "unexpected": true },
              "html_url": 42,
              "published_at": "不是时间",
              "assets": "不是数组"
            }
            """;

        Assert.True(ReleaseJson.TryParse(wrongTypes, out ReleaseInfo? release, out _));
        Assert.NotNull(release);

        Assert.Equal("1.3.0", release!.Version);
        Assert.Equal("v1.3.0", release.DisplayName);
        Assert.Equal(string.Empty, release.Notes);
        Assert.Equal(string.Empty, release.HtmlUrl);
        Assert.Null(release.PublishedAt);
        Assert.Empty(release.Assets);
    }

    [Fact]
    public void 解析tag_name缺失时退化到name()
    {
        const string noTag = """{ "name": "1.4.0", "body": "只有 name" }""";

        Assert.True(ReleaseJson.TryParse(noTag, out ReleaseInfo? release, out _));
        Assert.NotNull(release);

        Assert.Equal("1.4.0", release!.Version);
        Assert.Equal(string.Empty, release.TagName);
        Assert.Equal("1.4.0", release.DisplayName);

        // name 往往是"给人看的名字"而不是纯版本号，这时按原样退化即可：
        // 版本号认不出来 → 策略层拒绝据此提示，不会因为一个展示名就弹更新。
        const string displayName = """{ "name": "MineSweeper 1.4.0" }""";
        Assert.True(ReleaseJson.TryParse(displayName, out ReleaseInfo? fromName, out _));
        Assert.Equal("MineSweeper 1.4.0", fromName!.Version);
        Assert.False(UpdateCheckPolicy.ShouldNotify(new UpdateState(), "1.0.0", fromName, out _));
    }

    [Fact]
    public void 解析键名大小写不同也能识别()
    {
        const string upper = """
            {
              "TAG_NAME": "v1.5.0",
              "Name": "大写键名",
              "HTML_URL": "https://example.com/1.5.0",
              "Published_At": "2026-02-03T00:00:00Z",
              "ASSETS": [ { "NAME": "a.zip", "Browser_Download_URL": "https://example.com/a.zip", "SIZE": 10 } ]
            }
            """;

        Assert.True(ReleaseJson.TryParse(upper, out ReleaseInfo? release, out _));
        Assert.NotNull(release);

        Assert.Equal("1.5.0", release!.Version);
        Assert.Equal("v1.5.0", release.TagName);
        Assert.Equal("大写键名", release.DisplayName);
        Assert.Equal("https://example.com/1.5.0", release.HtmlUrl);
        Assert.NotNull(release.PublishedAt);
        Assert.Single(release.Assets);
        Assert.Equal("a.zip", release.Assets[0].Name);
        Assert.Equal(10, release.Assets[0].Size);
    }

    /// <summary>代理/CDN 常在响应体前面留下 UTF-8 BOM，合法 JSON 不能因此被判成损坏。</summary>
    [Fact]
    public void 解析带BOM的响应()
    {
        string withBom = "\uFEFF" + SampleJson;

        Assert.True(ReleaseJson.TryParse(withBom, out ReleaseInfo? release, out string? error));
        Assert.Null(error);
        Assert.Equal("1.1.0", release!.Version);
        Assert.Equal(2, release.Assets.Count);
    }

    [Fact]
    public void 解析空串与非法JSON返回false并给出中文原因()
    {
        Assert.False(ReleaseJson.TryParse(null, out ReleaseInfo? fromNull, out string? nullError));
        Assert.Null(fromNull);
        Assert.False(string.IsNullOrWhiteSpace(nullError));

        Assert.False(ReleaseJson.TryParse("   ", out _, out string? blankError));
        Assert.False(string.IsNullOrWhiteSpace(blankError));

        Assert.False(ReleaseJson.TryParse("{ 这不是合法 JSON ", out ReleaseInfo? fromBroken, out string? brokenError));
        Assert.Null(fromBroken);
        Assert.Contains("不是合法 JSON", brokenError);

        // 根节点是数组（拿错了接口或落到错误页）也要拒绝
        Assert.False(ReleaseJson.TryParse("""[ { "tag_name": "v1.1.0" } ]""", out ReleaseInfo? fromArray, out string? arrayError));
        Assert.Null(fromArray);
        Assert.Contains("不是对象", arrayError);

        // 两个版本来源都没有：无法识别版本号
        Assert.False(ReleaseJson.TryParse("""{ "body": "没有版本信息" }""", out ReleaseInfo? fromEmpty, out string? emptyError));
        Assert.Null(fromEmpty);
        Assert.Contains("无法识别版本号", emptyError);
    }

    // ------------------------------------------------------------ 检查策略

    [Fact]
    public void 启动检查在间隔内跳过()
    {
        var now = DateTimeOffset.Parse("2026-03-01T12:00:00Z", CultureInfo.InvariantCulture);
        var state = new UpdateState { LastCheckUtc = now.AddHours(-3) };

        Assert.False(UpdateCheckPolicy.ShouldCheck(state, UpdateCheckTrigger.Startup, now, out string reason));
        Assert.Contains("跳过", reason);
    }

    [Fact]
    public void 启动检查超过间隔或从未检查时发起()
    {
        var now = DateTimeOffset.Parse("2026-03-01T12:00:00Z", CultureInfo.InvariantCulture);

        var never = new UpdateState();
        Assert.True(UpdateCheckPolicy.ShouldCheck(never, UpdateCheckTrigger.Startup, now, out _));

        // 恰好满 24 小时：算作到期，应该查
        var exactly = new UpdateState { LastCheckUtc = now - UpdateCheckPolicy.AutoInterval };
        Assert.True(UpdateCheckPolicy.ShouldCheck(exactly, UpdateCheckTrigger.Startup, now, out _));

        var stale = new UpdateState { LastCheckUtc = now.AddDays(-30) };
        Assert.True(UpdateCheckPolicy.ShouldCheck(stale, UpdateCheckTrigger.Startup, now, out _));

        // 系统时间被改小（上次检查"在未来"）时不要去打扰
        var future = new UpdateState { LastCheckUtc = now.AddDays(1) };
        Assert.False(UpdateCheckPolicy.ShouldCheck(future, UpdateCheckTrigger.Startup, now, out _));
    }

    [Fact]
    public void 手动检查不受间隔限制()
    {
        var now = DateTimeOffset.Parse("2026-03-01T12:00:00Z", CultureInfo.InvariantCulture);
        var state = new UpdateState { LastCheckUtc = now.AddMinutes(-1) };

        Assert.True(UpdateCheckPolicy.ShouldCheck(state, UpdateCheckTrigger.Manual, now, out string reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void 存在新版本时应当提示()
    {
        var state = new UpdateState { LatestKnownVersion = "1.0.0", SkippedVersion = "0.9.0" };
        const string json = """{ "tag_name": "v1.1.0", "name": "1.1.0" }""";
        Assert.True(ReleaseJson.TryParse(json, out ReleaseInfo? release, out _));

        Assert.True(UpdateCheckPolicy.ShouldNotify(state, "1.0.0", release!, out string reason));
        Assert.Contains("1.1.0", reason);
    }

    [Fact]
    public void 已是最新版本时不提示()
    {
        var state = new UpdateState();
        const string json = """{ "tag_name": "v1.0.0" }""";
        Assert.True(ReleaseJson.TryParse(json, out ReleaseInfo? release, out _));

        Assert.False(UpdateCheckPolicy.ShouldNotify(state, "1.0.0", release!, out string reason));
        Assert.Contains("已是最新版本", reason);
    }

    [Fact]
    public void 玩家跳过的版本不提示()
    {
        var state = new UpdateState { SkippedVersion = "1.1.0" };
        const string json = """{ "tag_name": "v1.1.0" }""";
        Assert.True(ReleaseJson.TryParse(json, out ReleaseInfo? release, out _));

        Assert.False(UpdateCheckPolicy.ShouldNotify(state, "1.0.0", release!, out string reason));
        Assert.Contains("已被玩家跳过", reason);

        // 手改成带 v 的写法（存档可能被改过）也应当挡住
        var handwritten = new UpdateState { SkippedVersion = "v1.1.0" };
        Assert.False(UpdateCheckPolicy.ShouldNotify(handwritten, "1.0.0", release!, out _));
    }

    [Fact]
    public void 已经提示过的版本不重复提示()
    {
        var state = new UpdateState { LatestKnownVersion = "1.1.0" };
        const string json = """{ "tag_name": "v1.1.0" }""";
        Assert.True(ReleaseJson.TryParse(json, out ReleaseInfo? release, out _));

        Assert.False(UpdateCheckPolicy.ShouldNotify(state, "1.0.0", release!, out string reason));
        Assert.Contains("已经提示过", reason);

        // 更新的版本仍然要提示
        const string newer = """{ "tag_name": "v1.2.0" }""";
        Assert.True(ReleaseJson.TryParse(newer, out ReleaseInfo? next, out _));
        Assert.True(UpdateCheckPolicy.ShouldNotify(state, "1.0.0", next!, out _));
    }

    [Fact]
    public void 版本号无法识别时不提示()
    {
        var state = new UpdateState();
        const string json = """{ "tag_name": "每夜构建" }""";
        Assert.True(ReleaseJson.TryParse(json, out ReleaseInfo? release, out _));

        Assert.False(UpdateCheckPolicy.ShouldNotify(state, "1.0.0", release!, out string reason));
        Assert.Contains("无法识别", reason);

        // 本地版本号同样无法识别时也不能误报
        const string normal = """{ "tag_name": "v1.1.0" }""";
        Assert.True(ReleaseJson.TryParse(normal, out ReleaseInfo? ok, out _));
        Assert.False(UpdateCheckPolicy.ShouldNotify(state, "不知道", ok!, out _));
    }

    // ------------------------------------------------------------ 状态存档

    [Fact]
    public void 更新状态存取往返一致且不留临时文件()
    {
        string path = TestPaths.NewFile("updatestate.json");
        var store = new UpdateStateStore(path);

        UpdateStateLoadResult created = store.Load();
        Assert.Equal(UpdateStateLoadStatus.CreatedNew, created.Status);
        Assert.True(File.Exists(path));
        Assert.False(created.HasWarning);

        var lastCheck = DateTimeOffset.Parse("2026-04-05T06:07:08Z", CultureInfo.InvariantCulture);
        created.State.LastCheckUtc = lastCheck;
        created.State.LatestKnownVersion = "1.1.0";
        created.State.SkippedVersion = "1.2.0";
        store.Save(created.State);

        UpdateStateLoadResult reloaded = new UpdateStateStore(path).Load();
        Assert.Equal(UpdateStateLoadStatus.Loaded, reloaded.Status);
        Assert.Equal(lastCheck, reloaded.State.LastCheckUtc);
        Assert.Equal("1.1.0", reloaded.State.LatestKnownVersion);
        Assert.Equal("1.2.0", reloaded.State.SkippedVersion);
        Assert.Equal(UpdateState.CurrentSchemaVersion, reloaded.State.SchemaVersion);

        string directory = Path.GetDirectoryName(path)!;
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

        // 落盘键名必须是驼峰，方便手改排查
        string json = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"lastCheckUtc\"", json);
        Assert.Contains("\"latestKnownVersion\"", json);
        Assert.Contains("\"skippedVersion\"", json);
    }

    [Fact]
    public void 更新状态文件不存在时新建默认值()
    {
        string path = TestPaths.NewFile("updatestate.json");
        Assert.False(File.Exists(path));

        UpdateStateLoadResult result = new UpdateStateStore(path).Load();

        Assert.Equal(UpdateStateLoadStatus.CreatedNew, result.Status);
        Assert.True(File.Exists(path));
        Assert.Null(result.State.LastCheckUtc);
        Assert.Null(result.State.LatestKnownVersion);
        Assert.Null(result.State.SkippedVersion);
    }

    [Fact]
    public void 更新状态损坏时备份重建()
    {
        string path = TestPaths.NewFile("updatestate.json");
        File.WriteAllText(path, "{ 这不是合法 JSON ");

        UpdateStateLoadResult result = new UpdateStateStore(path).Load();

        Assert.Equal(UpdateStateLoadStatus.RecoveredFromCorrupt, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!), "损坏的原文件必须留下备份。");
        Assert.Contains("这不是合法 JSON", File.ReadAllText(result.BackupPath!));
        Assert.StartsWith("updatestate.corrupt.", Path.GetFileName(result.BackupPath!), StringComparison.Ordinal);

        // 重建出来的状态是默认值，且能正常再读写
        Assert.Null(result.State.LastCheckUtc);
        Assert.Equal(UpdateStateLoadStatus.Loaded, new UpdateStateStore(path).Load().Status);
    }

    [Fact]
    public void 更新状态版本过新时拒读且不覆盖原文件()
    {
        string path = TestPaths.NewFile("updatestate.json");
        const string original = """{ "schemaVersion": 99, "skippedVersion": "9.9" }""";
        File.WriteAllText(path, original);

        UpdateStateLoadResult result = new UpdateStateStore(path).Load();

        Assert.Equal(UpdateStateLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
        Assert.StartsWith("updatestate.unsupported.", Path.GetFileName(result.BackupPath!), StringComparison.Ordinal);

        // 关键：原文件一个字节都不能被动
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void 更新状态低版本被迁移且数据保留()
    {
        string path = TestPaths.NewFile("updatestate.json");
        File.WriteAllText(path, """{ "schemaVersion": 0, "latestKnownVersion": "1.1.0" }""");

        UpdateStateLoadResult result = new UpdateStateStore(path).Load();

        Assert.Equal(UpdateStateLoadStatus.Loaded, result.Status);
        Assert.Equal(UpdateState.CurrentSchemaVersion, result.State.SchemaVersion);
        Assert.Equal("1.1.0", result.State.LatestKnownVersion);
    }
}
