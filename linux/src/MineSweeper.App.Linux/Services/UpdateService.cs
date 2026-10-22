using System.Net.Http;
using MineSweeper.Core.Updates;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 检查更新（**Linux 版简化实现**）。
///
/// 与 Windows 端的差异（有意为之）：
///   - Windows 是 MSIX，可以自动下载安装包；Linux 的 .deb **不能自我更新**（需要 apt/root），
///     所以这里只做「发现新版本 → 打开 GitHub 页面让玩家自己下载 .deb」。
///   - 策略判断仍复用 Core：<c>UpdateState.ShouldCheck</c> / <c>ShouldNotify</c> / <c>AppVersion.IsNewer</c>。
///
/// 设计原则与 Windows 一致：**检查失败一律静默**，绝不影响游戏。
/// </summary>
public sealed class UpdateService
{
    /// <summary>
    /// 取发布列表（而不是 releases/latest）：Linux 端只认带 <see cref="ReleaseTagPrefix"/> 前缀的
    /// tag，Windows 端的 v1.5.2 之类不能被当成"Linux 有新版"。
    /// </summary>
    private const string ReleasesUrl =
        "https://api.github.com/repos/Fastrof/Minesweeper2.0/releases?per_page=30";

    /// <summary>
    /// Linux 端发布 tag 的前缀：<c>linux-v1.5.2</c>。
    /// Windows 端继续用 <c>v1.5.2</c>，两边发布节奏互不干扰。
    /// </summary>
    public const string ReleaseTagPrefix = "linux-v";

    private static readonly HttpClient Http = CreateClient();

    private readonly Storage _storage;

    public UpdateService(Storage storage) => _storage = storage;

    /// <summary>当前版本（与 csproj 的 Version 一致）。</summary>
    public static string CurrentVersion => "1.5.2";

    /// <summary>
    /// 游戏内展示用的版本号。Linux 端与 Windows 端共用同一个 Core，
    /// 但两边的 UI/打包是分开维护的，所以界面上一律带 <c>-Linux</c> 后缀，避免玩家报版本时混淆。
    /// </summary>
    public static string DisplayVersion => CurrentVersion + "-Linux";

    /// <summary>发现可更新的版本。</summary>
    public event EventHandler<ReleaseInfo>? UpdateAvailable;

    /// <summary>检查失败时的原因（给设置面板显示，null 表示成功或无更新）。</summary>
    public string? LastError { get; private set; }

    /// <summary>上次检查到的版本（无论是否更新）。</summary>
    public ReleaseInfo? LastRelease { get; private set; }

    /// <summary>
    /// 检查更新。
    /// <paramref name="manual"/> = 玩家主动点「检查更新」（忽略最小间隔）；
    /// false = 启动时静默检查（受频率/暂停策略约束）。
    /// </summary>
    public async Task<ReleaseInfo?> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        LastError = null;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (!manual)
        {
            TimeSpan interval = TimeSpan.FromHours(
                UpdateFrequency.Normalize(_storage.Settings.UpdateCheckIntervalHours));

            if (!UpdateSchedule.ShouldAutoCheck(_storage.UpdateState.LastCheckUtc, nowUtc, (int)interval.TotalHours))
            {
                return null;
            }

            if (UpdatePause.IsPaused(_storage.Settings.UpdatePausedUntilUtc, nowUtc))
            {
                return null;
            }
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.ParseAdd($"MineSweeper2-Linux/{DisplayVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            _storage.UpdateState.LastCheckUtc = nowUtc;
            _storage.SaveUpdateState();

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"HTTP {(int)response.StatusCode}";
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ReleaseInfo? release = ParseLinuxRelease(json, out string? error);
            if (release is null)
            {
                LastError = error ?? "发布信息解析失败";
                return null;
            }

            LastRelease = release;
            _storage.UpdateState.LatestKnownVersion = release.Version;
            _storage.SaveUpdateState();

            if (_storage.UpdateState.SkippedVersion == release.Version)
            {
                return null;
            }

            if (!AppVersion.IsNewer(release.Version, CurrentVersion))
            {
                return null;
            }

            AppPaths.Log($"[更新] 发现新版本 {release.Version}（当前 {CurrentVersion}）");
            UpdateAvailable?.Invoke(this, release);
            return release;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            LastError = ex.Message;
            AppPaths.Log($"[更新] 检查失败（已静默）：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从 <c>/releases</c> 的数组里挑出 **Linux 端** 的最新发布：
    /// 只认 tag 以 <c>linux-v</c> 开头（大小写不敏感），剥掉前缀与可选的 v 之后当版本号。
    /// </summary>
    private static ReleaseInfo? ParseLinuxRelease(string json, out string? error)
    {
        error = null;

        try
        {
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                });

            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                error = "发布列表不是数组";
                return null;
            }

            ReleaseInfo? best = null;
            foreach (System.Text.Json.JsonElement item in document.RootElement.EnumerateArray())
            {
                if (!ReleaseJson.TryParse(item.GetRawText(), out ReleaseInfo? parsed, out _) || parsed is null)
                {
                    continue;
                }

                if (!parsed.TagName.StartsWith(ReleaseTagPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string version = parsed.TagName[ReleaseTagPrefix.Length..].TrimStart('v', 'V');
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                ReleaseInfo candidate = parsed with { Version = version };
                if (best is null || AppVersion.IsNewer(candidate.Version, best.Version))
                {
                    best = candidate;
                }
            }

            if (best is null)
            {
                error = "暂无 Linux 版发布";
            }

            return best;
        }
        catch (System.Text.Json.JsonException ex)
        {
            error = "发布列表解析失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>跳过某个版本（不再提示）。</summary>
    public void Skip(string version)
    {
        _storage.UpdateState.SkippedVersion = version;
        _storage.SaveUpdateState();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MineSweeper2-Linux/" + DisplayVersion);
        return client;
    }
}
