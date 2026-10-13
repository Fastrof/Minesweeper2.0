using System.Diagnostics;
using System.Runtime.InteropServices;
using MineSweeper.Core.Updates;

namespace MineSweeper.App.Services;

/// <summary>下载进度（只暴露两个数，UI 自己决定怎么显示）。</summary>
internal sealed record UpdateDownloadProgress(long Received, long Total)
{
    /// <summary>0..1；总长度未知时为 0。</summary>
    public double Fraction => Total > 0 ? Math.Clamp((double)Received / Total, 0d, 1d) : 0d;

    /// <summary>给状态栏/进度条用的一行文字。</summary>
    public string Text => Total > 0
        ? $"{Received / 1048576d:0.0} MB / {Total / 1048576d:0.0} MB（{Fraction:P0}）"
        : $"{Received / 1048576d:0.0} MB";
}

internal sealed record UpdateDownloadResult(bool Success, string? FilePath, string Message);

internal sealed record UpdateInstallResult(bool Success, bool NeedsRestart, string Message);

/// <summary>
/// 自动下载与安装更新包（v1.1）。
///
/// 平台约束（决定了这里的实现方式，改之前请先看这一段）：
/// <list type="bullet">
/// <item><b>打包版（MSIX）能就地升级</b>：<c>PackageManager.AddPackageAsync</c> 可以安装同族的新版本。
/// 但"正在运行的同族包"默认会被 Windows 拒绝（0x80073D02 包在使用中），
/// 所以必须带 <c>ForceApplicationShutdown</c> —— 那会**结束当前进程**。
/// 因此这里的原则是：<b>下载可以自动，安装必须经过玩家同意</b>，绝不悄悄杀掉游戏。</item>
/// <item><b>未打包版（GitHub 侧载版）没有包身份</b>，无法给自己安装升级包；
/// 只能把下载好的 .msix 交给系统的 App Installer（<c>UseShellExecute</c>），由玩家点一下"安装"。
/// 这是系统限制，不是取巧。</item>
/// <item><b>下载完必须校验</b>：优先用发布说明里的 SHA256；没有哈希就放行（安装时 Windows 仍会校验包签名）。</item>
/// <item>失败一律只写日志并返回失败原因，绝不影响游戏。</item>
/// </list>
/// </summary>
internal sealed class UpdateDownloader
{
    /// <summary>下载用的 HttpClient：超时放宽到 10 分钟（93 MB 在慢网下需要时间）。</summary>
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly Func<bool> _isPackaged;

    public UpdateDownloader(string updateDirectory, Func<bool> isPackaged)
    {
        UpdateDirectory = updateDirectory;
        _isPackaged = isPackaged;
    }

    /// <summary>下载目录（&lt;数据目录&gt;\updates）。</summary>
    public string UpdateDirectory { get; }

    /// <summary>是否正在下载（防止重复触发）。</summary>
    public bool IsBusy { get; private set; }

    /// <summary>进度回调（在后台线程，调用方自己切 UI 线程）。</summary>
    public event EventHandler<UpdateDownloadProgress>? ProgressChanged;

    /// <summary>当前进程的架构（GitHub 上的包名里就带这个）。</summary>
    public static string CurrentArchitecture => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => UpdateAssetSelector.Arm64,
        _ => UpdateAssetSelector.X64,
    };

    /// <summary>
    /// 下载安装包。已经下过而且校验通过的文件会**直接复用**（幂等），
    /// 这样"点两次下载"不会白跑一趟流量。
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadAsync(
        ReleaseInfo release, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return new UpdateDownloadResult(false, null, "已经在下载了。");
        }

        ReleaseAsset? asset = UpdateAssetSelector.SelectInstaller(
            release.Assets, CurrentArchitecture, out string selectReason);

        if (asset is null)
        {
            AppPaths.Log($"[UPDATE] 无法下载：{selectReason}");
            return new UpdateDownloadResult(false, null, selectReason);
        }

        IsBusy = true;
        try
        {
            string directory = Path.Combine(UpdateDirectory, SanitizeVersion(release.Version));
            Directory.CreateDirectory(directory);

            string finalPath = Path.Combine(directory, Path.GetFileName(asset.Name));
            string tempPath = finalPath + ".part";

            // 已经下好过：复用，不再跑一遍流量（但哈希对不上就重下）。
            if (File.Exists(finalPath))
            {
                if (FileHash.VerifySha256(finalPath, expectedSha256, out string reuseReason))
                {
                    AppPaths.Log($"[UPDATE] 复用已下载的安装包：{finalPath}（{reuseReason}）");
                    return new UpdateDownloadResult(true, finalPath, $"已经下载好了：{Path.GetFileName(finalPath)}");
                }

                AppPaths.Log($"[UPDATE] 已存在的下载文件校验不通过，删除重下：{reuseReason}");
                TryDelete(finalPath);
            }

            AppPaths.Log($"[UPDATE] 开始下载：{asset.Name}（{asset.Size / 1048576d:0.0} MB）");

            using (var response = await Client
                       .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateDownloadResult(false, null, $"下载失败：HTTP {(int)response.StatusCode}");
                }

                long total = response.Content.Headers.ContentLength ?? asset.Size;
                long received = 0;
                var buffer = new byte[81920];
                long lastReport = 0;

                await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream target = File.Create(tempPath))
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        received += read;

                        // 限流上报：每 ~200 KB 报一次，免得把 UI 线程刷爆。
                        if (received - lastReport >= 204800 || received == total)
                        {
                            lastReport = received;
                            ProgressChanged?.Invoke(this, new UpdateDownloadProgress(received, total));
                        }
                    }

                    await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            // 先落成正式文件名，再校验；失败就删掉，绝不留一个半成品在那里冒充"已下载"。
            File.Move(tempPath, finalPath, overwrite: true);

            if (!FileHash.VerifySha256(finalPath, expectedSha256, out string verifyReason))
            {
                TryDelete(finalPath);
                AppPaths.Log($"[UPDATE] 下载完成但校验失败，已删除：{verifyReason}");
                return new UpdateDownloadResult(false, null, $"下载的文件校验失败：{verifyReason}");
            }

            CleanupOldVersions(keep: directory);
            AppPaths.Log($"[UPDATE] 下载完成：{finalPath}（{verifyReason}）");
            return new UpdateDownloadResult(true, finalPath, $"下载完成，{verifyReason}");
        }
        catch (OperationCanceledException)
        {
            AppPaths.Log("[UPDATE] 下载被取消。");
            return new UpdateDownloadResult(false, null, "下载已取消。");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"[UPDATE] 下载失败：{ex.GetType().Name}：{ex.Message}");
            return new UpdateDownloadResult(false, null, $"下载失败：{ex.GetType().Name}（不影响游戏）");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 安装已下载的包。
    /// 打包版走 <c>PackageManager</c>；未打包版只能交给系统安装程序（玩家点一下确认）。
    /// </summary>
    public async Task<UpdateInstallResult> InstallAsync(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            return new UpdateInstallResult(false, false, "安装包不见了，请重新下载。");
        }

        if (!_isPackaged())
        {
            return LaunchSystemInstaller(packagePath);
        }

        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();

            // ForceApplicationShutdown：同族包正在运行时不带这个参数会直接失败（0x80073D02）。
            // 它会让 Windows 结束当前进程以完成更新 —— 所以**调用这个方法的按钮上必须写清"会重启"**。
            Windows.Management.Deployment.DeploymentResult result = await manager.AddPackageAsync(
                new Uri(packagePath),
                null,
                Windows.Management.Deployment.DeploymentOptions.ForceApplicationShutdown);

            AppPaths.Log($"[UPDATE] 升级包安装完成：{result.ExtendedErrorCode?.Message ?? "ok"}");
            return new UpdateInstallResult(true, true, "更新已安装，重新打开游戏即可使用新版本。");
        }
        catch (Exception ex)
        {
            // 常见原因：证书不受信任、包被占用、包签名与清单不匹配、版本不比当前高。
            AppPaths.Log($"[UPDATE] 升级包安装失败：{ex.GetType().Name}：{ex.Message}");
            return new UpdateInstallResult(
                false,
                false,
                $"自动安装失败（{ex.GetType().Name}）。可以点「打开发布页」手动下载安装。");
        }
    }

    /// <summary>未打包版：把 .msix 交给系统安装程序。玩家会看到标准的安装确认界面。</summary>
    private static UpdateInstallResult LaunchSystemInstaller(string packagePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(packagePath) { UseShellExecute = true });
            AppPaths.Log($"[UPDATE] 已唤起系统安装程序：{packagePath}");
            return new UpdateInstallResult(true, false, "已打开安装程序，按提示点「安装」即可完成更新。");
        }
        catch (Exception ex)
        {
            AppPaths.Log($"[UPDATE] 唤起安装程序失败：{ex.GetType().Name}：{ex.Message}");
            return new UpdateInstallResult(false, false, "打不开安装程序，可以点「打开发布页」手动下载。");
        }
    }

    /// <summary>只保留最新一个版本的目录，别让 updates\ 无限长大（一个包 90+ MB）。</summary>
    private void CleanupOldVersions(string keep)
    {
        try
        {
            if (!Directory.Exists(UpdateDirectory))
            {
                return;
            }

            foreach (string dir in Directory.GetDirectories(UpdateDirectory))
            {
                if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.Delete(dir, recursive: true);
                AppPaths.Log($"[UPDATE] 清理旧版本下载目录：{Path.GetFileName(dir)}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"[UPDATE] 清理旧下载失败（忽略）：{ex.GetType().Name}");
        }
    }

    /// <summary>把版本号里的非法路径字符换掉（版本号来自网络，不能直接当目录名用）。</summary>
    private static string SanitizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "unknown";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(version.Length);
        foreach (char c in version)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string cleaned = builder.ToString().Trim().Trim('.');
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"[UPDATE] 删除文件失败（忽略）：{ex.GetType().Name}");
        }
    }
}
