using Microsoft.UI.Xaml;
using MineSweeper.App.Services;
using MineSweeper.Core.Notifications;

namespace MineSweeper.App;

/// <summary>应用入口（§1.3 Views/）。</summary>
public partial class App : Application
{
    private readonly ToastNotifier _notifier = new();

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        // 未处理异常既写 log，也发一条 Error 级通知——玩家看到的"闪退"至少有个说法
        UnhandledException += (_, e) =>
        {
            AppPaths.Log($"未处理异常：{e.Exception}");

            try
            {
                _notifier.Publish(GameNotification.Error(
                    "扫雷 2.0 出错了",
                    e.Exception?.Message ?? "发生未处理异常，详情见 %LOCALAPPDATA%\\MineSweeper2\\app.log。",
                    "app.unhandled"));
            }
            catch (Exception)
            {
                // 通知本身不允许再抛出去，否则就真的崩了
            }
        };
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_notifier);
        _window.Closed += (_, _) => _notifier.Dispose();
        _window.Activate();
    }
}
