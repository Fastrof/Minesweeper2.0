using System.Diagnostics;
using MineSweeper.Core.Notifications;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 通知服务（对应 Windows 端的 ToastNotifier）。
///
/// Linux 用 freedesktop 的 <c>notify-send</c>（几乎所有桌面环境都自带或可装 libnotify-bin）：
///   - 通知级别 → urgency（low / normal / critical）；
///   - 没有 notify-send 时退化：只写 app.log，并在窗口内用状态条提示（由 UI 订阅 <see cref="Raised"/>）。
///
/// 与 Windows 端一致：**通知失败绝不影响游戏**。
/// </summary>
public sealed class NotificationService
{
    private static readonly string[] Candidates = ["notify-send"];

    private readonly string? _notifySend;
    private readonly string? _iconPath;
    private readonly string _appName = "MineSweeper 2.0";

    /// <summary>
    /// 系统通知失败一次后就不再尝试（WSLg / 精简桌面常没有 org.freedesktop.Notifications
    /// 守护进程，每次 notify-send 都要等 DBus 超时才失败，白白占一个后台线程）。
    /// </summary>
    private volatile bool _systemNotifyDisabled;

    public NotificationService(string? iconPath = null)
    {
        _iconPath = iconPath;
        _notifySend = FindOnPath(Candidates);

        // WSLg 的 Wayland 合成器不支持 wlr-layer-shell：一旦调用 notify-send，
        // D-Bus 会去激活 xfce4-notifyd，它启动失败时会自己弹一个"提示守护进程错误"窗口。
        // 所以 WSL 下默认**完全不用**系统通知，只用窗口内提示条；
        // 需要强制开启时设环境变量 MINESWEEPER2_SYSTEM_NOTIFY=1。
        if (_notifySend is not null && IsWsl() && !ForceSystemNotifyEnabled)
        {
            _notifySend = null;
            AppPaths.Log("[通知] 运行在 WSL：跳过系统通知（否则会弹 xfce4-notifyd 错误窗），只用窗口内提示");
        }
    }

    /// <summary>强制启用系统通知（环境变量 MINESWEEPER2_SYSTEM_NOTIFY=1）。</summary>
    private static bool ForceSystemNotifyEnabled =>
        Environment.GetEnvironmentVariable("MINESWEEPER2_SYSTEM_NOTIFY") == "1";

    /// <summary>是否运行在 WSL（/proc/version 里带 microsoft）。</summary>
    private static bool IsWsl()
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")))
            {
                return true;
            }

            return File.Exists("/proc/version")
                   && File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>需要弹窗的通知（只对 <see cref="GameNotification.ShowToast"/> 为 true 的触发）。</summary>
    public event EventHandler<GameNotification>? Raised;

    /// <summary>是否真的能发系统通知（诊断用）。</summary>
    public bool HasSystemNotifier => _notifySend is not null;

    /// <summary>发一条通知：日志必写、系统通知尽力、窗口内提示由订阅方决定。</summary>
    public void Show(GameNotification notification)
    {
        if (notification is null)
        {
            return;
        }

        AppPaths.Log($"[通知/{notification.Level}] {notification.Title}：{notification.Message}");

        if (!notification.ShowToast)
        {
            return;
        }

        RaiseInWindow(notification);
        TrySystemNotify(notification);
    }

    private void RaiseInWindow(GameNotification notification) => Raised?.Invoke(this, notification);

    private void TrySystemNotify(GameNotification notification)
    {
        if (_notifySend is null || _systemNotifyDisabled)
        {
            return;
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = _notifySend,
                UseShellExecute = false,
                RedirectStandardError = false,
                RedirectStandardOutput = false,
                CreateNoWindow = true,
            };

            info.ArgumentList.Add("--app-name");
            info.ArgumentList.Add(_appName);
            info.ArgumentList.Add("--urgency");
            info.ArgumentList.Add(Urgency(notification.Level));
            info.ArgumentList.Add("--expire-time");
            info.ArgumentList.Add(ExpireMs(notification.Level).ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
            {
                info.ArgumentList.Add("--icon");
                info.ArgumentList.Add(_iconPath);
            }

            info.ArgumentList.Add(notification.Title);
            info.ArgumentList.Add(notification.Message);

            // 不能 using 立刻释放：notify-send 是异步进程，释放 handle 可能让它还没发就被清理。
            // 交给后台线程等它退出再释放，UI 线程不等。
            Process? process = Process.Start(info);
            if (process is not null)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (!process.WaitForExit(3000))
                        {
                            process.Kill(entireProcessTree: true);
                            _systemNotifyDisabled = true;
                            AppPaths.Log("[通知] notify-send 超时（多半是没有通知守护进程），后续只用窗口内提示");
                        }
                        else if (process.ExitCode != 0)
                        {
                            _systemNotifyDisabled = true;
                            AppPaths.Log("[通知] notify-send 返回非零（没有通知守护进程），后续只用窗口内提示");
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        _systemNotifyDisabled = true;
                        AppPaths.Log($"[通知] 等待 notify-send 退出失败：{ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppPaths.Log($"[通知] 系统通知失败：{ex.Message}");
        }
    }

    private static string Urgency(NotificationLevel level) => level switch
    {
        NotificationLevel.Info => "low",
        NotificationLevel.Success => "normal",
        NotificationLevel.Warning => "normal",
        NotificationLevel.Error => "critical",
        _ => "normal",
    };

    private static int ExpireMs(NotificationLevel level) => level switch
    {
        NotificationLevel.Info => 2500,
        NotificationLevel.Success => 3000,
        NotificationLevel.Warning => 4000,
        NotificationLevel.Error => 6000,
        _ => 3000,
    };

    private static string? FindOnPath(string[] candidates)
    {
        string[] pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries);

        foreach (string candidate in candidates)
        {
            foreach (string dir in pathDirs)
            {
                string full = Path.Combine(dir, candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }
}
