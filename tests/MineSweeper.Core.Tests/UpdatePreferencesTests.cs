using MineSweeper.Core.Updates;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// v1.2 的三件事：自动检查频率、暂停更新、本版新增（全是纯函数）。
/// </summary>
public sealed class UpdatePreferencesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------ 频率

    [Fact]
    public void 每次启动档永远该检查()
    {
        Assert.True(UpdateSchedule.ShouldAutoCheck(Now.AddMinutes(-1), Now, UpdateFrequency.EveryLaunch));
        Assert.True(UpdateSchedule.ShouldAutoCheck(Now, Now, UpdateFrequency.EveryLaunch));
    }

    [Fact]
    public void 从没检查过时任何档位都该检查()
    {
        foreach ((int hours, _) in UpdateFrequency.Options)
        {
            Assert.True(UpdateSchedule.ShouldAutoCheck(null, Now, hours), $"档位 {hours} 小时");
        }
    }

    [Theory]
    [InlineData(UpdateFrequency.Daily, 23, false)]
    [InlineData(UpdateFrequency.Daily, 25, true)]
    [InlineData(UpdateFrequency.Every3Days, 71, false)]
    [InlineData(UpdateFrequency.Every3Days, 73, true)]
    [InlineData(UpdateFrequency.Weekly, 167, false)]
    [InlineData(UpdateFrequency.Weekly, 169, true)]
    [InlineData(UpdateFrequency.Biweekly, 200, false)]
    [InlineData(UpdateFrequency.Biweekly, 400, true)]
    [InlineData(UpdateFrequency.Monthly, 719, false)]
    [InlineData(UpdateFrequency.Monthly, 721, true)]
    public void 各档频率按间隔判断(int hours, int elapsedHours, bool expected)
        => Assert.Equal(expected, UpdateSchedule.ShouldAutoCheck(Now.AddHours(-elapsedHours), Now, hours));

    [Fact]
    public void 非法频率会被收敛到最近档位()
    {
        Assert.Equal(UpdateFrequency.Daily, UpdateFrequency.Normalize(-5));
        Assert.Equal(UpdateFrequency.Daily, UpdateFrequency.Normalize(1));
        Assert.Equal(UpdateFrequency.Every3Days, UpdateFrequency.Normalize(70));
        Assert.Equal(UpdateFrequency.Weekly, UpdateFrequency.Normalize(160));
        Assert.Equal(UpdateFrequency.EveryLaunch, UpdateFrequency.Normalize(0));
        Assert.Equal(UpdateFrequency.Monthly, UpdateFrequency.Normalize(999999));
    }

    [Fact]
    public void 档位标签与档位一一对应()
    {
        Assert.Equal("每天", UpdateFrequency.LabelOf(UpdateFrequency.Daily));
        Assert.Equal("每周", UpdateFrequency.LabelOf(UpdateFrequency.Weekly));
        Assert.Equal("每次启动", UpdateFrequency.LabelOf(0));
        Assert.Equal(6, UpdateFrequency.Options.Count);
    }

    // ------------------------------------------------------------ 暂停

    [Fact]
    public void 暂停到期前后行为相反()
    {
        Assert.True(UpdatePause.IsPaused(Now.AddDays(1), Now));
        Assert.False(UpdatePause.IsPaused(Now.AddSeconds(-1), Now));
        Assert.False(UpdatePause.IsPaused(null, Now));
        Assert.False(UpdatePause.IsPaused(Now, Now));
    }

    [Fact]
    public void 剩余时间只为未来暂停计算()
    {
        Assert.Equal(TimeSpan.FromDays(7), UpdatePause.Remaining(Now.AddDays(7), Now));
        Assert.Null(UpdatePause.Remaining(Now.AddDays(-1), Now));
        Assert.Null(UpdatePause.Remaining(null, Now));
        Assert.Equal(TimeSpan.FromDays(7), UpdatePause.DefaultDuration);
    }

    // ------------------------------------------------------- 本版新增

    [Fact]
    public void 第一次安装不弹本版新增()
    {
        Assert.False(WhatsNewCatalog.ShouldShow(null, "1.2"));
        Assert.False(WhatsNewCatalog.ShouldShow("", "1.2"));
        Assert.False(WhatsNewCatalog.ShouldShow("   ", "1.2"));
    }

    [Fact]
    public void 版本变高才弹()
    {
        Assert.True(WhatsNewCatalog.ShouldShow("1.1", "1.2"));
        Assert.True(WhatsNewCatalog.ShouldShow("v1.0", "1.2.0"));
        Assert.False(WhatsNewCatalog.ShouldShow("1.2", "1.2"));
        Assert.False(WhatsNewCatalog.ShouldShow("1.2", "1.1"));
        Assert.False(WhatsNewCatalog.ShouldShow("乱码", "1.2"));
        Assert.False(WhatsNewCatalog.ShouldShow("1.1", "乱码"));
    }

    [Fact]
    public void 当前版本必须有对应的本版新增文案()
    {
        // 这条是给"改版本号却忘了写文案"兜底的：AboutInfo.Version 是 1.2，表里就必须有 1.2
        Assert.NotNull(WhatsNewCatalog.Find("1.2"));
    }

    [Fact]
    public void 条目查得到且要点不为空()
    {
        WhatsNewEntry? entry = WhatsNewCatalog.Find("v1.1");

        Assert.NotNull(entry);
        Assert.Equal("1.1", entry!.Version);
        Assert.NotEmpty(entry.Highlights);
        Assert.All(WhatsNewCatalog.All, e => Assert.NotEmpty(e.Highlights));
        Assert.Null(WhatsNewCatalog.Find("9.9"));
        Assert.Null(WhatsNewCatalog.Find(null));
    }
}
