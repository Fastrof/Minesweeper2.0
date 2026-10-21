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
/// <item><b>尺寸固定</b>：winver 是 System DPI aware；本机系统 DPI 96（100%）下实测外框
/// （GetWindowRect）是 <b>550×528 物理像素</b>，也就是 <b>550×528 DIP</b>；150% 下是 825×792 物理。</item>
/// <item><b>不能拖动改大小、不能最大化、也不能最小化</b> —— winver 标题栏上只有关闭按钮。</item>
/// </list>
/// 因为尺寸按 DIP 给、运行时再乘 <c>GetDpiForWindow</c>，所以各缩放比例下都与 winver 的物理尺寸一致。
/// </summary>
internal static class AboutWindowChrome
{
    /// <summary>
    /// 设计宽度（DIP），对齐 winver。
    ///
    /// winver 是 System DPI aware：系统 DPI 96 时外框 550×528 物理，换算成 DIP 就是 550×528；
    /// 150% 时 Windows 会把它放大到 825×792 物理。所以这里存 DIP，运行时乘 DPI。
    ///
    /// v1.4 曾误把"100% 下的 550×528 物理"当成"150% 下的物理尺寸"，据此改成 367×352 DIP；
    /// 结果在 100% 缩放下窗口只有 winver 的 2/3（367×352 对 550×528），v1.5.1 修回 550×528。
    /// </summary>
    public const int DesignWidth = 550;

    /// <summary>设计高度（DIP），对齐 winver。</summary>
    public const int DesignHeight = 528;

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
