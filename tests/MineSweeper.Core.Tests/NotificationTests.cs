using MineSweeper.Core.Notifications;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 通知模型：级别、以及"只入日志 / 弹窗"的区分。
///
/// 后者是"精简通知"的核心机制——过程性提示（换主题、新局开始、瞄准态……）
/// 仍然会生成通知对象并写进 app.log，但不该弹窗打扰玩家。
/// </summary>
public sealed class NotificationTests
{
    [Fact]
    public void 普通工厂默认弹窗()
    {
        Assert.True(GameNotification.Info("t", "m", "tag").ShowToast);
        Assert.True(GameNotification.Success("t", "m", "tag").ShowToast);
        Assert.True(GameNotification.Warning("t", "m", "tag").ShowToast);
        Assert.True(GameNotification.Error("t", "m", "tag").ShowToast);
    }

    [Fact]
    public void 安静工厂不弹窗但保留全部内容()
    {
        GameNotification quiet = GameNotification.QuietInfo("已切换主题", "深色", "settings.theme");

        Assert.False(quiet.ShowToast);
        Assert.Equal(NotificationLevel.Info, quiet.Level);
        Assert.Equal("已切换主题", quiet.Title);
        Assert.Equal("深色", quiet.Message);
        Assert.Equal("settings.theme", quiet.Tag);

        Assert.False(GameNotification.QuietSuccess("已复制种子串", "9x9:10:1:2:3", "share.copied").ShowToast);
        Assert.False(GameNotification.Quiet(NotificationLevel.Error, "t", "m", "tag").ShowToast);
    }

    [Fact]
    public void 工厂函数保留传入的级别()
    {
        Assert.Equal(NotificationLevel.Info, GameNotification.Info("t", "m", "tag").Level);
        Assert.Equal(NotificationLevel.Success, GameNotification.Success("t", "m", "tag").Level);
        Assert.Equal(NotificationLevel.Warning, GameNotification.Warning("t", "m", "tag").Level);
        Assert.Equal(NotificationLevel.Error, GameNotification.Error("t", "m", "tag").Level);
        Assert.Equal(NotificationLevel.Warning, GameNotification.Quiet(NotificationLevel.Warning, "t", "m", "tag").Level);
    }
}
