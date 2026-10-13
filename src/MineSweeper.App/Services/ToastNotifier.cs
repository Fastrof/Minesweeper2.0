using System.Security;
using Microsoft.Windows.AppNotifications;
using MineSweeper.Core.Localization;
using MineSweeper.Core.Notifications;

namespace MineSweeper.App.Services;

/// <summary>
/// 通知出口的 Windows Toast 实现（替代原来的游戏内提示条）。
///
/// 级别 → Toast 表现的映射（这是本功能唯一需要"设计"的地方）：
/// <list type="table">
/// <item><term>Info</term><description>短时、<b>静音</b>——新局开始之类的过程提示不该打断玩家</description></item>
/// <item><term>Success</term><description>短时、系统默认提示音——胜利、获得道具、刷新纪录</description></item>
/// <item><term>Warning</term><description><b>长时</b>（留在通知中心更久）、Reminder 音——操作被拒绝、参数不合法</description></item>
/// <item><term>Error</term><description><b>长时</b>、警报音——踩雷、开局失败、存档损坏、未处理异常</description></item>
/// </list>
///
/// 刻意不使用 <c>scenario="urgent"</c>：它会先弹一次系统"重要通知"授权框，
/// 玩家选"以后再说"后最关键的失败通知反而可能收不到，详见 <see cref="BuildToastXml"/> 的注释。
///
/// 两个必须的工程细节：
/// 1. <b>限流</b>：像"周围旗数与数字不符"这种每次点错都会产生的提示，如果不去重会把通知中心刷满。
///    这里按 (Tag, 文案) 去重，窗口内被压掉的条数会在下一次放行时合并成一句告诉玩家。
/// 2. <b>绝不因为通知失败而影响游戏</b>：Toast 不可用时只写 app.log，游戏照常运行。
///    注意 self-contained + unpackaged 下 <c>Register()</c> 会抛已知的 COMException，
///    但注册往往已完成、<c>Show()</c> 仍然有效，所以不能因为它就放弃发送（见构造函数）。
/// </summary>
public sealed class ToastNotifier : INotificationSink, IDisposable
{
    /// <summary>同一 (Tag, 文案) 在该窗口内只弹一次。</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, RateEntry> _recent = new(StringComparer.Ordinal);
    private readonly bool _registered;

    private bool _loggedFirstSuccess;
    private bool _disposed;

    public ToastNotifier()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
            AppPaths.Log("Toast 通知已注册。");
        }
        catch (Exception ex)
        {
            // WinAppSDK 在 "self-contained + unpackaged" 下的已知问题：
            // Register() 会因为加载不到 Microsoft.WindowsAppRuntime.Insights.Resource.dll 而抛 COMException。
            // 但注册往往已经完成，Show() 实际上仍然能发出通知（见 microsoft/WindowsAppSDK#6071 的实测反馈）。
            // 所以这里只记日志，绝不因此放弃发送——否则一个平台 bug 会把整个通知功能打死。
            _registered = false;
            AppPaths.Log(
                $"Toast 注册抛出 {ex.GetType().Name}：{ex.Message}" +
                "（WinAppSDK self-contained + unpackaged 的已知问题；仍会继续尝试发送通知）");
        }
    }

    /// <summary>
    /// <c>Register()</c> 是否无异常返回。
    /// 注意：为 false <b>不代表</b>发不出通知——见构造函数里的说明；真正的判据是 Show() 有没有抛异常。
    /// </summary>
    public bool IsRegistered => _registered;

    /// <summary>Show() 是否至少成功过一次（诊断用）。</summary>
    public bool HasShownSuccessfully => _loggedFirstSuccess;

    /// <summary>被限流压掉的通知总数（排查"为什么没弹"时用）。</summary>
    public int SuppressedTotal { get; private set; }

    /// <summary>只入日志、没弹窗的"安静通知"条数。</summary>
    public int QuietTotal { get; private set; }

    /// <summary>真正弹出去的 Toast 条数。</summary>
    public int ShownTotal { get; private set; }

    /// <inheritdoc />
    /// <summary>
    /// 把通知的标题与正文按当前语言翻译一次（v1.3）。
    ///
    /// 放在这个**唯一出口**上，而不是去改上百个调用点：Toast 的文案散落在 Core 与 App 各处，
    /// 逐个改既容易漏、又会让每次加提示都多一道手续。翻译按"简体中文原文"反查语言表，
    /// 查不到就原样发出（表现为那句还是中文，一眼能看出漏了哪条）。
    /// </summary>
    private static GameNotification Translate(GameNotification notification)
        => notification with
        {
            Title = Strings.TranslateLiteral(notification.Title),
            Message = Strings.TranslateLiteral(notification.Message),
        };

    public void Publish(GameNotification notification)
    {
        notification = Translate(notification);

        ArgumentNullException.ThrowIfNull(notification);

        // 安静通知：只写日志，不弹窗、也不占用限流额度。
        //
        // 这类通知的共同点是"玩家已经能直接从界面上看到结果"：
        // 换主题、开关设置、新局开始、进入瞄准态、视图复位、复制成功……
        // 每做一次动作就弹一次会把通知中心刷满，而且没有任何界面上看不到的信息。
        // 保留写入 app.log 是为了排查问题时有痕迹。
        if (!notification.ShowToast)
        {
            QuietTotal++;
            AppPaths.Log($"[记录/{notification.Level}] {notification.Title}：{notification.Message}");
            return;
        }

        if (!TryPrepare(notification, out string message))
        {
            // 被限流：已经记进 app.log，这里直接结束，绝不发一条空内容的 toast
            return;
        }

        // 日志里带上提示音，方便核对"某个级别到底响了哪个音"（改音效时很有用）
        AppPaths.Log($"[通知/{notification.Level}] {notification.Title}：{message} · 提示音={(AudioSourceOf(notification.Level) ?? "静音")}");
        ShownTotal++;

        try
        {
            AppNotificationManager.Default.Show(BuildNotification(notification.Level, notification.Title, message));

            if (!_loggedFirstSuccess)
            {
                _loggedFirstSuccess = true;
                AppPaths.Log("Toast 发送成功（首次）。");
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Toast 发送失败（{notification.Level}）：{ex.Message}");

            // Urgent 这类 scenario 对音频有硬性要求，个别系统上会被判为非法载荷；
            // 回退成最普通的 toast，保证玩家至少能看到这条通知。
            try
            {
                AppNotificationManager.Default.Show(BuildFallbackNotification(notification.Level, notification.Title, message));
                _loggedFirstSuccess = true;
            }
            catch (Exception fallbackError)
            {
                AppPaths.Log($"Toast 回退发送也失败：{fallbackError.Message}");
            }
        }
    }

    /// <summary>
    /// <b>所有通知共用的槽位</b>（Tag + Group）。
    ///
    /// 这是"通知跟得上操作"的关键：Tag/Group 相同的通知会被系统**替换**，
    /// 而不是各自排队。默认（不设 Tag/Group）每条通知占一个独立槽位，于是
    /// "chord 未执行"还挂在屏幕上的时候踩到雷，"踩雷失败"那条要等前一条
    /// 自己收起来（约 7 秒）才会出现、才会响铃——玩家感觉通知慢半拍。
    ///
    /// 通知只反映"刚刚发生了什么"，旧的那条本来就该让位，所以替换是正确行为。
    /// </summary>
    internal const string NotificationSlotTag = "game";

    internal const string NotificationSlotGroup = "minesweeper2";

    private static AppNotification BuildNotification(NotificationLevel level, string title, string message)
        => StampSlot(new AppNotification(BuildToastXml(level, title, message)));

    private static AppNotification BuildFallbackNotification(NotificationLevel level, string title, string message)
        => StampSlot(new AppNotification(BuildFallbackToastXml(level, title, message)));

    private static AppNotification StampSlot(AppNotification notification)
    {
        notification.Tag = NotificationSlotTag;
        notification.Group = NotificationSlotGroup;
        return notification;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            AppNotificationManager.Default.Unregister();
            AppPaths.Log("Toast 通知已注销。");
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Toast 注销失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 按 (Tag, 文案) 去重。返回 false 表示这条被限流压掉、不要展示；
    /// 返回 true 时 <paramref name="message"/> 是真正要展示的文案（可能带合并计数）。
    /// </summary>
    private bool TryPrepare(GameNotification notification, out string message)
    {
        string key = notification.Tag + "|" + notification.Message;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PruneRecent(now);

        if (_recent.TryGetValue(key, out RateEntry? entry))
        {
            if (now - entry.LastAt < DuplicateWindow)
            {
                entry.Suppressed++;
                SuppressedTotal++;
                AppPaths.Log($"[通知已限流 ×{entry.Suppressed}] {notification.Title}：{notification.Message}");
                message = string.Empty;
                return false;
            }

            int merged = entry.Suppressed;
            entry.Suppressed = 0;
            entry.LastAt = now;

            message = merged > 0
                ? $"{notification.Message}（期间另有 {merged} 条同类提示已合并）"
                : notification.Message;
            return true;
        }

        _recent[key] = new RateEntry { LastAt = now };
        message = notification.Message;
        return true;
    }

    /// <summary>限制字典规模：超过一分钟没再出现的键直接丢掉。</summary>
    private void PruneRecent(DateTimeOffset now)
    {
        if (_recent.Count < 64)
        {
            return;
        }

        var stale = new List<string>();
        foreach (KeyValuePair<string, RateEntry> pair in _recent)
        {
            if (now - pair.Value.LastAt > TimeSpan.FromMinutes(1))
            {
                stale.Add(pair.Key);
            }
        }

        foreach (string key in stale)
        {
            _recent.Remove(key);
        }
    }

    /// <summary>
    /// 某个级别用的系统提示音。返回 null 表示静音（Info）。
    ///
    /// 日志与 toast XML 都用这一个来源，避免"日志说响了警报、实际发的是别的音"。
    /// </summary>
    private static string? AudioSourceOf(NotificationLevel level) => level switch
    {
        NotificationLevel.Info => null,
        NotificationLevel.Success => "ms-winsoundevent:Notification.Default",
        NotificationLevel.Warning => "ms-winsoundevent:Notification.Reminder",
        _ => "ms-winsoundevent:Notification.Looping.Alarm",
    };

    private static string BuildAudioXml(NotificationLevel level)
        => AudioSourceOf(level) is { } source
            ? $"<audio src=\"{source}\" loop=\"false\" silent=\"false\"/>"
            : "<audio silent=\"true\"/>";

    private static string BuildToastXml(NotificationLevel level, string title, string message)
    {
        // 级别靠"时长 + 提示音"两个维度区分。
        //
        // 为什么<b>不用</b> scenario="urgent"（原本的想法，实测后放弃）：
        // urgent 会触发一次系统级的"重要通知"授权弹窗（"是否允许来自 MineSweeper2 的重要通知？"），
        // 玩家一旦选"以后再说"，最关键的"本局失败"通知反而可能收不到——
        // 对游戏来说这个代价换来的区分度不值。所以四种级别都用默认样式，
        // 靠时长（short/long）与提示音（静音/默认/提醒/警报）拉开差别。
        // 一律短时（约 7 秒后自动收起）。
        //
        // 原来 Warning / Error 用 duration="long"（约 25 秒），玩家反馈"通知不会自己收起来"。
        // 级别之间的区分现在只靠提示音——反正真正重要的是"有没有看到"，而不是它挂多久。
        const string duration = "short";

        // 提示音：
        //   Info 静音 / Success 默认 / Warning 提醒 / Error 警报。
        // Error 用的是 "Looping.Alarm"——名字里带 Looping，但 loop="false" 所以**不会真的循环**。
        // （中途曾换成 Notification.IM，玩家反馈"关卡失败的铃声变了"，于是按原版改回警报音；
        //   "不会自己收起来"那个问题已经由上面的 duration="short" 解决，两者互不影响。）
        string audio = BuildAudioXml(level);

        return $"""
            <toast duration="{duration}">
              <visual>
                <binding template="ToastGeneric">
                  <text>{Escape(title)}</text>
                  <text>{Escape(message)}</text>
                  <text placement="attribution">{Escape(LevelLabel(level))} · 扫雷 2.0</text>
                  {LogoImageXml()}
                </binding>
              </visual>
              {audio}
            </toast>
            """;
    }

    /// <summary>
    /// 通知左上角的程序图标。
    ///
    /// unpackaged 应用拿不到 ms-appx，所以只能用 file:// 指向磁盘上的真实 PNG
    /// （Assets\ToastIcon.png 随 exe 一起输出）。文件不在时返回空串——
    /// 宁可少一个图标，也不能让整条通知因为非法 src 而发不出去。
    /// </summary>
    private static string LogoImageXml()
    {
        try
        {
            string path = AppPaths.ToastIconFile;
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            // 用 Uri 生成合法的 file:// 形式（自动处理中文与空格转义）
            string uri = new Uri(path).AbsoluteUri;
            return $"<image placement=\"appLogoOverride\" hint-crop=\"none\" src=\"{Escape(uri)}\"/>";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string BuildFallbackToastXml(NotificationLevel level, string title, string message) => $"""
        <toast duration="short">
          <visual>
            <binding template="ToastGeneric">
              <text>{Escape(title)}</text>
              <text>{Escape(message)}</text>
              <text placement="attribution">扫雷 2.0</text>
              {LogoImageXml()}
            </binding>
          </visual>
          {BuildAudioXml(level)}
        </toast>
        """;

    private static string LevelLabel(NotificationLevel level) => level switch
    {
        NotificationLevel.Info => "提示",
        NotificationLevel.Success => "成功",
        NotificationLevel.Warning => "警告",
        _ => "错误",
    };

    private static string Escape(string? text) => SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;

    private sealed class RateEntry
    {
        public DateTimeOffset LastAt { get; set; }

        public int Suppressed { get; set; }
    }
}
