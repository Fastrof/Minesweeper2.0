namespace MineSweeper.Core.Notifications;

/// <summary>
/// 通知级别。UI 按级别决定用哪种表现（例如 Windows Toast 的 scenario / 提示音 / 优先级），
/// 所以级别必须由产生通知的地方决定，而不是由 UI 事后猜文案。
/// </summary>
public enum NotificationLevel
{
    /// <summary>普通信息：新局开始、复制成功之类的过程性提示。不打断玩家。</summary>
    Info = 0,

    /// <summary>正向结果：胜利、获得道具、刷新纪录。</summary>
    Success = 1,

    /// <summary>操作被拒绝或需要注意：参数不合法、区域已透视、旗数与数字不符。</summary>
    Warning = 2,

    /// <summary>失败与错误：踩雷、开局失败、存档损坏、未处理异常。</summary>
    Error = 3,
}

/// <summary>
/// 一条通知（§5.3 的提示条内容升级为带级别的通知）。
///
/// <paramref name="Tag"/> 是同类通知的稳定标识，供 UI 做去重与限流——
/// 像"周围旗数与数字不符"这种每次点错都会产生的提示，如果不去重会把通知中心刷满。
///
/// <paramref name="ShowToast"/> 决定这条通知是否<b>弹窗</b>打扰玩家。
/// 为 false 时只写进 <c>data\app.log</c> 备查（见 <see cref="Quiet"/> 系列工厂）。
/// </summary>
public sealed record GameNotification(
    NotificationLevel Level,
    string Title,
    string Message,
    string Tag,
    bool ShowToast = true)
{
    /// <summary>普通信息。</summary>
    public static GameNotification Info(string title, string message, string tag) =>
        new(NotificationLevel.Info, title, message, tag);

    /// <summary>正向结果。</summary>
    public static GameNotification Success(string title, string message, string tag) =>
        new(NotificationLevel.Success, title, message, tag);

    /// <summary>需要注意 / 被拒绝。</summary>
    public static GameNotification Warning(string title, string message, string tag) =>
        new(NotificationLevel.Warning, title, message, tag);

    /// <summary>失败或错误。</summary>
    public static GameNotification Error(string title, string message, string tag) =>
        new(NotificationLevel.Error, title, message, tag);

    // ---------------------------------------------------------------- 只入日志

    /// <summary>
    /// <b>只写日志、不弹窗</b>的通知。
    ///
    /// 用于"玩家已经能直接从界面上看到结果"的过程性信息：设置变更、新局开始、
    /// 瞄准态提示、视图复位、复制成功…… 这些每做一次动作就弹一次通知，
    /// 会把通知中心刷满，而它们并没有带来任何界面上看不到的信息。
    /// 保留下来是为了排查问题（app.log 里仍有痕迹），但不再打断玩家。
    /// </summary>
    public static GameNotification Quiet(NotificationLevel level, string title, string message, string tag) =>
        new(level, title, message, tag, ShowToast: false);

    /// <summary>只入日志的普通信息。</summary>
    public static GameNotification QuietInfo(string title, string message, string tag) =>
        Quiet(NotificationLevel.Info, title, message, tag);

    /// <summary>只入日志的正向结果。</summary>
    public static GameNotification QuietSuccess(string title, string message, string tag) =>
        Quiet(NotificationLevel.Success, title, message, tag);
}

/// <summary>
/// 通知出口。Core 与 ViewModel 只依赖这个接口，不引用任何 Windows/WinRT 类型，
/// 于是"通知怎么显示"完全留在 App 层（当前实现是 Windows Toast 通知）。
/// </summary>
public interface INotificationSink
{
    /// <summary>发出一条通知。</summary>
    void Publish(GameNotification notification);
}
