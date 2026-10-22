using Avalonia;
using MineSweeper.Core;

namespace MineSweeper.App.Linux;

/// <summary>Linux 版入口（Avalonia）。与 Windows 端 <c>MineSweeper.App</c>（WinUI 3）并列，互不引用。</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 无头截图模式：dotnet run -- --screenshot <dir>
        if (args.Length >= 2 && args[0] == "--screenshot")
        {
            Screenshots.Run(args[1]);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>给 Avalonia 设计器/测试复用。</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
