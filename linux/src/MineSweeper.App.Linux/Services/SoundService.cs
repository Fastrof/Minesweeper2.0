using System.Diagnostics;
using MineSweeper.Core.Audio;

namespace MineSweeper.App.Linux.Services;

/// <summary>
/// 音效服务：WAV 由 Core 的 <c>WavSynth</c> 代码合成（零素材），这里负责落到磁盘并播放。
///
/// Linux 播放策略（任选其一，找不到就静默）：
///   1. paplay  （PulseAudio / PipeWire 用户空间都有）
///   2. pw-play （PipeWire 原生）
///   3. aplay   （ALSA，最底层）
///
/// 与 Windows 端同一条铁律：**音效失败绝不影响游戏**。
/// </summary>
public sealed class SoundService : IDisposable
{
    private readonly Dictionary<SoundEffect, string> _files = new();
    private string? _player;

    /// <summary>音效开关（来自设置）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>音量 0..1（仅 paplay / pw-play 支持，其余忽略）。</summary>
    public double Volume { get; set; } = 0.7;

    /// <summary>是否成功找到播放器（诊断用）。</summary>
    public bool HasPlayer => _player is not null;

    /// <summary>合成并准备所有音效文件（首次运行；已存在的不覆盖，方便玩家替换）。</summary>
    public void Initialize()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SoundsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"[音效] 无法创建音效目录：{ex.Message}");
            return;
        }

        foreach (SoundEffect effect in Enum.GetValues<SoundEffect>())
        {
            string path = Path.Combine(AppPaths.SoundsDirectory, $"{effect}.wav");
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllBytes(path, WavSynth.CreateWav(effect));
                }

                _files[effect] = path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppPaths.Log($"[音效] 合成 {effect} 失败：{ex.Message}");
            }
        }

        _player = FindPlayer();
        AppPaths.Log($"[音效] 播放器 = {_player ?? "未找到（静默）"}，音效文件 {_files.Count} 个");
    }

    /// <summary>播放一个音效（异步、不阻塞、失败静默）。</summary>
    public void Play(SoundEffect effect)
    {
        if (!Enabled || _player is null || !_files.TryGetValue(effect, out string? file))
        {
            return;
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = _player,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            if (_player.EndsWith("paplay", StringComparison.Ordinal))
            {
                // paplay 的音量是 0..65536
                int volume = (int)Math.Clamp(Volume * 65536, 0, 65536);
                info.ArgumentList.Add($"--volume={volume}");
            }
            else if (_player.EndsWith("pw-play", StringComparison.Ordinal))
            {
                info.ArgumentList.Add($"--volume={Volume.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
            }

            info.ArgumentList.Add(file);

            using Process? process = Process.Start(info);
            // 不 WaitForExit：音效是 fire-and-forget，播放器自己会退出。
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppPaths.Log($"[音效] 播放 {effect} 失败：{ex.Message}");
        }
    }

    private static string? FindPlayer()
    {
        string[] candidates = ["paplay", "pw-play", "aplay"];
        string[] pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries);

        foreach (string candidate in candidates)
        {
            foreach (string dir in pathDirs)
            {
                string full = Path.Combine(dir, candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        // 无需释放：每次播放都是独立进程
    }
}
