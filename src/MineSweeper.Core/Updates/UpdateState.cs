using System.Text.Json.Serialization;

namespace MineSweeper.Core.Updates;

/// <summary>这次检查是谁发起的。</summary>
public enum UpdateCheckTrigger
{
    /// <summary>随程序启动自动检查（受最小间隔限制，不会每开一次就查一次）。</summary>
    Startup = 0,

    /// <summary>玩家在"关于"里点了"检查更新"：玩家的意愿优先，永远真的去查。</summary>
    Manual = 1,
}

/// <summary>
/// 「检查更新」的持久状态（updatestate.json）。
///
/// 三个字段各挡一类骚扰：<see cref="LastCheckUtc"/> 挡住"每次启动都联网"，
/// <see cref="LatestKnownVersion"/> 挡住"同一个版本反复弹窗"，
/// <see cref="SkippedVersion"/> 尊重玩家明确说过的"这个版本我不装"。
/// 结构改动时 +1 版本号并补迁移（与其它存档同规矩）。
/// </summary>
public sealed class UpdateState
{
    /// <summary>当前 schema 版本。改结构时 +1 并在 <see cref="UpdateStateStore"/> 里补迁移。</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>上次真正发起检查的时间（UTC）；从未检查过为 null。</summary>
    [JsonPropertyName("lastCheckUtc")]
    public DateTimeOffset? LastCheckUtc { get; set; }

    /// <summary>已经提示过的那个新版本（规范化后的版本号）。</summary>
    [JsonPropertyName("latestKnownVersion")]
    public string? LatestKnownVersion { get; set; }

    /// <summary>玩家选择"跳过这个版本"的版本号（规范化后的版本号）。</summary>
    [JsonPropertyName("skippedVersion")]
    public string? SkippedVersion { get; set; }

    /// <summary>默认状态：从未检查、没有已知新版本、没有跳过任何版本。</summary>
    public static UpdateState CreateDefault() => new();
}

/// <summary>
/// 「要不要查」和「要不要打扰玩家」两条纯策略（无 IO、可单测）。
///
/// 这一层存在的意义就是<b>把"检查更新"从"每次启动弹窗"变成"偶尔、且只在真有事时出现"</b>：
/// 频繁弹提示的更新提醒最后只会被玩家一律点掉，等于没有。
/// </summary>
public static class UpdateCheckPolicy
{
    /// <summary>
    /// 默认的自动检查最小间隔：24 小时。
    /// <b>实际间隔以玩家在设置里选的档位为准</b>（调用 <see cref="ShouldCheck"/> 时传入）；
    /// 这里保留 24 小时只是"没传间隔时的兜底值"，避免旧调用方拿到无限制行为。
    /// </summary>
    public static TimeSpan AutoInterval { get; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 现在要不要发起一次检查。
    /// <see cref="UpdateCheckTrigger.Manual"/> 永远为 true（玩家点了按钮就必须有反馈，哪怕只是"已是最新"）；
    /// <see cref="UpdateCheckTrigger.Startup"/> 在距上次检查不足 <paramref name="minInterval"/> 时为 false，
    /// 从未检查过（<see cref="UpdateState.LastCheckUtc"/> 为 null）时为 true。
    ///
    /// <paramref name="minInterval"/> 由调用方按玩家设置传入：0（每次启动档）表示不做时间限制，
    /// 其余档位传对应小时数。v1.2 引入"检查频率"后曾出现两层限流打架——
    /// 启动逻辑按玩家档位放行、这里又固定按 24 小时拦一次，
    /// 结果"每次启动"档在 24 小时内根本不生效。现在间隔只有一个来源。
    /// </summary>
    public static bool ShouldCheck(UpdateState state, UpdateCheckTrigger trigger, DateTimeOffset nowUtc, TimeSpan minInterval, out string reason)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (trigger == UpdateCheckTrigger.Manual)
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.manualNoLimit");
            return true;
        }

        if (state.LastCheckUtc is not { } lastCheck)
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.noCheckYet");
            return true;
        }

        TimeSpan elapsed = nowUtc - lastCheck;

        // elapsed 为负说明系统时间被改小了：当作"刚查过"，不去打扰玩家
        if (elapsed < minInterval)
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.tooSoon", elapsed.TotalHours.ToString("F1"), minInterval.TotalHours.ToString("F0"));
            return false;
        }

        reason = MineSweeper.Core.Localization.Strings.Get("update.intervalElapsed", elapsed.TotalHours.ToString("F1"), minInterval.TotalHours.ToString("F0"));
        return true;
    }

    /// <summary>
    /// 这个发行版值不值得弹提示。四个"不值得"各有独立理由，全部写进 <paramref name="reason"/> 便于日志排查：
    /// 版本不新、玩家跳过过、已经提示过、版本号根本认不出来。
    /// 顺序上先做"版本号能否解析"的检查 —— 认不出来的版本号谈不上比较新旧。
    /// </summary>
    public static bool ShouldNotify(UpdateState state, string? currentVersion, ReleaseInfo release, out string reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(release);

        if (!AppVersion.TryParse(release.Version, out _))
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.versionUnreadable", release.Version);
            return false;
        }

        if (!AppVersion.IsNewer(release.Version, currentVersion))
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.upToDate", currentVersion ?? MineSweeper.Core.Localization.Strings.Get("common.unknown"), release.Version);
            return false;
        }

        if (SameVersion(state.SkippedVersion, release.Version))
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.skipped", release.Version);
            return false;
        }

        if (SameVersion(state.LatestKnownVersion, release.Version))
        {
            reason = MineSweeper.Core.Localization.Strings.Get("update.alreadyPrompted", release.Version);
            return false;
        }

        reason = MineSweeper.Core.Localization.Strings.Get("update.newVersion", release.Version, currentVersion ?? MineSweeper.Core.Localization.Strings.Get("common.unknown"));
        return true;
    }

    /// <summary>
    /// 两个版本串是否指同一个版本。
    /// 存档里的值可能是手改的（"v1.1.0" / " 1.1.0 "），所以能解析就按补齐后的版本号比，
    /// 解析不了才退回字符串比较 —— 否则"跳过 v1.1.0"挡不住"v1.1.0"的提示。
    /// </summary>
    private static bool SameVersion(string? left, string? right)
    {
        if (AppVersion.TryParse(left, out Version parsedLeft) && AppVersion.TryParse(right, out Version parsedRight))
        {
            return parsedLeft == parsedRight;
        }

        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
