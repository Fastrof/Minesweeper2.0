using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MineSweeper.App.Services;
using Windows.Graphics;

namespace MineSweeper.App.Views;

/// <summary>
/// 两个"关于"窗口共用的外观设置。
///
/// 目标就是 Windows 自己的"关于 Windows"（winver）对话框那种感觉：
/// <list type="bullet">
/// <item><b>尺寸固定</b>：本机 150% 缩放下实测 winver 的外框（GetWindowRect）是 550×528 物理像素，
/// 换算成 367×352 DIP，就按这个来。</item>
/// <item><b>不能拖动改大小、不能最大化、也不能最小化</b> —— winver 标题栏上只有关闭按钮。</item>
/// </list>
/// 因为尺寸是按 DIP 给的，所以在别的缩放比例下窗口的"观感大小"与 winver 保持一致。
/// </summary>
internal static class AboutWindowChrome
{
    /// <summary>
    /// 设计宽度（DIP）。
    ///
    /// 实测 winver 在本机 150% 缩放下的外框（GetWindowRect）是 550×528 物理像素，
    /// DWM 可视边界是 536×521；两者的差就是窗口阴影内缩（14×7，和本工程主窗口完全一致）。
    /// 550/1.5 = 366.7、528/1.5 = 352，所以取 367×352 DIP —— 这样最终可视尺寸与 winver 一致。
    ///
    /// 注意这里存的是 <b>DIP</b>：运行时（<see cref="OnFirstActivated"/>）还要乘 DPI 才得到
    /// <c>AppWindow.Resize</c> 需要的物理像素。以前这里误存了物理像素，150% 缩放下窗口会比
    /// winver 大 1.5 倍。
    /// </summary>
    public const int DesignWidth = 367;

    /// <summary>设计高度（DIP），对齐 winver。</summary>
    public const int DesignHeight = 352;

    /// <summary>套用固定尺寸与"不可缩放"的窗口样式。</summary>
    public static void Apply(Window window, string title)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Title = title;
        window.AppWindow.Title = title;

        // 关键：OverlappedPresenter 的这三个开关就是"能不能拖大 / 最大化 / 最小化"
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        else
        {
            AppPaths.Log($"关于窗口的 Presenter 不是 OverlappedPresenter（{window.AppWindow.Presenter?.GetType().Name}），无法限制尺寸。");
        }

        // 标题栏左上角的小图标：用工程自己那份**多尺寸** ICO
        // （和主窗口、exe 内嵌的是同一张图。仓库根目录那份是单帧 256×252 原图，
        //  拿它当 16×16 的窗口图标会发虚，所以这里用 Assets 里处理过的版本。）
        try
        {
            if (System.IO.File.Exists(AppPaths.AppIconFile))
            {
                window.AppWindow.SetIcon(AppPaths.AppIconFile);
            }
            else
            {
                AppPaths.Log($"关于窗口图标缺失：{AppPaths.AppIconFile}");
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log($"设置关于窗口图标失败：{ex.GetType().Name}：{ex.Message}");
        }

        // DPI 要等窗口真正创建出来才拿得到，所以放到第一次激活时再按比例设尺寸
        window.Activated += OnFirstActivated;
    }

    private static void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Activated -= OnFirstActivated;

        double scale = ResolveScale(window, out uint rawDpi);
        int width = (int)Math.Round(DesignWidth * scale);
        int height = (int)Math.Round(DesignHeight * scale);

        // AppWindow.Resize 用的是物理像素，所以这里乘上缩放比例
        window.AppWindow.Resize(new SizeInt32(width, height));

        AppPaths.Log(
            $"[ABOUT] {window.Title}：设计 {DesignWidth}×{DesignHeight} DIP @ {scale:0.##}× → {width}×{height} px，" +
            $"可缩放={IsResizable(window)} · GetDpiForWindow={rawDpi}");
    }

    /// <summary>
    /// 取缩放比例。优先用 GetDpiForWindow —— 首次 Activated 时 XamlRoot 可能还没就绪
    /// （踩过这个坑：那时 RasterizationScale 还是 1.0，窗口就小了 1/3）。
    /// </summary>
    private static double ResolveScale(Window window, out uint rawDpi)
    {
        rawDpi = 0;

        try
        {
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            uint dpi = GetDpiForWindow(hwnd);
            rawDpi = dpi;
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log($"取窗口 DPI 失败，回退到 XamlRoot：{ex.Message}");
        }

        double fallback = window.Content?.XamlRoot?.RasterizationScale ?? 1.0;
        return fallback > 0 ? fallback : 1.0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private static bool IsResizable(Window window)
        => window.AppWindow.Presenter is OverlappedPresenter presenter && presenter.IsResizable;
}
