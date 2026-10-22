using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core.Audio;
using MineSweeper.Core.Localization;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 「关于游戏」面板（对应 Windows 端独立小窗口 <c>AboutGameWindow</c>）。
///
/// 藏了**彩蛋 1**：连点版本号 **10 下**（10 = 初级雷数）→ 弹出隐藏卡片「🎉 隐藏彩蛋 · 十连击」。
/// 刻意没有任何提示（无 ToolTip、无悬浮变色），触发只放一声和弦音 + 写日志，绝不弹通知。
/// </summary>
public sealed class AboutGameOverlay : Border, IOverlay
{
    /// <summary>连点版本号多少次触发。</summary>
    private const int EggTaps = 10;

    private readonly AppHost _host;
    private readonly Border _eggCard;
    private int _tapCount;

    public event EventHandler? CloseRequested;

    public AboutGameOverlay(AppHost host)
    {
        _host = host;

        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(20), MaxWidth = 520 };

        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("about.game.header"),
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        // 版本号：可连点（彩蛋 1）
        var versionLine = new TextBlock
        {
            Text = Strings.Get("about.version", UpdateService.DisplayVersion, Strings.TranslateLiteral(Strings.Get("about.licenseText"))),
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        versionLine.PointerPressed += OnVersionPressed;
        stack.Children.Add(versionLine);

        stack.Children.Add(Spacer());
        stack.Children.Add(Paragraph(Strings.Get("about.game.tagline")));
        stack.Children.Add(Paragraph(Strings.Get("about.game.classic")));
        stack.Children.Add(Paragraph(Strings.Get("about.game.tech")));
        stack.Children.Add(Paragraph(Strings.Get("about.game.scale")));
        stack.Children.Add(Paragraph(Strings.Get("about.repo", "https://github.com/Fastrof/Minesweeper2.0")));
        stack.Children.Add(Paragraph(Strings.Get("about.license",
            Strings.Get("about.licenseText"), Strings.Get("about.redLine"))));
        stack.Children.Add(Paragraph(Strings.Get("about.credits",
            Strings.Get("about.game.header"),
            UpdateService.DisplayVersion,
            "Fastrof",
            "Shinichi")));

        // 彩蛋卡片（默认隐藏）
        _eggCard = new Border
        {
            IsVisible = false,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.Accent),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "🎉 " + LocalizationService.T("egg.ten.title", "隐藏彩蛋 · 十连击", "Hidden egg · Ten taps"),
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(UiPalette.Current.Accent),
                    },
                    new TextBlock
                    {
                        Text = LocalizationService.T("egg.ten.body",
                            "你连点了版本号 10 下 —— 和初级雷数一样多。这个彩蛋没有提示，也没有成就，只有你自己知道。",
                            "You tapped the version 10 times - exactly the beginner mine count."),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(UiPalette.Current.SubText),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
        stack.Children.Add(_eggCard);

        var close = new Button
        {
            Content = LocalizationService.Get("common.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(close);

        Child = new Border
        {
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxHeight = 640,
            Child = new ScrollViewer { Content = stack },
        };
    }

    /// <summary>彩蛋 1：连点版本号 10 下。触发一次后不再重复，也不弹通知。</summary>
    private void OnVersionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_eggCard.IsVisible || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _tapCount++;
        if (_tapCount < EggTaps)
        {
            return;
        }

        _eggCard.IsVisible = true;
        _host.Sound.Play(SoundEffect.Chord);
        AppPaths.Log($"[记录/Info] 隐藏彩蛋：关于游戏 · 连点版本号 {EggTaps} 下");
    }

    /// <summary>截图/自检用：直接触发彩蛋（等价于连点版本号 10 下）。</summary>
    public void TriggerEggForScreenshot()
    {
        _eggCard.IsVisible = true;
        _host.Sound.Play(SoundEffect.Chord);
        AppPaths.Log($"[记录/Info] 隐藏彩蛋：关于游戏 · 连点版本号 {EggTaps} 下（自检触发）");
    }

    private static Control Spacer() => new Border { Height = 6 };

    private static Control Section(string text) => new TextBlock
    {
        Text = text,
        Foreground = new SolidColorBrush(UiPalette.Current.Accent),
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static Control Paragraph(string text) => new TextBlock
    {
        Text = text,
        Foreground = new SolidColorBrush(UiPalette.Current.SubText),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };
}

/// <summary>
/// 「关于作者」面板（对应 Windows 端 <c>AboutAuthorsWindow</c>）。
///
/// 藏了**彩蛋 2 / 3 / 4**：
///   - 连点 **Fastrof 头像 3 下**（3 = 初级赠送的放大镜数）→ 戴上 😎 + 卡布奇诺台词；
///   - 连点 **Shinichi 头像 5 下** → 若已触发 Fastrof 彩蛋，则 🐛 出现并**解锁隐藏关卡**；
///     顺序不对只给「来早了一步」的小彩蛋。
/// 彩蛋由角色自己的台词引导，不需要额外提示 UI。
/// </summary>

/// <summary>
/// 「关于作者」面板 —— **1:1 复刻 Windows 端 <c>AboutAuthorsWindow</c>（v1.5.1）**。
///
/// 布局来源：<c>Assets\About\Windows.png</c> / <c>Linux.png</c> 两枚平台图标 +
/// 头像 60×60 圆形、名字 17px、角色 12px、职责 11px(opacity .7)、卡片 Padding 10 / CornerRadius 6、
/// 外层 ScrollViewer 内边距 14、行间距 14 —— 数值全部照抄 XAML。
///
/// 三个彩蛋（触发次数/顺序与 Windows 端完全一致）：
///   - 连点 Fastrof 头像 3 下 → 戴上 😎 + 「主开发模式」卡片；
///   - 连点 Shinichi 头像 5 下 → 已先触发 Fastrof 则 🐛 + 解锁隐藏关卡「卡布奇诺」；
///     顺序不对只给「来早了一步」小彩蛋。
/// </summary>
public sealed class AboutAuthorsOverlay : Border, IOverlay
{
    private const int EggTaps = 3;
    private const int BigEggTaps = 5;

    private readonly AppHost _host;
    private readonly Action _onSecretLevelUnlocked;
    private readonly Border _fastrofCard;
    private readonly Border _shinichiCard;
    private readonly Border _tooEarlyCard;
    private readonly TextBlock _fastrofGlyph;
    private readonly TextBlock _shinichiGlyph;
    private int _tapCount;
    private int _bigTapCount;

    public event EventHandler? CloseRequested;

    public AboutAuthorsOverlay(AppHost host, Action onSecretLevelUnlocked)
    {
        _host = host;
        _onSecretLevelUnlocked = onSecretLevelUnlocked;

        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        // 外层间距照抄 Windows：ScrollViewer Padding=14 + StackPanel Spacing=14
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(14) };

        // ---------- 1) Fastrof 行（右侧 Windows 平台图标） ----------
        _fastrofGlyph = new TextBlock
        {
            Text = "😎",
            FontSize = 26,
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(BuildPersonRow(
            "Fastrof.png",
            "Fastrof",
            Strings.Get("about.authors.role1"),
            Strings.Get("about.authors.tag1"),
            "Windows.png",
            TryUnlockEgg,
            _fastrofGlyph));

        // ---------- 2) 彩蛋 2 的奖励卡片 ----------
        _fastrofCard = BuildEggCard(
            LocalizationService.T("egg.authors.primaryTitle", "😎 隐藏彩蛋 · 主开发模式", "😎 Hidden egg · Lead developer mode"),
            LocalizationService.T("egg.authors.primaryBody1", "你给主开发戴上了墨镜。", "You gave the lead developer sunglasses."),
            LocalizationService.T(
                "egg.authors.primaryBody2",
                "咦！我怎么看不见Bug了！刚好，休息一下，我想品尝一下“卡布奇诺”。你去找Shinichi吧~",
                "Wait - I can't see the bugs anymore! Perfect, I'll take a break and enjoy a cappuccino. Go talk to Shinichi~"));
        stack.Children.Add(_fastrofCard);

        // ---------- 3) Shinichi 行（右侧 Linux/Tux 图标） ----------
        _shinichiGlyph = new TextBlock
        {
            Text = "🐛",
            FontSize = 26,
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(BuildPersonRow(
            "Shinichi.png",
            "Shinichi",
            Strings.Get("about.authors.role2"),
            Strings.Get("about.authors.tag2"),
            "Linux.png",
            TryUnlockBigEgg,
            _shinichiGlyph));

        // ---------- 4) 大彩蛋卡片（隐藏） ----------
        _shinichiCard = BuildEggCard(
            LocalizationService.T("egg.authors.bigTitle", "🐛 大彩蛋 · Shinichi 的挑战", "🐛 Big egg · Shinichi's challenge"),
            LocalizationService.T(
                "egg.authors.bigBody1",
                "Fastrof 戴着墨镜去喝卡布奇诺了，于是所有 bug 都归 Shinichi。",
                "Fastrof went for a cappuccino in sunglasses, so all the bugs now belong to Shinichi."),
            LocalizationService.T(
                "egg.authors.bigBody2",
                "他随手撒下 100 多颗雷，还不许你带道具。",
                "He scattered over 100 mines and won't let you bring items."),
            LocalizationService.T(
                "egg.authors.bigBody3",
                "已解锁隐藏关卡「卡布奇诺」—— 去「关卡模式」最下面找它（只在本次运行有效）。",
                "Secret level \"Cappuccino\" unlocked - find it at the bottom of Level Mode (valid for this run only)."),
            emphasizeLast: true);
        stack.Children.Add(_shinichiCard);

        // ---------- 5) 顺序不对时的小彩蛋（隐藏） ----------
        _tooEarlyCard = BuildEggCard(
            LocalizationService.T("egg.authors.earlyTitle", "😴 小彩蛋 · 来早了一步", "😴 Small egg · You're early"),
            LocalizationService.T(
                "egg.authors.earlyBody1",
                "你连点了 Shinichi 五下。他抬起头看了你一眼：",
                "You tapped Shinichi five times. He looks up at you:"),
            LocalizationService.T(
                "egg.authors.earlyBody2",
                "「Fastrof 还没跑路呢，bug 都还堆在他那儿。你先去把 Fastrof 送走，我这边才有活干。」",
                "\"Fastrof hasn't run off yet - the bugs are still his. Send him off first, then I'll have work to do.\""));
        stack.Children.Add(_tooEarlyCard);

        // ---------- 6) 分隔线 + 贡献者 ----------
        stack.Children.Add(BuildSeparator());
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("about.contributors.header"),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        stack.Children.Add(BuildContributorRow());

        // ---------- 7) 分隔线 + 页脚 ----------
        stack.Children.Add(BuildSeparator());
        var footer = new StackPanel { Spacing = 3 };
        footer.Children.Add(new TextBlock
        {
            Text = Strings.Get("about.credits", Strings.Get("about.game.header"), UpdateService.DisplayVersion, "Fastrof", "Shinichi"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        footer.Children.Add(new TextBlock
        {
            Text = Strings.Get("about.authorsFooter"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(UiPalette.Current.SubText),
        });
        stack.Children.Add(footer);

        var close = new Button
        {
            Content = LocalizationService.Get("common.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(close);

        Child = new Border
        {
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(10),
            Child = new ScrollViewer { Content = stack },
        };
    }

    /// <summary>Fastrof 的彩蛋是否已经触发过（Shinichi 大彩蛋的前置条件）。</summary>
    private bool IsFastrofEggUnlocked => _fastrofCard.IsVisible;

    /// <summary>彩蛋 2：连点主开发头像 3 下 → 戴上墨镜。</summary>
    private void TryUnlockEgg()
    {
        if (_fastrofCard.IsVisible)
        {
            return;
        }

        _tapCount++;
        if (_tapCount < EggTaps)
        {
            return;
        }

        _fastrofGlyph.IsVisible = true;
        _fastrofCard.IsVisible = true;
        _host.Sound.Play(SoundEffect.Chord);
        AppPaths.Log($"[记录/Info] 隐藏彩蛋：关于作者 · 连点 Fastrof 头像 {EggTaps} 下");
    }

    /// <summary>彩蛋 3 / 4：连点 Shinichi 头像 5 下。</summary>
    private void TryUnlockBigEgg()
    {
        if (_shinichiCard.IsVisible)
        {
            return;
        }

        _bigTapCount++;
        if (_bigTapCount < BigEggTaps)
        {
            return;
        }

        if (!IsFastrofEggUnlocked)
        {
            _tooEarlyCard.IsVisible = true;
            _host.Sound.Play(SoundEffect.Flag);
            AppPaths.Log($"[记录/Info] 隐藏彩蛋：先点 Shinichi 头像 {BigEggTaps} 下（顺序不对，只给小彩蛋）");
            return;
        }

        _tooEarlyCard.IsVisible = false;
        _shinichiGlyph.IsVisible = true;
        _shinichiCard.IsVisible = true;
        _host.Sound.Play(SoundEffect.Victory);
        _onSecretLevelUnlocked();
        AppPaths.Log($"[记录/Info] 隐藏彩蛋：连点 Shinichi 头像 {BigEggTaps} 下 → 解锁隐藏关卡");
    }

    /// <summary>截图/自检用：按正确顺序触发两个彩蛋（Fastrof 3 下 → Shinichi 5 下）。</summary>
    public void TriggerEggsForScreenshot()
    {
        for (int i = 0; i < EggTaps; i++)
        {
            TryUnlockEgg();
        }

        for (int i = 0; i < BigEggTaps; i++)
        {
            TryUnlockBigEgg();
        }
    }
    // ---------------------------------------------------------------- 布局小工具（数值照抄 Windows XAML）

    /// <summary>
    /// 一行"人物"：60×60 圆头像（可点，带 26px 彩蛋覆盖层）+ 名字/角色/职责 + 右侧 96×96 平台图标。
    /// </summary>
    private static Control BuildPersonRow(
        string avatarFile,
        string name,
        string role,
        string duties,
        string platformIconFile,
        Action onAvatarTap,
        TextBlock eggGlyph)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        // 头像 + 覆盖层
        var avatarHost = new Grid { Width = 60, Height = 60 };
        var avatarBorder = new Border
        {
            Width = 60,
            Height = 60,
            CornerRadius = new CornerRadius(30),
            ClipToBounds = true,
            Background = new SolidColorBrush(UiPalette.Current.ChromeBg),
            Child = BuildImage(avatarFile, 60, 60, Stretch.UniformToFill),
        };
        avatarBorder.Cursor = new Cursor(StandardCursorType.Hand);
        avatarBorder.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(avatarBorder).Properties.IsLeftButtonPressed)
            {
                onAvatarTap();
            }
        };
        avatarHost.Children.Add(avatarBorder);
        avatarHost.Children.Add(eggGlyph);
        Grid.SetColumn(avatarHost, 0);
        row.Children.Add(avatarHost);

        // 名字 / 角色 / 职责
        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        info.Children.Add(new TextBlock
        {
            Text = role,
            FontSize = 12,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        info.Children.Add(new TextBlock
        {
            Text = duties,
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        Grid.SetColumn(info, 1);
        row.Children.Add(info);

        // 右侧平台图标 96×96（VerticalAlignment=Top，Margin 4,0,2,0）
        var icon = BuildImage(platformIconFile, 96, 96, Stretch.Uniform);
        icon.Margin = new Thickness(4, 0, 2, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(icon, 2);
        row.Children.Add(icon);

        return row;
    }

    private static Control BuildContributorRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var avatar = new Border
        {
            Width = 60,
            Height = 60,
            CornerRadius = new CornerRadius(30),
            ClipToBounds = true,
            Background = new SolidColorBrush(UiPalette.Current.ChromeBg),
            Child = BuildImage("LLJ99999.png", 60, 60, Stretch.UniformToFill),
        };
        row.Children.Add(avatar);

        var info = new StackPanel
        {
            MaxWidth = 255,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
        };
        info.Children.Add(new TextBlock
        {
            Text = "LLJ99999",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        info.Children.Add(new TextBlock
        {
            Text = Strings.Get("about.contributors.role"),
            FontSize = 12,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        info.Children.Add(new TextBlock
        {
            Text = Strings.Get("about.contributors.tag"),
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        row.Children.Add(info);

        return row;
    }

    /// <summary>彩蛋卡片：Padding 10 / CornerRadius 6 / 标题 13 SemiBold + 正文 12 Wrap。</summary>
    private static Border BuildEggCard(
        string title,
        string body1,
        string body2,
        string? body3 = null,
        bool emphasizeLast = false)
    {
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });
        stack.Children.Add(Paragraph(body1));
        stack.Children.Add(Paragraph(body2));
        if (body3 is not null)
        {
            var last = Paragraph(body3);
            if (emphasizeLast)
            {
                last.FontWeight = FontWeight.SemiBold;
            }

            stack.Children.Add(last);
        }

        return new Border
        {
            IsVisible = false,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.CardBorder),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(UiPalette.Current.Text),
    };

    private static Control BuildSeparator() => new Border
    {
        Height = 1,
        Background = new SolidColorBrush(UiPalette.Current.CardBorder),
    };

    /// <summary>从 <c>avares://</c> 加载头像 / 平台图标；失败时退化为空图（不崩）。</summary>
    private static Control BuildImage(string fileName, double width, double height, Stretch stretch)
    {
        string folder = fileName is "Windows.png" or "Linux.png" ? "About" : "Authors";
        var image = new Image
        {
            Width = width,
            Height = height,
            Stretch = stretch,
        };

        try
        {
            image.Source = new Bitmap(AssetLoader.Open(
                new Uri($"avares://Minesweeper2/Assets/{folder}/{fileName}")));
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
        {
            AppPaths.Log($"[关于] 图片加载失败：{folder}/{fileName}");
        }

        return image;
    }
}