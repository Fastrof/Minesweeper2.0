using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MineSweeper.App.Linux.Controls;
using MineSweeper.App.Linux.Views;
using MineSweeper.Core;
using MineSweeper.Core.Levels;

namespace MineSweeper.App.Linux;

/// <summary>
/// 无头渲染截图（用于验证"界面确实画得出来"，也方便 CI 做视觉回归）。
///
/// 用 Avalonia.Headless + Skia 真实光栅化：不需要 X11/Wayland。
/// 用法：<c>dotnet run -- --screenshot &lt;输出目录&gt;</c>
/// </summary>
internal static class Screenshots
{
    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        AppHost host = AppHost.Create();
        var window = new MainWindow(host);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Console.WriteLine($"A11y: {window.BoardAccessibleName}");

        // 0) 本版新增
        window.OpenWhatsNewForScreenshot(Services.UpdateService.DisplayVersion);
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "00-whatsnew.png"));
        window.CloseOverlayForScreenshot();
        Dispatcher.UIThread.RunJobs();

        Capture(window, Path.Combine(outputDirectory, "01-initial.png"));

        GameSession? session = host.Game.Session;
        if (session is null)
        {
            Console.WriteLine("no session");
            return;
        }

        // 1) 翻开中心格（首点安全布雷 + 洪水填充）
        int center = (session.Board.Height / 2 * session.Board.Width) + (session.Board.Width / 2);
        host.Game.Reveal(center);
        PumpFlood(host.Game);
        window.RefreshBoard();
        Capture(window, Path.Combine(outputDirectory, "02-first-click.png"));

        // 2) 再开几格 + 插两面旗
        host.Game.Reveal(0);
        host.Game.Reveal(center + 1);
        host.Game.ToggleFlag(session.Board.Width - 1);
        host.Game.ToggleFlag(session.Board.Width - 2);
        PumpFlood(host.Game);
        window.RefreshBoard();
        Capture(window, Path.Combine(outputDirectory, "03-flag-and-numbers.png"));

        // 3b) 失败卡片：故意踩一颗雷
        int mineIndex = -1;
        for (int i = 0; i < session.Board.CellCount; i++)
        {
            if (session.Board.IsMine(i) && !session.Board.IsRevealed(i) && !session.Board.IsFlagged(i))
            {
                mineIndex = i;
                break;
            }
        }

        if (mineIndex >= 0)
        {
            host.Game.Reveal(mineIndex);
            window.RefreshBoard();
            Capture(window, Path.Combine(outputDirectory, "03b-defeat.png"));
            window.CloseOverlayForScreenshot();
        }

        // 3c) 声呐结果卡片
        host.Game.StartPreset(DifficultyPreset.Intermediate);
        host.Game.Reveal(6 * 16 + 8);   // 先布雷，道具才可用
        window.FitBoard();

        int sonarCenter = -1;
        if (host.Game.Session is { } sonarSession)
        {
            for (int y = 1; y < sonarSession.Board.Height - 1 && sonarCenter < 0; y++)
            {
                for (int x = 1; x < sonarSession.Board.Width - 1; x++)
                {
                    int c = (y * sonarSession.Board.Width) + x;
                    if (!sonarSession.Board.IsRevealed(c))
                    {
                        sonarCenter = c;
                        break;
                    }
                }
            }
        }

        if (sonarCenter >= 0)
        {
            Core.SonarScanResult sonar = host.Game.UseSonar(sonarCenter);
            Console.WriteLine($"声呐诊断: success={sonar.Success} check={sonar.Check} center={sonarCenter} remaining={sonar.RemainingCount} mines={sonar.MineCount}");
            if (sonar.Success)
            {
                window.ShowSonarForScreenshot(sonar);
                Dispatcher.UIThread.RunJobs();
                Capture(window, Path.Combine(outputDirectory, "03c-sonar.png"));
                window.CloseOverlayForScreenshot();
                Dispatcher.UIThread.RunJobs();
                Capture(window, Path.Combine(outputDirectory, "03c-sonar-mark.png"));
            }
        }

        // 3d) 关卡局：目标条 + 关卡导航（验证"关卡界面"完整）
        if (LevelTable.At(0) is { } firstLevel)
        {
            host.Game.StartLevel(firstLevel);
            window.FitBoard();
            Dispatcher.UIThread.RunJobs();
            Capture(window, Path.Combine(outputDirectory, "03d-level-playing.png"));
        }

        // 3) 高级盘
        host.Game.StartPreset(DifficultyPreset.Expert);
        window.FitBoard();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "04-expert.png"));

        // 4) 设置面板
        window.OpenSettingsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "05-settings.png"));
        window.CloseOverlayForScreenshot();

        // 5) 关卡面板
        window.OpenLevelsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "06-levels.png"));
        window.CloseOverlayForScreenshot();

        // 6) 商店
        window.OpenShopForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "07-shop.png"));
        window.CloseOverlayForScreenshot();

        // 7) 成就
        window.OpenAchievementsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "08-achievements.png"));
        window.CloseOverlayForScreenshot();

        // 8) 记录
        window.OpenRecordsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "09-records.png"));
        window.CloseOverlayForScreenshot();

        // 9) 关于游戏
        window.OpenAboutGameForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "10-about-game.png"));
        window.CloseOverlayForScreenshot();

        // 9b) 关于作者（含彩蛋入口）
        window.OpenAboutAuthorsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "10b-about-authors.png"));
        window.CloseOverlayForScreenshot();

        // 9c) 彩蛋：关于游戏（十连击）+ 关于作者（墨镜 + 隐藏关）
        window.OpenAboutWithEggsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "10c-egg-game.png"));
        window.CloseOverlayForScreenshot();

        window.OpenAuthorsWithEggsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "10d-egg-authors.png"));
        window.CloseOverlayForScreenshot();

        // 9d) 彩蛋解锁后的关卡面板（应出现隐藏关 EX-1）
        window.OpenLevelsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "10e-levels-secret.png"));
        window.CloseOverlayForScreenshot();

        // 10b) 放大镜透视后（验证透视格显示数字/雷）
        host.Game.StartPreset(DifficultyPreset.Intermediate);
        window.FitBoard();
        // 道具要求已布雷：先点第一格（首点保证安全）
        host.Game.Reveal(6 * 16 + 8);
        window.RefreshBoard();

        // 找一个 2×2 全未翻开的中心，保证透视效果看得见
        int magCenter = -1;
        if (host.Game.Session is { } magSession)
        {
            for (int y = 0; y < magSession.Board.Height - 1 && magCenter < 0; y++)
            {
                for (int x = 0; x < magSession.Board.Width - 1; x++)
                {
                    int c = (y * magSession.Board.Width) + x;
                    if (!magSession.Board.IsRevealed(c)
                        && !magSession.Board.IsRevealed(c + 1)
                        && !magSession.Board.IsRevealed(c + magSession.Board.Width)
                        && !magSession.Board.IsRevealed(c + magSession.Board.Width + 1))
                    {
                        magCenter = c;
                        break;
                    }
                }
            }
        }

        string magMessage = string.Empty;
        bool magOk = false;
        if (magCenter >= 0)
        {
            magOk = host.Game.UseMagnifier(magCenter, out magMessage);
        }
        Console.WriteLine($"放大镜: {magOk} · {magMessage}");
        window.RefreshBoard();
        Capture(window, Path.Combine(outputDirectory, "11b-magnifier-result.png"));
        window.CloseOverlayForScreenshot();

        // 11c) 声呐标记：同一局再扫一次，验证棋盘上留下浅蓝区域
        if (host.Game.Session is { } sonarSession2)
        {
            int sonarCenter2 = -1;
            for (int y = 1; y < sonarSession2.Board.Height - 1 && sonarCenter2 < 0; y++)
            {
                for (int x = 1; x < sonarSession2.Board.Width - 1; x++)
                {
                    int c = (y * sonarSession2.Board.Width) + x;
                    if (!sonarSession2.Board.IsRevealed(c))
                    {
                        sonarCenter2 = c;
                        break;
                    }
                }
            }

            if (sonarCenter2 >= 0)
            {
                Core.SonarScanResult sonar2 = host.Game.UseSonar(sonarCenter2);
                Console.WriteLine($"声呐标记: success={sonar2.Success} check={sonar2.Check} center={sonarCenter2}");
                if (sonar2.Success)
                {
                    window.ShowSonarForScreenshot(sonar2);
                    Dispatcher.UIThread.RunJobs();
                    Capture(window, Path.Combine(outputDirectory, "11c-sonar-dialog.png"));
                    window.CloseOverlayForScreenshot();
                    Dispatcher.UIThread.RunJobs();
                    Capture(window, Path.Combine(outputDirectory, "11d-sonar-mark.png"));
                }
            }
        }

        // 10) 放大镜瞄准态（区域框跟随鼠标；这里把鼠标放到棋盘中心）
        host.Game.StartPreset(DifficultyPreset.Intermediate);
        window.FitBoard();
        window.BeginAimingForScreenshot(AimingMode.Magnifier);
        // 无头环境下没有真实鼠标，用 headless 的输入注入把光标放到棋盘中心，才能画出瞄准框
        window.MouseMove(new Point(410, 350));
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "11-aiming-magnifier.png"));

        // 11) 百万格远景（验证"行色带合并"）
        if (GameSession.TryCreate(1000, 1000, 100_000, 20261021, new GameSettings(), out GameSession? huge, out _)
            && huge is not null)
        {
            host.Game.Adopt(huge);
            window.FitBoard();
            Dispatcher.UIThread.RunJobs();
            Capture(window, Path.Combine(outputDirectory, "12-million-cells.png"));
            Console.WriteLine(
                $"1000x1000 远景：可见 {window.BoardCells:N0} 格 → 绘制 {window.BoardDrawCalls:N0} 次，" +
                $"{window.BoardRenderMs:F2} ms");
        }

        // 12) 深色界面 + Dark 调色板（验证主题切换链路）
        // 只为出图，不写存档（避免把用户的主题永久改成深色）
        host.Storage.Settings.UiTheme = "dark";
        host.Storage.Settings.ThemeId = MineSweeper.Core.Themes.BoardPalette.Dark.Id;
        host.Theme.NotifyChanged();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "13-dark-theme.png"));

        // 13) 英文界面（验证双语切换链路）
        host.L10n.SetLanguage("en");
        window.OpenSettingsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "14-english.png"));
        window.CloseOverlayForScreenshot();
        host.L10n.SetLanguage("zh-Hans");

        // 16) 提示高亮（回归：渲染期间 InvalidateVisual 会直接崩进程）
        host.Game.StartPreset(DifficultyPreset.Intermediate);
        host.Game.Reveal(6 * 16 + 8);
        window.FitBoard();
        Dispatcher.UIThread.RunJobs();

        if (host.Game.Session is { } hintSession && hintSession.Board.MinesPlaced)
        {
            int hintIndex = host.Game.FindHint();
            Console.WriteLine($"提示高亮诊断: index={hintIndex}");
            if (hintIndex >= 0)
            {
                host.Game.MarkHintUsed();
                window.ShowHintForScreenshot(hintIndex);
                Dispatcher.UIThread.RunJobs();
                Capture(window, Path.Combine(outputDirectory, "15-hint-highlight.png"));
                Dispatcher.UIThread.RunJobs();
                Console.WriteLine("提示高亮诊断: 渲染通过，没有崩溃");
            }
        }

        // 16c) 回归：提示之后重开一局，绿框必须消失（不能留在新盘上）
        host.Game.StartPreset(DifficultyPreset.Beginner);
        window.FitBoard();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "15c-hint-cleared.png"));
        Console.WriteLine("提示清理诊断: 已重开新盘并截图，绿框应已消失");

        // 16b) 窄窗口下的关卡面板（回归：面板写死 900 宽时左边会被裁掉）
        window.Width = 700;
        window.Height = 700;
        Dispatcher.UIThread.RunJobs();
        window.OpenLevelsForScreenshot();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "15b-levels-narrow.png"));
        window.CloseOverlayForScreenshot();
        Dispatcher.UIThread.RunJobs();

        // 18) 账号金币诊断：赚币入账 + 重开不清零
        host.Game.StartPreset(DifficultyPreset.Beginner);
        if (host.Game.Session is { } coinSession)
        {
            int coinBefore = host.Game.AccountCoins;
            host.Game.Reveal((coinSession.Board.Height / 2 * coinSession.Board.Width) + (coinSession.Board.Width / 2));
            int mineIdx = -1;
            for (int i = 0; i < coinSession.Board.CellCount; i++)
            {
                if (coinSession.Board.IsMine(i) && !coinSession.Board.IsFlagged(i))
                {
                    mineIdx = i;
                    break;
                }
            }

            if (mineIdx >= 0)
            {
                host.Game.ToggleFlag(mineIdx);
            }

            host.Game.Tick();
            int coinAfterFlag = host.Game.AccountCoins;

            host.Game.StartPreset(DifficultyPreset.Beginner);
            host.Game.Tick();
            int coinAfterRestart = host.Game.AccountCoins;

            Console.WriteLine(
                $"金币诊断: before={coinBefore} afterFlag={coinAfterFlag} afterRestart={coinAfterRestart} " +
                $"(期望 afterFlag=before+1, afterRestart=afterFlag)");
        }

        // 19) 账号库存诊断：买到的道具重开后仍在
        host.Storage.Progress.Coins = 100;
        host.Game.StartPreset(DifficultyPreset.Beginner);
        if (host.Game.Session is { Inventory: { } shopInventory })
        {
            int sonarBefore = shopInventory.SonarCount;
            Core.Items.ShopPurchaseResult buyResult = host.Game.Buy(Core.Items.ItemIds.Sonar);
            int sonarAfterBuy = host.Game.Session!.Inventory!.SonarCount;

            host.Game.StartPreset(DifficultyPreset.Beginner);
            int sonarAfterRestart = host.Game.Session!.Inventory!.SonarCount;

            Console.WriteLine(
                $"库存诊断: before={sonarBefore} afterBuy={sonarAfterBuy} afterRestart={sonarAfterRestart} " +
                $"bought={buyResult.Bought} coinsAfter={buyResult.CoinsAfter} " +
                $"(期望 afterBuy=before+1, afterRestart=afterBuy)");
        }

        // 20) 关于作者独立窗口（1:1 对照 Windows 的 550×528 固定窗口）
        var authorsWindow = new Avalonia.Controls.Window
        {
            Title = "关于作者",
            Width = 550,
            Height = 528,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Services.UiPalette.Current.WindowBg),
            Content = new Views.Overlays.AboutAuthorsOverlay(host, onSecretLevelUnlocked: () => { }),
        };
        authorsWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(authorsWindow, Path.Combine(outputDirectory, "20-about-authors-window.png"));
        authorsWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 17) 更新检查诊断：验证只认 linux-v* 前缀的 release
        try
        {
            MineSweeper.Core.Updates.ReleaseInfo? update = host.Update
                .CheckAsync(manual: true)
                .GetAwaiter()
                .GetResult();
            Console.WriteLine($"更新检查诊断: release={update?.Version ?? "null"} tag={update?.TagName ?? "-"} error={host.Update.LastError ?? "none"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新检查诊断: 异常 {ex.GetType().Name}: {ex.Message}");
        }

        // 恢复默认外观，避免截图模式把用户存档改成深色
        host.Storage.Settings.UiTheme = "light";
        host.Storage.Settings.ThemeId = MineSweeper.Core.Themes.BoardPalette.ClassicId;
        host.Storage.Settings.Language = "zh-Hans";
        host.Storage.SaveSettings();

        Console.WriteLine($"数据目录: {Services.AppPaths.DataDirectory}");
    }

    private static void PumpFlood(Services.GameCoordinator game)
    {
        int guard = 0;
        while (game.Session?.HasPendingFlood == true && guard++ < 1000)
        {
            game.Tick();
        }
    }

    private static void Capture(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.WriteLine($"capture failed: {path}");
            return;
        }

        using FileStream stream = File.Create(path);
        frame.Save(stream);
        Console.WriteLine($"saved {path}");
    }
}
