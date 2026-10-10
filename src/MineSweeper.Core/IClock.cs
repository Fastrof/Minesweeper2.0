namespace MineSweeper.Core;

/// <summary>
/// 时钟抽象：Core 层需要计时（§0 第 6 条），但不得依赖任何 UI/平台 API。
/// 生产用 <see cref="SystemClock"/>，单测注入假时钟即可精确控制时间。
/// </summary>
public interface IClock
{
    /// <summary>当前 UTC 时间。</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>默认时钟，直接取系统时间。</summary>
public sealed class SystemClock : IClock
{
    /// <summary>共享实例。</summary>
    public static readonly SystemClock Instance = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>可控时钟，供测试使用（也方便做"时光倒流"型的边界测试）。</summary>
public sealed class ManualClock : IClock
{
    private DateTimeOffset _now;

    public ManualClock(DateTimeOffset? start = null)
    {
        _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _now;

    /// <summary>把时钟向前推进指定时长。</summary>
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    /// <summary>把时钟向前推进指定秒数。</summary>
    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
}
