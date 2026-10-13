namespace MineSweeper.Core.Updates;

/// <summary>
/// 自动检查更新的频率（v1.2）。存的是"间隔小时数"，<see cref="EveryLaunch"/> = 0 表示每次启动都查。
///
/// 为什么用小时数而不是枚举：settings.json 是给人手改的，
/// 手写个 <c>48</c> 也该能看懂并生效（<see cref="Normalize"/> 会把它收敛到最近的合法档位）。
/// </summary>
public static class UpdateFrequency
{
    /// <summary>每次启动都查。</summary>
    public const int EveryLaunch = 0;

    /// <summary>每天（默认，与 v1.1 的行为一致）。</summary>
    public const int Daily = 24;

    /// <summary>每 3 天。</summary>
    public const int Every3Days = 72;

    /// <summary>每周。</summary>
    public const int Weekly = 168;

    /// <summary>每 2 周。</summary>
    public const int Biweekly = 336;

    /// <summary>每月（按 30 天算）。</summary>
    public const int Monthly = 720;

    /// <summary>界面下拉框用的档位，顺序就是显示顺序。</summary>
    public static IReadOnlyList<(int Hours, string Label)> Options { get; } =
    [
        (EveryLaunch, "每次启动"),
        (Daily, "每天"),
        (Every3Days, "每 3 天"),
        (Weekly, "每周"),
        (Biweekly, "每 2 周"),
        (Monthly, "每月"),
    ];

    /// <summary>把任意小时数收敛到最接近的合法档位；非法值（负数/超大）退回每天。</summary>
    public static int Normalize(int hours)
    {
        if (hours == EveryLaunch)
        {
            return EveryLaunch;
        }

        int best = Daily;
        int bestDelta = int.MaxValue;

        foreach ((int candidate, _) in Options)
        {
            if (candidate == EveryLaunch)
            {
                continue;
            }

            int delta = Math.Abs(candidate - hours);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>档位对应的中文名（用于日志与设置面板的状态行）。</summary>
    public static string LabelOf(int hours)
    {
        int normalized = Normalize(hours);

        foreach ((int candidate, string label) in Options)
        {
            if (candidate == normalized)
            {
                return label;
            }
        }

        return "每天";
    }
}

/// <summary>自动检查的时间判断（纯函数，可单测）。</summary>
public static class UpdateSchedule
{
    /// <summary>
    /// 该不该做一次"自动"检查。
    /// "每次启动"档永远为真；从没查过也为真；否则要距上次检查满一个间隔。
    /// <b>手动检查不走这里</b>——玩家点了按钮就必须真去查。
    /// </summary>
    public static bool ShouldAutoCheck(DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc, int intervalHours)
    {
        int interval = UpdateFrequency.Normalize(intervalHours);

        if (interval <= UpdateFrequency.EveryLaunch)
        {
            return true;
        }

        if (lastCheckUtc is not { } last)
        {
            return true;
        }

        return nowUtc - last >= TimeSpan.FromHours(interval);
    }
}

/// <summary>暂停更新（v1.2）：玩家可以选一个日期，在那之前不自动检查。</summary>
public static class UpdatePause
{
    /// <summary>暂停的默认时长：7 天。</summary>
    public static TimeSpan DefaultDuration => TimeSpan.FromDays(7);

    /// <summary>暂停到期时间在"未来"才算暂停中（已过期 = 自动恢复）。</summary>
    public static bool IsPaused(DateTimeOffset? pausedUntilUtc, DateTimeOffset nowUtc)
        => pausedUntilUtc is { } until && until > nowUtc;

    /// <summary>还剩多久（未暂停返回 null），用于状态行文案。</summary>
    public static TimeSpan? Remaining(DateTimeOffset? pausedUntilUtc, DateTimeOffset nowUtc)
        => pausedUntilUtc is { } until && until > nowUtc ? until - nowUtc : null;
}
