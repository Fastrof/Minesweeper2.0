using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MineSweeper.App.Services;
using MineSweeper.Core.Audio;
using MineSweeper.Core.Notifications;

namespace MineSweeper.App.Views;

/// <summary>
/// "关于游戏"窗口（固定尺寸、不可缩放，对齐 Windows 的"关于 Windows"对话框）。
///
/// 这个窗口里藏了一个彩蛋：<b>连点版本号 10 下</b>。
/// 刻意不做任何提示——没有 ToolTip、没有悬浮变色、也没有可点击的视觉暗示。
/// </summary>
public sealed partial class AboutGameWindow : Window
{
    /// <summary>彩蛋 1 需要连点的次数（10 = 初级雷数）。</summary>
    private const int EggTaps = 10;

    private readonly SoundPlayer? _sound;
    private readonly INotificationSink? _notifier;
    private int _tapCount;

    internal AboutGameWindow(SoundPlayer? sound = null, INotificationSink? notifier = null)
    {
        _sound = sound;
        _notifier = notifier;

        InitializeComponent();

        AboutWindowChrome.Apply(this, "关于游戏");
        LoadContent();
    }

    private void LoadContent()
    {
        TaglineText.Text = AboutText.Tagline;

        // 彩蛋 1 的点击目标：版本号这一行
        VersionText.Text = $"版本 {AboutInfo.Version} · {AboutInfo.License}";

        BodyText.Text = AboutText.Body;
        TechText.Text = AboutText.Tech;
        StatsText.Text = AboutText.Stats;

        RepoText.Text = $"开源地址：{AboutInfo.RepositoryText}";
        LicenseText.Text = $"许可：{AboutInfo.License} · {AboutText.RedLine}";
    }

    private void OnVersionTapped(object sender, TappedRoutedEventArgs e) => TryUnlockEgg();

    /// <summary>彩蛋 1：连点版本号 10 下。</summary>
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

        EggCard.Visibility = Visibility.Visible;
        _sound?.Play(SoundEffect.Chord);

        // 只在 app.log 留一行痕迹，绝不弹 Toast——彩蛋不该被通知系统剧透
        _notifier?.Publish(GameNotification.QuietInfo(
            "隐藏彩蛋",
            $"关于游戏：连点版本号 {EggTaps} 下",
            "egg.about-game"));
    }
    /// <summary>
    /// 仅供启动自检使用（环境变量 SAOLEI_ABOUT=*-egg）：等价于玩家连点到满次数，
    /// 内部调的就是 <see cref="TryUnlockEgg"/>，走的路径完全一样。
    /// </summary>
    internal void ForceUnlockEggForCheck()
    {
        for (int i = 0; i < EggTaps; i++)
        {
            TryUnlockEgg();
        }
    }
}