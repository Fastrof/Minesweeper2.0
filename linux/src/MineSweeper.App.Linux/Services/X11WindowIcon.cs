using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 用 X11 原生方式给窗口写 <c>_NET_WM_ICON</c>（多尺寸 ARGB）。
///
/// 背景：Avalonia 12 自带的 <see cref="Window.Icon"/> 在 X11 下写出的属性，
/// Xfwm4（WSLg 的窗口管理器）读进去会渲染成空框（实测：属性存在、尺寸正确，
/// 但标题栏一直是默认占位图；连 <c>show_app_icon</c> 打开也一样）。
/// 所以这里按 ICCCM 规范自己写一遍：
///   _NET_WM_ICON = CARDINAL[]，每张图 = [width, height, 0xAARRGGBB × width×height]
/// 多张图首尾相接，窗口管理器挑最合适的尺寸用。写多尺寸同时也让任务栏/Alt+Tab 更清晰。
///
/// 失败一律静默（没有 libX11 / 不是 X11 会话时直接跳过，绝不影响启动）。
/// </summary>
public static class X11WindowIcon
{
    private const int XaCardinal = 6;
    private const int PropModeReplace = 0;

    /// <summary>打包用到的尺寸档位（与 .deb 的 hicolor 图标一致）。</summary>
    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 128, 256];

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? displayName);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        IntPtr data,
        int elementCount);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    /// <summary>
    /// 把 <paramref name="icon"/> 写成多尺寸窗口图标。必须在窗口已创建（拿到 XID）后调用。
    /// </summary>
    public static void Apply(Window window, Bitmap icon)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(icon);

        try
        {
            IntPtr xid = ResolveWindowId(window);
            if (xid == IntPtr.Zero)
            {
                AppPaths.Log("[图标] 拿不到 X11 窗口 ID，跳过原生图标设置");
                return;
            }

            IntPtr display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
            {
                AppPaths.Log("[图标] 打不开 X11 display，跳过原生图标设置");
                return;
            }

            try
            {
                uint[] pixels = BuildIconData(icon);
                IntPtr propertyAtom = XInternAtom(display, "_NET_WM_ICON", onlyIfExists: false);
                IntPtr cardinalAtom = new(XaCardinal);

                IntPtr buffer = Marshal.AllocHGlobal(pixels.Length * sizeof(uint));
                try
                {
                    Marshal.Copy(pixels.Select(p => unchecked((int)p)).ToArray(), 0, buffer, pixels.Length);
                    XChangeProperty(
                        display, xid, propertyAtom, cardinalAtom,
                        format: 32, mode: PropModeReplace, buffer, pixels.Length);
                    XFlush(display);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                AppPaths.Log($"[图标] 已写入 _NET_WM_ICON（{Sizes.Length} 档尺寸，窗口 0x{xid.ToInt64():X}）");
            }
            finally
            {
                XCloseDisplay(display);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                       or FormatException or ArgumentException or InvalidOperationException)
        {
            AppPaths.Log($"[图标] 原生图标设置失败（已忽略）：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>按 _NET_WM_ICON 的格式拼出所有尺寸的像素数据。</summary>
    private static uint[] BuildIconData(Bitmap icon)
    {
        var data = new List<uint>(Sizes.Sum(s => (s * s) + 2));

        foreach (int size in Sizes)
        {
            uint[] bgra = ReadScaledPixels(icon, size);
            data.Add((uint)size);
            data.Add((uint)size);

            foreach (uint pixel in bgra)
            {
                // Skia 给的是 BGRA8888（小端序），_NET_WM_ICON 要 0xAARRGGBB
                uint b = (pixel >> 0) & 0xFF;
                uint g = (pixel >> 8) & 0xFF;
                uint r = (pixel >> 16) & 0xFF;
                uint a = (pixel >> 24) & 0xFF;

                // 预乘 alpha → 直通 alpha（Xfwm4 按非预乘渲染，不做这步边缘会发黑）
                if (a is > 0 and < 255)
                {
                    r = Math.Min(255, (r * 255) / a);
                    g = Math.Min(255, (g * 255) / a);
                    b = Math.Min(255, (b * 255) / a);
                }

                data.Add((a << 24) | (r << 16) | (g << 8) | b);
            }
        }

        return data.ToArray();
    }

    /// <summary>把图标缩放到目标边长并读出 BGRA 像素。</summary>
    private static uint[] ReadScaledPixels(Bitmap icon, int size)
    {
        using var scaled = icon.CreateScaledBitmap(
            new PixelSize(size, size),
            BitmapInterpolationMode.HighQuality);

        var buffer = new uint[size * size];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            // CopyPixels 的最后一个参数是 stride（每行字节数），像素格式由 Bitmap 自身决定
            scaled.CopyPixels(
                new PixelRect(0, 0, size, size),
                handle.AddrOfPinnedObject(),
                buffer.Length * sizeof(uint),
                size * sizeof(uint));
        }
        finally
        {
            handle.Free();
        }

        return buffer;
    }

    /// <summary>拿到窗口的 X11 XID；不是 X11 会话时返回 0。</summary>
    private static IntPtr ResolveWindowId(Window window)
    {
        IPlatformHandle? handle = window.TryGetPlatformHandle();
        return handle?.Handle ?? IntPtr.Zero;
    }
}
