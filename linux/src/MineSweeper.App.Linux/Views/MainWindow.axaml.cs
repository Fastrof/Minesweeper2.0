using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MineSweeper.App.Linux.Controls;
using MineSweeper.App.Linux.Services;
using MineSweeper.App.Linux.Views.Overlays;
using MineSweeper.Core;
using MineSweeper.Core.Levels;
using MineSweeper.Core.Notifications;
using MineSweeper.Core.Themes;

namespace MineSweeper.App.Linux.Views;

/// <summary>
/// 主窗口（按 Windows v1.5 的布局重做）：
///   Row 0 状态栏：剩余雷数卡片 / 状态文字 / 笑脸 / 用时卡片 / 物品栏（3 格）/ 提示按钮
///   Row 1 棋盘区：棋盘 + 右上角商店浮层 + 右侧放大镜信息面板
///   Row 2 操作区：关卡目标条 + 难度行 + 功能行 + 内联设置面板
///   Row 3 关卡导航：上一关 / 下一关（关卡局才显示）
///
/// 业务逻辑全部在 <see cref="GameCoordinator"/>，这里只做显示与输入转发。
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly GameCoordinator _game;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _toastTimer;
    private int _lastElapsed = -1;
    private bool _settingsReady;

    public MainWindow()
        : this(AppHost.Current ?? AppHost.Create())
    {
    }

    public MainWindow(AppHost host)
    {
        _host = host;
        _game = host.Game;

        InitializeComponent();

        // 窗口图标：任务栏 / Alt+Tab / 标题栏都用它（素材与 .deb 的 hicolor 图标同一张）
        Icon = CreateAppIcon();

        Board.Game = _game;
        Board.StatusMessage += (_, text) => ShowToast(string.Empty, text);
        Board.AimingChanged += (_, _) => UpdateItemInfoPanel();
        Board.SonarScanned += (_, result) => OnSonarScanned(result);

        _game.NewSession += (_, _) => OnNewSession();
        _game.StateChanged += (_, _) => UpdateStatus();
        _game.NotificationRequested += (_, notification) => OnNotification(notification);
        _game.SoundRequested += (_, effect) => _host.Sound.Play(effect);
        _game.GameFinished += (_, session) => OnGameFinished(session);
        _game.LevelFinished += (_, outcome) => ShowSettlement(outcome);

        _host.Theme.Changed += (_, _) => ApplyTheme();
        _host.L10n.LanguageChanged += (_, _) => ApplyLanguage();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBar.IsVisible = false;
        };

        InitSettingsControls();
        ApplyTheme();
        ApplyLanguage();
        _game.StartPreset(DifficultyPreset.Beginner);

        Opened += (_, _) =>
        {
            ApplyTheme();
            Board.FitToWindow();
            FitWindowToBoard();
        };
        // 窗口尺寸变化（切难度 / 切关卡局都会改窗口大小）后重新适配棋盘，
        // 否则棋盘会按"改尺寸之前"的 Bounds 算格宽——这就是初/中/高级盘显示不对的根因。
        SizeChanged += (_, _) =>
        {
            if (!Board.IsUserAdjusted)
            {
                Board.FitToWindow();
            }
        };

        Closing += (_, _) => _host.Shutdown();
        Activated += (_, _) => _game.SetWindowFocused(true);
        Deactivated += (_, _) => _game.SetWindowFocused(false);
    }

    public GameSession? Session => _game.Session;
    public int BoardDrawCalls => Board.LastRenderDrawCalls;
    public int BoardCells => Board.LastRenderCells;
    public double BoardRenderMs => Board.LastRenderMs;

    // ---------------------------------------------------------------- 状态刷新

    private void OnNewSession()
    {
        HideOverlay();
        Board.CancelAiming();
        Board.ClearHint();   // 新的一局不该还留着上一局的提示绿框
        Board.FitToWindow();
        UpdateStatus();
        FitWindowToBoard();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_game.Tick())
        {
            Board.InvalidateVisual();
        }

        GameSession? session = _game.Session;
        if (session is not null && session.ElapsedSecondsInt != _lastElapsed)
        {
            _lastElapsed = session.ElapsedSecondsInt;
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        GameSession? session = _game.Session;
        if (session is null)
        {
            return;
        }

        RemainingText.Text = session.RemainingMines.ToString("000", CultureInfo.InvariantCulture);
        TotalMinesText.Text = session.Board.MineCount.ToString(CultureInfo.InvariantCulture);
        TimerText.Text = session.ElapsedSecondsInt.ToString("000", CultureInfo.InvariantCulture);
        // v1.5.1 Core：GameSession.Coins 就是账号余额（PlayerProgress.Coins），跨局累计
        CoinText.Text = "🪙 " + _game.AccountCoins.ToString(CultureInfo.InvariantCulture);
        SeedText.Text = "种子 " + session.Seed.ToString(CultureInfo.InvariantCulture);

        Smiley.State = session.Status switch
        {
            GameStatus.Won => SmileyState.Won,
            GameStatus.Lost => SmileyState.Lost,
            _ => SmileyState.Normal,
        };

        StatusText.Text = session.Status switch
        {
            GameStatus.NotStarted => LocalizationService.Get("status.ready"),
            GameStatus.InProgress => LocalizationService.T("status.playing", "进行中", "Playing"),
            GameStatus.Won => LocalizationService.T("status.won", "胜利", "Won"),
            GameStatus.Lost => LocalizationService.T("status.lost", "踩雷", "Hit a mine"),
            _ => string.Empty,
        };

        int magnifier = session.Inventory?.MagnifierCount ?? 0;
        int sonar = session.Inventory?.SonarCount ?? 0;
        int freeze = session.Inventory?.FreezeCount ?? 0;
        MagnifierCountText.Text = magnifier.ToString(CultureInfo.InvariantCulture);
        SonarCountText.Text = sonar.ToString(CultureInfo.InvariantCulture);
        FreezeCountText.Text = freeze.ToString(CultureInfo.InvariantCulture);

        bool itemsUsable = session.Settings.ItemsAvailable && !session.IsFinished;
        MagnifierSlot.Opacity = itemsUsable && magnifier > 0 ? 1 : 0.45;
        SonarSlot.Opacity = itemsUsable && sonar > 0 ? 1 : 0.45;
        FreezeSlot.Opacity = itemsUsable && freeze > 0 ? 1 : 0.45;

        bool hintMode = !_game.LevelSession
                        && !session.Settings.NoGuess
                        && !session.IsFinished;
        HintButton.IsVisible = hintMode;
        // 与 Windows v1.5.2 对齐：还没翻开第一格时提示按钮置灰（此时还没有可推的安全格）
        HintButton.IsEnabled = hintMode && session.Board.MinesPlaced;

        LevelDefinition? level = _game.Level;
        LevelGoalBar.IsVisible = level is not null;
        LevelNavRow.IsVisible = level is not null;

        if (level is not null)
        {
            LevelGoalText.Text = $"{level.DisplayName} · {level.BoardText} · {level.GoalText}";
            LevelDefinition? prev = _game.PreviousLevel;
            LevelDefinition? next = _game.NextLevel;
            PrevLevelButton.IsEnabled = prev is not null;
            NextLevelButton.IsEnabled = next is not null && LevelRules.IsUnlocked(_host.Storage.LevelProgress, next);
            LevelNavText.Text = $"{level.DisplayId} · 当前可拿 {LevelRules.EvaluateStars(level, session.ElapsedSeconds)} 星";
        }

        UpdateItemInfoPanel();
    }

    private void UpdateItemInfoPanel()
    {
        GameSession? session = _game.Session;
        bool aiming = Board.Aiming != AimingMode.None;
        bool wasVisible = ItemInfoPanel.IsVisible;
        ItemInfoPanel.IsVisible = aiming && session is not null;

        // 右侧信息面板一展开，棋盘区的宽度就少 280 DIP；
        // 不重新适配的话棋盘会超出可用区域（"雷盘显示不完全"的根因）。
        if (wasVisible != ItemInfoPanel.IsVisible)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!Board.IsUserAdjusted)
                {
                    Board.FitToWindow();
                }
            }, DispatcherPriority.Loaded);
        }

        if (!aiming || session is null)
        {
            return;
        }

        ItemInfoHeading.Text = Board.Aiming == AimingMode.Magnifier
            ? LocalizationService.Get("item.magnifier")
            : LocalizationService.Get("item.sonar");

        int index = Board.AimingPreviewIndex;
        if (index < 0)
        {
            ItemInfoText.Text = LocalizationService.T("item.hover", "把鼠标移到棋盘上选择区域", "Move the mouse over the board");
            return;
        }

        if (Board.Aiming == AimingMode.Magnifier
            && _game.PreviewMagnifier(index) is { } reveal)
        {
            ItemInfoText.Text = reveal.ToPanelText(session.Board);
            return;
        }

        int col = index % session.Board.Width;
        int row = index / session.Board.Width;
        ItemInfoText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "({0}, {1}) · {2}×{3}",
            col + 1, row + 1,
            session.MagnifierWidth, session.MagnifierHeight);
    }

    // ---------------------------------------------------------------- 通知

    private void ShowToast(string title, string message)
    {
        ToastText.Text = string.IsNullOrEmpty(title) ? message : $"{title} · {message}";
        ToastBar.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnNotification(GameNotification notification)
    {
        if (!notification.ShowToast)
        {
            return;
        }

        ShowToast(notification.Title, notification.Message);
    }

    private void OnGameFinished(GameSession session)
    {
        UpdateStatus();
        if (session.Status == GameStatus.Lost)
        {
            ShowOverlay(new DefeatOverlay(
                _host,
                onRetrySameBoard: () =>
                {
                    HideOverlay();
                    TryStart(() => _game.RestartSameBoard());
                },
                onNewBoard: () =>
                {
                    HideOverlay();
                    TryStart(() => _game.Restart());
                },
                onExit: Close));
        }
    }

    private void OnSonarScanned(Core.SonarScanResult result)
    {
        GameSession? session = _game.Session;
        if (session is null)
        {
            return;
        }

        if (result.Success)
        {
            // 在棋盘上把扫过的区域标成浅蓝，保留到下一局，玩家随时能回看
            Board.MarkSonar(result.CenterIndex, result.Radius);
        }

        ShowOverlay(new SonarResultOverlay(session, result));
    }

    private void ShowSettlement(LevelOutcome outcome)
    {
        ShowOverlay(new SettlementOverlay(_host, outcome, onClose: HideOverlay));
    }

    // ---------------------------------------------------------------- 覆盖层

    private void ShowOverlay(Control overlay)
    {
        if (overlay is IOverlay closeable)
        {
            closeable.CloseRequested += (_, _) => HideOverlay();
        }

        OverlayHost.Content = overlay;
        OverlayHost.IsVisible = true;
    }

    private void HideOverlay()
    {
        OverlayHost.Content = null;
        OverlayHost.IsVisible = false;
    }

    // ---------------------------------------------------------------- 主题与语言

    /// <summary>
    /// 应用窗口图标。Linux 端此前只打了 .deb 的 hicolor 图标，窗口/任务栏是 Avalonia 默认图标；
    /// 这里用同一张 <c>Assets/app-icon.png</c> 补上，加载失败也不影响启动。
    /// </summary>
    private static WindowIcon? CreateAppIcon()
    {
        try
        {
            return new WindowIcon(
                Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://Minesweeper2/Assets/app-icon.png")));
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
        {
            AppPaths.Log($"[窗口] 应用图标加载失败：{ex.Message}");
            return null;
        }
    }

    private void ApplyTheme()
    {
        bool dark = _host.Theme.Mode == UiThemeMode.Dark;
        UiPalette p = UiPalette.Use(dark);

        Background = new SolidColorBrush(p.WindowBg);
        MinesCard.Background = new SolidColorBrush(p.CardBg);
        MinesCard.BorderBrush = new SolidColorBrush(p.CardBorder);
        TimerCard.Background = new SolidColorBrush(p.CardBg);
        TimerCard.BorderBrush = new SolidColorBrush(p.CardBorder);
        BoardFrame.Background = new SolidColorBrush(dark ? Color.Parse("#1A1A1A") : Color.Parse("#E2E2E2"));
        BoardFrame.BorderBrush = new SolidColorBrush(p.CardBorder);
        SettingsPanel.Background = new SolidColorBrush(p.CardBg);
        SettingsPanel.BorderBrush = new SolidColorBrush(p.CardBorder);
        LevelGoalBar.Background = new SolidColorBrush(p.CardBg);
        LevelGoalBar.BorderBrush = new SolidColorBrush(p.CardBorder);
        ItemInfoPanel.Background = new SolidColorBrush(p.CardBg);
        ItemInfoPanel.BorderBrush = new SolidColorBrush(p.CardBorder);
        ShopEntry.Background = new SolidColorBrush(Color.FromArgb(242, p.CardBg.R, p.CardBg.G, p.CardBg.B));
        ShopEntry.BorderBrush = new SolidColorBrush(p.CardBorder);

        foreach (Border slot in new[] { MagnifierSlot, SonarSlot, FreezeSlot })
        {
            slot.Background = new SolidColorBrush(p.CardBg);
            slot.BorderBrush = new SolidColorBrush(p.CardBorder);
        }

        RemainingText.Foreground = new SolidColorBrush(p.Text);
        TotalMinesText.Foreground = new SolidColorBrush(p.AccentSoft);
        TimerText.Foreground = new SolidColorBrush(p.Text);
        StatusText.Foreground = new SolidColorBrush(p.SubText);
        SeedText.Foreground = new SolidColorBrush(p.SubText);
        CoinText.Foreground = new SolidColorBrush(p.AccentSoft);
        LevelGoalText.Foreground = new SolidColorBrush(p.Text);
        LevelNavText.Foreground = new SolidColorBrush(p.SubText);
        ItemInfoHeading.Foreground = new SolidColorBrush(p.Text);
        ItemInfoText.Foreground = new SolidColorBrush(p.Text);
        UpdateVersionText.Foreground = new SolidColorBrush(p.SubText);
        UpdateStatusText.Foreground = new SolidColorBrush(p.SubText);
        ToastBar.Background = new SolidColorBrush(Color.FromArgb(242, p.CardBg.R, p.CardBg.G, p.CardBg.B));
        ToastBar.BorderBrush = new SolidColorBrush(p.CardBorder);
        ToastText.Foreground = new SolidColorBrush(p.Text);

        if (Application.Current is App app)
        {
            app.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        }

        Board.BoardTheme = _host.ResolveBoardTheme(dark);
    }

    private void ApplyLanguage()
    {
        Title = LocalizationService.Get("app.title");
        LevelsButton.Content = LocalizationService.Get("btn.levels");
        HintButton.Content = LocalizationService.Get("hint.button");
        AchievementsButton.Content = LocalizationService.Get("btn.achievements");
        HistoryButton.Content = LocalizationService.Get("btn.history");
        AboutGameButton.Content = LocalizationService.Get("btn.aboutGame");
        AboutAuthorsButton.Content = LocalizationService.Get("btn.aboutAuthors");
        BeginnerButton.Content = LocalizationService.Get("btn.beginner");
        IntermediateButton.Content = LocalizationService.Get("btn.intermediate");
        ExpertButton.Content = LocalizationService.Get("btn.expert");
        WhatsNewButton.Content = LocalizationService.Get("btn.whatsNew");
        ToolTip.SetTip(HintButton, LocalizationService.Get("hint.tooltip"));
    }

    // ---------------------------------------------------------------- 设置面板

    private void InitSettingsControls()
    {
        _settingsReady = false;

        UiThemeBox.ItemsSource = new[]
        {
            LocalizationService.T("uiTheme.system", "跟随系统", "System"),
            LocalizationService.T("uiTheme.light", "浅色", "Light"),
            LocalizationService.T("uiTheme.dark", "深色", "Dark"),
        };
        UiThemeBox.SelectedIndex = UiThemeCatalog.Normalize(_host.Storage.Settings.UiTheme) switch
        {
            UiThemeMode.Light => 1,
            UiThemeMode.Dark => 2,
            _ => 0,
        };
        UiThemeBox.SelectionChanged += (_, _) =>
        {
            if (!_settingsReady || UiThemeBox.SelectedIndex < 0)
            {
                return;
            }

            _host.Theme.SetMode(UiThemeBox.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" });
        };

        LanguageBox.ItemsSource = _host.L10n.Languages.Select(l => l.NativeName).ToArray();
        LanguageBox.SelectedIndex = Math.Max(0, _host.L10n.Languages.ToList().FindIndex(l => l.Code == _host.L10n.CurrentCode));
        LanguageBox.SelectionChanged += (_, _) =>
        {
            if (!_settingsReady || LanguageBox.SelectedIndex < 0)
            {
                return;
            }

            _host.Storage.Settings.Language = _host.L10n.Languages[LanguageBox.SelectedIndex].Code;
            _host.Storage.SaveSettings();
            _host.L10n.SetLanguage(_host.Storage.Settings.Language);
        };

        ThemeCombo.ItemsSource = BoardPalette.BuiltIn.Select(b => LocalizationService.Translate(b.DisplayName)).ToArray();
        ThemeCombo.SelectedIndex = Math.Max(0, BoardPalette.BuiltIn.ToList().FindIndex(b => b.Id == _host.Storage.Settings.ThemeId));
        ThemeCombo.SelectionChanged += (_, _) =>
        {
            if (!_settingsReady || ThemeCombo.SelectedIndex < 0)
            {
                return;
            }

            _host.Storage.Settings.ThemeId = BoardPalette.BuiltIn[ThemeCombo.SelectedIndex].Id;
            _host.Storage.SaveSettings();
            ApplyTheme();
        };

        FirstClickOpeningSwitch.IsChecked = _host.Storage.Settings.FirstClickOpening;
        FirstClickOpeningSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_settingsReady)
            {
                return;
            }

            _host.Storage.Settings.FirstClickOpening = FirstClickOpeningSwitch.IsChecked == true;
            _host.Storage.SaveSettings();
        };

        PauseTimerSwitch.IsChecked = _host.Storage.Settings.PauseTimerOnFocusLoss;
        PauseTimerSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_settingsReady)
            {
                return;
            }

            _host.Storage.Settings.PauseTimerOnFocusLoss = PauseTimerSwitch.IsChecked == true;
            _host.Storage.SaveSettings();
        };

        NoGuessSwitch.IsChecked = _host.Storage.Settings.NoGuess;
        NoGuessSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_settingsReady)
            {
                return;
            }

            _host.Storage.Settings.NoGuess = NoGuessSwitch.IsChecked == true;
            _host.Storage.SaveSettings();
        };

        AudioToggle.IsChecked = _host.Storage.Settings.AudioEnabled;
        AudioToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_settingsReady)
            {
                return;
            }

            _host.Storage.Settings.AudioEnabled = AudioToggle.IsChecked == true;
            _host.ApplySettings();
        };

        VolumeSlider.Value = _host.Storage.Settings.AudioVolume * 100;
        VolumeSlider.PropertyChanged += (_, args) =>
        {
            if (!_settingsReady || args.Property != Slider.ValueProperty)
            {
                return;
            }

            _host.Storage.Settings.AudioVolume = VolumeSlider.Value / 100;
            _host.ApplySettings();
        };

        UpdateVersionText.Text = "v" + UpdateService.DisplayVersion;
        _settingsReady = true;
    }

    private void OnSettingsToggleClick(object? sender, RoutedEventArgs e)
    {
        SettingsPanel.IsVisible = SettingsToggle.IsChecked == true;
    }

    private void OnPreviewSoundClick(object? sender, RoutedEventArgs e) =>
        _host.Sound.Play(MineSweeper.Core.Audio.SoundEffect.Reveal);

    private void OnCopySeedClick(object? sender, RoutedEventArgs e)
    {
        GameSession? session = _game.Session;
        if (session is null)
        {
            return;
        }

        string text = session.Seed.ToString(CultureInfo.InvariantCulture);
        try
        {
            // Avalonia 12 的 IClipboard 方法签名可能随版本变化，用反射调用避免绑死
            object? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            clipboard?.GetType()
                .GetMethod("SetTextAsync", new[] { typeof(string) })
                ?.Invoke(clipboard, new object?[] { text });
        }
        catch (Exception ex)
        {
            AppPaths.Log($"[剪贴板] 复制种子失败：{ex.Message}");
        }

        ShowToast(string.Empty, string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.T("seed.copied", "已复制种子 {0}", "Seed {0} copied"),
            text));
    }

    private void OnCustomStartClick(object? sender, RoutedEventArgs e)
    {
        int width = (int)(WidthBox.Value ?? 9);
        int height = (int)(HeightBox.Value ?? 9);
        int mines = (int)(MinesBox.Value ?? 10);
        TryStart(() => _game.StartCustom(width, height, mines, null));
    }

    private void OnReplaySeedClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SeedBox.Text, out int seed))
        {
            ShowToast(string.Empty, LocalizationService.T("seed.invalid", "种子必须是整数", "Seed must be an integer"));
            return;
        }

        GameSession? current = _game.Session;
        int width = current?.Board.Width ?? 9;
        int height = current?.Board.Height ?? 9;
        int mines = current?.Board.MineCount ?? 10;
        TryStart(() => _game.StartCustom(width, height, mines, seed));
    }

    private void OnOpenShareCodeClick(object? sender, RoutedEventArgs e)
    {
        string code = ShareCodeBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        GameSession? session = _game.StartFromShareCode(code, out string? error);
        if (session is null)
        {
            ShowToast(string.Empty, error ?? "分享串无效");
            return;
        }

        Board.FitToWindow();
        UpdateStatus();
    }

    private void TryStart(Func<GameSession> start)
    {
        try
        {
            start();
            Board.FitToWindow();
            UpdateStatus();
        }
        catch (InvalidOperationException ex)
        {
            ShowToast(string.Empty, ex.Message);
        }
    }

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = LocalizationService.T("update.checking", "检查中…", "Checking…");
        MineSweeper.Core.Updates.ReleaseInfo? release = await _host.Update.CheckAsync(manual: true);
        UpdateStatusText.Text = release is not null
            ? string.Format(CultureInfo.InvariantCulture, "发现 {0}", release.Version)
            : _host.Update.LastError switch
            {
                null => LocalizationService.T("update.latest", "已是最新", "Up to date"),
                "暂无 Linux 版发布" => LocalizationService.T("update.noLinuxRelease", "暂无 Linux 版发布", "No Linux release yet"),
                _ => LocalizationService.T("update.failed", "检查失败", "Check failed"),
            };

        if (release is not null)
        {
            ShowOverlay(new WhatsNewOverlay(_host, release.Version));
        }
    }

    private void OnWhatsNewClick(object? sender, RoutedEventArgs e) =>
        ShowOverlay(new WhatsNewOverlay(_host));

    // ---------------------------------------------------------------- 窗口尺寸

    /// <summary>按棋盘尺寸调整窗口（对应 Windows 端 FitWindowToBoard：26 DIP/格 + 装饰）。</summary>
    private void FitWindowToBoard()
    {
        GameSession? session = _game.Session;
        if (session is null || WindowState != WindowState.Normal)
        {
            return;
        }

        const double CellSize = 26;
        const double ChromeWidth = 190;

        // 关卡局多两条（目标条 + 上一关/下一关），高度预算要跟着加，
        // 否则底部控件会把棋盘挤小（Windows 端踩过的同一个坑）。
        const double ChromeHeight = 420;
        double chromeHeight = _game.LevelSession ? ChromeHeight + 90 : ChromeHeight;

        double targetWidth = (session.Board.Width * CellSize) + ChromeWidth;
        double targetHeight = (session.Board.Height * CellSize) + chromeHeight;

        Screen? screen = Screens.Primary;
        if (screen is not null)
        {
            double maxWidth = screen.WorkingArea.Width / screen.Scaling * 0.94;
            double maxHeight = screen.WorkingArea.Height / screen.Scaling * 0.94;
            targetWidth = Math.Clamp(targetWidth, 700, maxWidth);
            targetHeight = Math.Clamp(targetHeight, 540, maxHeight);
        }

        // 同难度重开时尺寸几乎不变，不做多余改动（否则点笑脸会看到窗口"跳一下"）
        if (Math.Abs(Width - targetWidth) < 8 && Math.Abs(Height - targetHeight) < 8)
        {
            return;
        }

        Width = targetWidth;
        Height = targetHeight;
    }

    // ---------------------------------------------------------------- 按钮

    private void OnSmileyClick(object? sender, RoutedEventArgs e) => TryStart(() => _game.Restart());

    private void OnBeginnerClick(object? sender, RoutedEventArgs e) => StartPreset(DifficultyPreset.Beginner);

    private void OnIntermediateClick(object? sender, RoutedEventArgs e) => StartPreset(DifficultyPreset.Intermediate);

    private void OnExpertClick(object? sender, RoutedEventArgs e) => StartPreset(DifficultyPreset.Expert);

    private void StartPreset(DifficultyPreset preset) => TryStart(() => _game.StartPreset(preset));

    private void OnLevelsClick(object? sender, RoutedEventArgs e) =>
        ShowOverlay(new LevelOverlay(_host, level =>
        {
            HideOverlay();
            TryStart(() => _game.StartLevel(level));
        }));

    private void OnHistoryClick(object? sender, RoutedEventArgs e) => ShowOverlay(new RecordsOverlay(_host));

    private void OnAchievementsClick(object? sender, RoutedEventArgs e) => ShowOverlay(new AchievementsOverlay(_host));

    private void OnShopClick(object? sender, RoutedEventArgs e) => ShowOverlay(new ShopOverlay(_host));

    private void OnAboutGameClick(object? sender, RoutedEventArgs e) =>
        ShowAboutWindow(new AboutGameOverlay(_host), "关于游戏", 550, 528);

    private void OnAboutAuthorsClick(object? sender, RoutedEventArgs e) =>
        ShowAboutWindow(new AboutAuthorsOverlay(_host, UnlockSecretLevel), "关于作者", 550, 528);

    /// <summary>
    /// 打开一个**独立、不可缩放**的关于窗口（对齐 Windows 端的两个 About 窗口）。
    /// 内容仍是 Overlay 控件，只是宿主换成 Window；彩蛋逻辑不变。
    /// </summary>
    private void ShowAboutWindow(Control content, string title, double width, double height)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            CanResize = false,
            CanMaximize = false,
            CanMinimize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(UiPalette.Current.WindowBg),
            Icon = CreateAppIcon(),
            Content = content,
        };

        if (content is IOverlay overlay)
        {
            overlay.CloseRequested += (_, _) => window.Close();
        }

        window.Show(this);
    }

    private void OnHintClick(object? sender, RoutedEventArgs e)
    {
        GameSession? hintSession = _game.Session;
        if (hintSession is null || !hintSession.Board.MinesPlaced)
        {
            ShowToast(
                LocalizationService.Get("hint.beforeFirst.title"),
                LocalizationService.Get("hint.beforeFirst.body"));
            return;
        }

        int index = _game.FindHint();
        if (index < 0)
        {
            ShowToast(
                LocalizationService.Get("hint.none.title"),
                LocalizationService.Get("hint.none.body"));
            return;
        }

        _game.MarkHintUsed();
        Board.ShowHint(index);
    }

    private void OnPrevLevelClick(object? sender, RoutedEventArgs e)
    {
        LevelDefinition? prev = _game.PreviousLevel;
        if (prev is not null)
        {
            TryStart(() => _game.StartLevel(prev));
        }
    }

    private void OnNextLevelClick(object? sender, RoutedEventArgs e)
    {
        LevelDefinition? next = _game.NextLevel;
        if (next is not null && LevelRules.IsUnlocked(_host.Storage.LevelProgress, next))
        {
            TryStart(() => _game.StartLevel(next));
        }
    }

    // ---------------------------------------------------------------- 道具槽

    private void OnMagnifierSlotPressed(object? sender, PointerPressedEventArgs e) =>
        ActivateItemSlot(sender, e, AimingMode.Magnifier);

    private void OnSonarSlotPressed(object? sender, PointerPressedEventArgs e) =>
        ActivateItemSlot(sender, e, AimingMode.Sonar);

    private void OnFreezeSlotPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        TimeFreezeResult result = _game.Freeze();
        if (result.Started)
        {
            ShowToast(LocalizationService.Get("item.freeze.title"), LocalizationService.Get("item.freeze.body"));
        }
        else
        {
            ShowToast(string.Empty, LocalizationService.T("item.freezeRejected", "现在不能使用时间冻结", "Time freeze is not available now"));
        }
    }

    private void ActivateItemSlot(object? sender, PointerPressedEventArgs e, AimingMode mode)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed)
        {
            return;
        }

        e.Handled = true;
        GameSession? session = _game.Session;
        if (session is null || !session.Settings.ItemsAvailable || session.IsFinished)
        {
            return;
        }

        int count = mode == AimingMode.Magnifier
            ? session.Inventory?.MagnifierCount ?? 0
            : session.Inventory?.SonarCount ?? 0;
        if (count <= 0)
        {
            return;
        }

        Board.BeginAiming(mode);
        ShowToast(
            mode == AimingMode.Magnifier ? LocalizationService.Get("item.magnifier") : LocalizationService.Get("item.sonar"),
            LocalizationService.T("item.aiming", "点击网格使用；右键取消", "Click the board to use; right-click to cancel"));
    }

    // ---------------------------------------------------------------- 隐藏关（彩蛋）

    /// <summary>彩蛋解锁隐藏关「EX-1 卡布奇诺」：只改内存标记，不落盘（关掉游戏就消失）。</summary>
    private void UnlockSecretLevel()
    {
        _host.Storage.LevelProgress.SecretUnlocked = true;
        AppPaths.Log("[彩蛋] 已解锁隐藏关卡 EX-1（不落盘）");
        ShowToast(string.Empty, LocalizationService.T("egg.secretUnlocked", "隐藏关卡已解锁", "Secret level unlocked"));
    }

    // ---------------------------------------------------------------- 截图辅助

    public void RefreshBoard()
    {
        Board.InvalidateVisual();
        UpdateStatus();
    }

    public void FitBoard() => Board.FitToWindow();

    public void ShowSession(GameSession session) => Board.Game = _game;

    public void SetPreset(DifficultyPreset preset) => StartPreset(preset);

    public void OpenSettingsForScreenshot()
    {
        SettingsToggle.IsChecked = true;
        SettingsPanel.IsVisible = true;
    }

    public void OpenLevelsForScreenshot() =>
        ShowOverlay(new LevelOverlay(_host, level => { HideOverlay(); TryStart(() => _game.StartLevel(level)); }));

    public void OpenShopForScreenshot() => ShowOverlay(new ShopOverlay(_host));

    public void OpenAchievementsForScreenshot() => ShowOverlay(new AchievementsOverlay(_host));

    public void OpenRecordsForScreenshot() => ShowOverlay(new RecordsOverlay(_host));

    public void OpenAboutGameForScreenshot() => ShowOverlay(new AboutGameOverlay(_host));

    public void OpenAboutAuthorsForScreenshot() => ShowOverlay(new AboutAuthorsOverlay(_host, UnlockSecretLevel));

    public void OpenWhatsNewForScreenshot(string version) => ShowOverlay(new WhatsNewOverlay(_host, version));

    public void ShowDefeatForScreenshot() => ShowOverlay(new DefeatOverlay(
        _host,
        onRetrySameBoard: () => { HideOverlay(); TryStart(() => _game.RestartSameBoard()); },
        onNewBoard: () => { HideOverlay(); TryStart(() => _game.Restart()); },
        onExit: Close));

    public void ShowSonarForScreenshot(Core.SonarScanResult result) => OnSonarScanned(result);

    public void OpenAboutWithEggsForScreenshot()
    {
        var about = new AboutGameOverlay(_host);
        about.TriggerEggForScreenshot();
        ShowOverlay(about);
    }

    public void OpenAuthorsWithEggsForScreenshot()
    {
        var about = new AboutAuthorsOverlay(_host, UnlockSecretLevel);
        about.TriggerEggsForScreenshot();
        ShowOverlay(about);
    }

    public void CloseOverlayForScreenshot()
    {
        HideOverlay();
        SettingsToggle.IsChecked = false;
        SettingsPanel.IsVisible = false;
    }

    public void BeginAimingForScreenshot(AimingMode mode) => Board.BeginAiming(mode);

    /// <summary>截图/自检：触发一次提示高亮（验证渲染期间不会因 InvalidateVisual 崩溃）。</summary>
    public void ShowHintForScreenshot(int index) => Board.ShowHint(index);

    public string BoardAccessibleName => Board.BuildAccessibleName();
}

/// <summary>可关闭的覆盖层约定。</summary>
public interface IOverlay
{
    event EventHandler? CloseRequested;
}
