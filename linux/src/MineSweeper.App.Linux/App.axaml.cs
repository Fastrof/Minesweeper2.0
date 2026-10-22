using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MineSweeper.App.Linux.Views;

namespace MineSweeper.App.Linux;

public partial class App : Application
{
    /// <summary>组合根（进程级）。</summary>
    public static AppHost? Host { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Host = AppHost.Create();
            desktop.MainWindow = new MainWindow(Host);
            desktop.ShutdownRequested += (_, _) => Host?.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
