using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MineSweeper.App.Linux.Services;
using MineSweeper.Core;
using MineSweeper.Core.Themes;

namespace MineSweeper.App.Linux.Views.Overlays;

/// <summary>
/// 设置面板（对应 Windows 端主窗口里的「设置」覆盖层）。
///
/// 内容按 v1.5 对齐：棋盘主题、界面深浅色、语言、音效开关/音量、
/// 无猜模式、首点扩展安全区、失焦暂停、检查更新。
/// </summary>
public sealed class SettingsOverlay : Border, IOverlay
{
    private readonly AppHost _host;
    private readonly TextBlock _updateStatus = new() { FontSize = 12, Foreground = new SolidColorBrush(UiPalette.Current.SubText), TextWrapping = TextWrapping.Wrap };

    public event EventHandler? CloseRequested;

    private readonly Action? _onWhatsNew;

    public SettingsOverlay(AppHost host, Action? onWhatsNew = null)
    {
        _host = host;
        _onWhatsNew = onWhatsNew;

        Background = new SolidColorBrush(UiPalette.Current.Mask);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("settings.title", "设置", "Settings"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(UiPalette.Current.Text),
        });

        // 棋盘主题
        stack.Children.Add(Row(LocalizationService.Get("settings.theme"),
            Combo(BoardPalette.BuiltIn.Select(p => LocalizationService.Translate(p.DisplayName)).ToArray(),
                Math.Max(0, BoardPalette.BuiltIn.ToList().FindIndex(p => p.Id == host.Storage.Settings.ThemeId)),
                index =>
                {
                    host.Storage.Settings.ThemeId = BoardPalette.BuiltIn[index].Id;
                    host.Storage.SaveSettings();
                    host.Theme.NotifyChanged();
                })));

        // 界面深浅色
        stack.Children.Add(Row(LocalizationService.T("settings.uiTheme", "界面颜色", "Interface theme"),
            Combo(
                [
                    LocalizationService.T("uiTheme.system", "跟随系统", "System"),
                    LocalizationService.T("uiTheme.light", "浅色", "Light"),
                    LocalizationService.T("uiTheme.dark", "深色", "Dark"),
                ],
                UiThemeCatalog.Normalize(host.Storage.Settings.UiTheme) switch
                {
                    UiThemeMode.Light => 1,
                    UiThemeMode.Dark => 2,
                    _ => 0,
                },
                index =>
                {
                    host.Theme.SetMode(index switch { 1 => "light", 2 => "dark", _ => "system" });
                })));

        // 语言
        stack.Children.Add(Row(LocalizationService.T("settings.language", "语言", "Language"),
            Combo(host.L10n.Languages.Select(l => l.NativeName).ToArray(),
                Math.Max(0, host.L10n.Languages.ToList().FindIndex(l => l.Code == host.L10n.CurrentCode)),
                index =>
                {
                    _host.Storage.Settings.Language = _host.L10n.Languages[index].Code;
                    _host.Storage.SaveSettings();
                    _host.L10n.SetLanguage(_host.L10n.Languages[index].Code);
                })));

        // 音效
        var audioToggle = new ToggleSwitch
        {
            IsChecked = host.Storage.Settings.AudioEnabled,
            OnContent = "",
            OffContent = "",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        audioToggle.IsCheckedChanged += (_, _) =>
        {
            host.Storage.Settings.AudioEnabled = audioToggle.IsChecked == true;
            host.ApplySettings();
        };
        stack.Children.Add(Row(LocalizationService.Get("settings.audio"), audioToggle));

        var volume = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = host.Storage.Settings.AudioVolume * 100,
            Width = 180,
        };
        volume.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            {
                host.Storage.Settings.AudioVolume = volume.Value / 100;
                host.ApplySettings();
            }
        };
        stack.Children.Add(Row(LocalizationService.Get("settings.volume"), volume));

        // 无猜模式
        var noGuess = new ToggleSwitch
        {
            IsChecked = host.Storage.Settings.NoGuess,
            OnContent = "",
            OffContent = "",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        noGuess.IsCheckedChanged += (_, _) =>
        {
            host.Storage.Settings.NoGuess = noGuess.IsChecked == true;
            host.Storage.SaveSettings();
        };
        stack.Children.Add(Row(LocalizationService.Get("settings.toggleNoGuess"), noGuess));

        // 首点扩展安全区
        var firstClick = new ToggleSwitch
        {
            IsChecked = host.Storage.Settings.FirstClickOpening,
            OnContent = "",
            OffContent = "",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        firstClick.IsCheckedChanged += (_, _) =>
        {
            host.Storage.Settings.FirstClickOpening = firstClick.IsChecked == true;
            host.Storage.SaveSettings();
        };
        stack.Children.Add(Row(LocalizationService.Get("settings.toggleExtSafe"), firstClick));

        // 失焦暂停
        var pause = new ToggleSwitch
        {
            IsChecked = host.Storage.Settings.PauseTimerOnFocusLoss,
            OnContent = "",
            OffContent = "",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        pause.IsCheckedChanged += (_, _) =>
        {
            host.Storage.Settings.PauseTimerOnFocusLoss = pause.IsChecked == true;
            host.Storage.SaveSettings();
        };
        stack.Children.Add(Row(LocalizationService.Get("settings.togglePauseTimer"), pause));

        // 检查更新（Linux 版只提示去 GitHub 下载 .deb）
        var checkButton = new Button
        {
            Content = LocalizationService.T("settings.checkUpdate", "检查更新", "Check for updates"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        checkButton.Click += async (_, _) =>
        {
            _updateStatus.Text = "检查中…";
            var release = await _host.Update.CheckAsync(manual: true);
            _updateStatus.Text = release is not null
                ? $"发现新版本 {release.Version}：{release.HtmlUrl}"
                : _host.Update.LastError is null
                    ? "已是最新版本"
                    : $"检查失败：{_host.Update.LastError}";
        };
        stack.Children.Add(checkButton);
        stack.Children.Add(_updateStatus);

        // 本版新增
        if (_onWhatsNew is not null)
        {
            var whatsNew = new Button
            {
                Content = LocalizationService.Get("btn.whatsNew"),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            whatsNew.Click += (_, _) =>
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                _onWhatsNew();
            };
            stack.Children.Add(whatsNew);
        }

        // 关闭
        var close = new Button
        {
            Content = LocalizationService.Get("common.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(close);

        Child = new Border
        {
            Background = new SolidColorBrush(UiPalette.Current.CardBg),
            BorderBrush = new SolidColorBrush(UiPalette.Current.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 520,
            MaxHeight = 620,
            Child = new ScrollViewer { Content = stack },
        };
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static Control Row(string label, Control input) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        Children =
        {
            new TextBlock { Text = label, Foreground = new SolidColorBrush(UiPalette.Current.Text), VerticalAlignment = VerticalAlignment.Center },
            Place(input, 1),
        },
    };

    private static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static ComboBox Combo(string[] items, int selected, Action<int> onChanged)
    {
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = selected, Width = 200 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChanged(combo.SelectedIndex);
            }
        };
        return combo;
    }
}
