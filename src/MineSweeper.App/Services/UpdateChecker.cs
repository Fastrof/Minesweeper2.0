using MineSweeper.Core.Updates;

namespace MineSweeper.App.Services;

/// <summary>
/// 检查更新（v1.1）。
///
/// 四条设计取舍，写在这里免得以后被"顺手优化"掉：
/// <list type="bullet">
/// <item><b>只请求 GitHub 的公开接口</b>：不带任何标识、不带查询参数、不读也不发任何玩家数据。
/// 请求头里的 User-Agent 只含产品名与版本号 —— GitHub 强制要求 UA，缺了会直接 403，
/// 所以这不是"多此一举"，而是能不能拿到结果的必要条件。</item>
/// <item><b>商店版完全不启用</b>：打包版由 Microsoft Store 负责更新。
/// 既没必要自己查，更不该把商店用户引导到 GitHub 去侧载（那会绕过商店的签名与更新链路）。</item>
/// <item><b>失败一律静默</b>：断网、超时、限流、JSON 异常都只写 app.log，
/// 绝不弹窗、绝不阻塞游戏 —— 这是本工程最反复强调的一条（平台可能失败的地方都要兜住）。</item>
/// <item><b>限流与"提示过就不再提示"的策略在 Core</b>（<c>UpdateCheckPolicy</c>）：纯函数、有单测，
/// App 层只负责发请求和把结果翻译成 UI。</item>
/// </list>
/// </summary>
internal sealed class UpdateChecker
{
    /// <summary>GitHub 的"最新正式发布"接口。用 latest 而不是列表接口，省流量也不会拿到草稿/预发布。</summary>
    public const string ReleasesApiUrl = "https://api.github.com/repos/Fastrof/MineSweeper2.0/releases/latest";

    /// <summary>发布页（手动检查时给玩家一个可点的入口）。</summary>
    public const string ReleasesPageUrl = "https://github.com/Fastrof/MineSweeper2.0/releases";

    /// <summary>超时压到 12 秒：启动时静默检查不该拖着窗口不放。</summary>
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };

    private readonly UpdateStateStore _store;
    private readonly Func<string> _currentVersion;
    private bool _busy;

    public UpdateChecker(UpdateStateStore store, UpdateState state, Func<string> currentVersion)
    {
        _store = store;
        State = state;
        _currentVersion = currentVersion;
    }

    /// <summary>跨次运行的检查状态（上次检查时间 / 已提示版本 / 已跳过版本）。</summary>
    public UpdateState State { get; }

    /// <summary>是否启用。商店版与"玩家关掉了自动检查"都会把它置为 false。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>当前版本号（来自 <c>AboutInfo.Version</c>，单一来源）。</summary>
    public string CurrentVersion => _currentVersion();

    /// <summary>最近一次检查的结论，供设置面板显示。</summary>
        private string? _lastMessage;

    /// <summary>
    /// 最近一次检查的说明。默认值<b>读取时</b>才查语言表——
    /// 这样不受"对象构造早于设置语言"的时机影响（曾经因此显示成中文）。
    /// </summary>
    public string LastMessage
    {
        get => _lastMessage ?? MineSweeper.Core.Localization.Strings.Get("update.neverChecked");
        private set => _lastMessage = value;
    }

    /// <summary>最近一次发现的新版本（没有就是 null）。</summary>
    public ReleaseInfo? LatestRelease { get; private set; }

    /// <summary>检查完成后触发（在后台线程，调用方自己切回 UI 线程）。</summary>
    public event EventHandler<UpdateCheckResult>? CheckCompleted;

    /// <summary>
    /// 是否该发起一次检查。<paramref name="trigger"/> 为 <see cref="UpdateCheckTrigger.Manual"/> 时永远 true
    /// （玩家点了按钮就必须真去查一次，不能被限流吃掉）。
    /// </summary>
    public bool TryStartCheck(UpdateCheckTrigger trigger, out string reason)
    {
        if (!IsEnabled)
        {
            reason = "检查更新已关闭。";
            return false;
        }

        if (_busy)
        {
            reason = "上一次检查还没结束。";
            return false;
        }

        return UpdateCheckPolicy.ShouldCheck(State, trigger, DateTimeOffset.UtcNow, out reason);
    }

    /// <summary>真正发一次请求。调用方负责把返回值切回 UI 线程。</summary>
    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckTrigger trigger)
    {
        _busy = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
            // GitHub 要求必须带 User-Agent；这里只暴露产品名与版本，不含任何可识别玩家的信息。
            request.Headers.TryAddWithoutValidation("User-Agent", $"MineSweeper2/{CurrentVersion}");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false);

            // 无论成败都记下这次尝试的时间：否则一台长期离线的机器会每次启动都重试一遍。
            State.LastCheckUtc = DateTimeOffset.UtcNow;

            if (!response.IsSuccessStatusCode)
            {
                LastMessage = MineSweeper.Core.Localization.Strings.Get("update.httpFailed", (int)response.StatusCode);
                AppPaths.Log($"[UPDATE] {LastMessage}");
                SaveState();
                return Finish(false, false, null, LastMessage);
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!ReleaseJson.TryParse(json, out ReleaseInfo? release, out string? error) || release is null)
            {
                LastMessage = $"检查更新失败：{error ?? "返回内容无法解析"}（不影响游戏）";
                AppPaths.Log($"[UPDATE] {LastMessage}");
                SaveState();
                return Finish(false, false, null, LastMessage);
            }

            LatestRelease = release;

            bool notify = UpdateCheckPolicy.ShouldNotify(State, CurrentVersion, release, out string why);
            if (AppVersion.IsNewer(release.Version, CurrentVersion))
            {
                State.LatestKnownVersion = release.Version;
            }

            SaveState();

            LastMessage = notify
                ? $"发现新版本 {release.Version}（当前 {AppVersion.Normalize(CurrentVersion)}）"
                : why;
            AppPaths.Log($"[UPDATE] 检查完成：最新 {release.Version}，当前 {CurrentVersion}，{(notify ? "需要提示" : "不提示")} —— {why}");

            return Finish(true, notify, release, LastMessage);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
        {
            // 网络类失败：只写日志。注意这里**不**再重试 —— 检查更新失败对玩家毫无影响，
            // 反复重试只会让离线环境里的启动变慢。
            State.LastCheckUtc = DateTimeOffset.UtcNow;
            SaveState();
            LastMessage = $"检查更新失败：{ex.GetType().Name}（不影响游戏）";
            AppPaths.Log($"[UPDATE] {LastMessage}：{ex.Message}");
            return Finish(false, false, null, LastMessage);
        }
        finally
        {
            _busy = false;
        }
    }

    private UpdateCheckResult Finish(bool checkedNow, bool hasUpdate, ReleaseInfo? release, string message)
    {
        var result = new UpdateCheckResult(checkedNow, hasUpdate, release, message);
        CheckCompleted?.Invoke(this, result);
        return result;
    }

    /// <summary>玩家选择"跳过这个版本"：以后不再为它提示（手动检查仍查得到）。</summary>
    public void SkipVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        State.SkippedVersion = version;
        SaveState();
        AppPaths.Log($"[UPDATE] 玩家选择跳过版本 {version}");

        LastMessage = $"已跳过 {version}，之后不再提示。";
    }

    /// <summary>把状态落盘。失败只写日志（存档问题不该影响"要不要更新"这件事）。</summary>
    public void SaveState()
    {
        try
        {
            _store.Save(State);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"[UPDATE] 检查更新状态落盘失败：{ex.GetType().Name}：{ex.Message}");
        }
    }
}

/// <summary>一次检查更新的结果。</summary>
internal sealed record UpdateCheckResult(bool CheckedNow, bool HasUpdate, ReleaseInfo? Release, string Message);
