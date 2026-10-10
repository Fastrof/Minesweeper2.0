using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MineSweeper.App.Services;
using MineSweeper.Core.Audio;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Notifications;

namespace MineSweeper.App.Views;

/// <summary>
/// "关于作者"窗口（固定尺寸、不可缩放，对齐 Windows 的"关于 Windows"对话框）。
///
/// 这个窗口里藏了**两个**彩蛋，都刻意不做任何提示（没有 ToolTip、没有悬浮变色）：
/// <list type="bullet">
/// <item><b>连点主开发头像 3 下</b>（3 = 初级赠送的放大镜数）→ 给 Fastrof 戴上墨镜。</item>
/// <item><b>连点 Shinichi 头像 5 下</b> → 大彩蛋：解锁隐藏关卡「EX-1 卡布奇诺」。</item>
/// </list>
/// </summary>
public sealed partial class AboutAuthorsWindow : Window
{
    /// <summary>Fastrof 彩蛋需要连点的次数。</summary>
    private const int EggTaps = 3;

    /// <summary>Shinichi 大彩蛋需要连点的次数。</summary>
    private const int BigEggTaps = 5;

    private readonly SoundPlayer? _sound;
    private readonly INotificationSink? _notifier;
    private readonly Action? _onSecretLevelUnlocked;
    private int _tapCount;
    private int _bigTapCount;

    internal AboutAuthorsWindow(
        SoundPlayer? sound = null,
        INotificationSink? notifier = null,
        Action? onSecretLevelUnlocked = null)
    {
        _sound = sound;
        _notifier = notifier;
        _onSecretLevelUnlocked = onSecretLevelUnlocked;

        InitializeComponent();

        AboutWindowChrome.Apply(this, "关于作者");
        LoadContent();
        LoadAvatar(PrimaryAvatarBrush, "Fastrof.png");
        LoadAvatar(SecondaryAvatarBrush, "Shinichi.png");
    }

    private void LoadContent()
    {
        FooterText.Text =
            $"{AboutInfo.ProductName} v{AboutInfo.Version} · 由 {AboutInfo.PrimaryAuthor} 与 " +
            $"{AboutInfo.SecondaryAuthor} 制作。";
        NoteText.Text = AboutInfo.AuthorsNote;
    }

    /// <summary>
    /// 头像用**文件路径**加载而不是 ms-appx：unpackaged 运行下 ms-appx 不可靠
    /// （做通知图标时踩过这个坑）。原图 1448×1448 已缩到 256×256 随 exe 输出。
    /// </summary>
    private static void LoadAvatar(ImageBrush target, string fileName)
    {
        try
        {
            string path = System.IO.Path.Combine(AppPaths.AssetsDirectory, "Authors", fileName);
            if (!System.IO.File.Exists(path))
            {
                AppPaths.Log($"作者头像缺失：{path}");
                return;
            }

            target.ImageSource = new BitmapImage(new Uri(path));
        }
        catch (Exception ex)
        {
            AppPaths.Log($"作者头像加载失败（{fileName}）：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>Fastrof 的彩蛋是否已经触发过（Shinichi 大彩蛋的前置条件）。</summary>
    private bool IsFastrofEggUnlocked => EggCard.Visibility == Visibility.Visible;

    private void OnPrimaryAvatarTapped(object sender, TappedRoutedEventArgs e) => TryUnlockEgg();

    private void OnSecondaryAvatarTapped(object sender, TappedRoutedEventArgs e) => TryUnlockBigEgg();

    /// <summary>彩蛋 2：连点主开发头像 3 下 → 戴上墨镜。</summary>
    private void TryUnlockEgg()
    {
        if (EggCard.Visibility == Visibility.Visible)
        {
            return;   // 已经发现过，不再重复触发，也不弹任何提示
        }

        _tapCount++;
        if (_tapCount < EggTaps)
        {
            return;
        }

        PrimaryAvatarEggGlyph.Visibility = Visibility.Visible;
        EggCard.Visibility = Visibility.Visible;
        _sound?.Play(SoundEffect.Chord);

        // 只在 app.log 留一行痕迹，绝不弹 Toast——彩蛋不该被通知系统剧透
        _notifier?.Publish(GameNotification.QuietInfo(
            "隐藏彩蛋",
            $"关于作者：连点主开发头像 {EggTaps} 下",
            "egg.about-authors"));
    }

    /// <summary>
    /// 彩蛋 3（大彩蛋）：连点 Shinichi 头像 5 下 → 解锁隐藏关卡「EX-1 卡布奇诺」。
    ///
    /// 这是三个彩蛋里唯一一个**改变游戏内容**的：解锁后关卡面板最下面会多出一个隐藏关
    /// （16×16 / 80 雷，比第三章还狠），并且解锁状态会写进 levelprogress.json。
    /// </summary>
    private void TryUnlockBigEgg()
    {
        if (ShinichiEggCard.Visibility == Visibility.Visible)
        {
            return;
        }

        _bigTapCount++;
        if (_bigTapCount < BigEggTaps)
        {
            return;
        }

        // 前置条件：必须先触发 Fastrof 的彩蛋（他得先戴上墨镜去喝卡布奇诺，
        // bug 才会落到 Shinichi 手上）。顺序不对只给一个幽默小彩蛋，**不解锁隐藏关**。
        if (!IsFastrofEggUnlocked)
        {
            ShinichiTooEarlyCard.Visibility = Visibility.Visible;
            _sound?.Play(SoundEffect.Flag);

            _notifier?.Publish(GameNotification.QuietInfo(
                "隐藏彩蛋",
                $"关于作者：先点了 Shinichi 头像 {BigEggTaps} 下（顺序不对，只给了小彩蛋）",
                "egg.about-authors-too-early"));

            return;
        }

        ShinichiTooEarlyCard.Visibility = Visibility.Collapsed;
        SecondaryAvatarEggGlyph.Visibility = Visibility.Visible;
        ShinichiEggCard.Visibility = Visibility.Visible;
        _sound?.Play(SoundEffect.Victory);

        // 通知主窗口把隐藏关解锁并落盘（窗口之间不直接互相引用）
        _onSecretLevelUnlocked?.Invoke();

        _notifier?.Publish(GameNotification.QuietInfo(
            "隐藏彩蛋",
            $"关于作者：连点 Shinichi 头像 {BigEggTaps} 下 → 已解锁隐藏关卡 {LevelTable.SecretId}",
            "egg.secret-level"));
    }

    /// <summary>
    /// 仅供启动自检使用（环境变量 SAOLEI_ABOUT=authors-egg / authors-bigegg）：
    /// 等价于玩家连点到满次数，内部调的就是那两个 Try 方法，走的路径完全一样。
    /// </summary>
    /// <summary>仅供启动自检：只点 Shinichi 头像，不碰 Fastrof（用来验证"顺序不对"分支）。</summary>
    internal void ForceTooEarlyForCheck()
    {
        for (int i = 0; i < BigEggTaps; i++)
        {
            TryUnlockBigEgg();
        }
    }

    internal void ForceUnlockEggForCheck(bool big = false)
    {
        if (big)
        {
            // 自检要模拟"正常顺序"：先把 Fastrof 的彩蛋触发掉，否则会走到"来早了一步"分支
            for (int i = 0; i < EggTaps; i++)
            {
                TryUnlockEgg();
            }

            for (int i = 0; i < BigEggTaps; i++)
            {
                TryUnlockBigEgg();
            }

            return;
        }

        for (int i = 0; i < EggTaps; i++)
        {
            TryUnlockEgg();
        }
    }
}
