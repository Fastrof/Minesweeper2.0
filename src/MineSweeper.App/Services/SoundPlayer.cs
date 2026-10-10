using MineSweeper.Core.Audio;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace MineSweeper.App.Services;

/// <summary>
/// 音效播放（零素材）。
///
/// 音效不是随工程分发的素材文件，而是**首次运行时由 <see cref="WavSynth"/> 合成**到
/// <c>%LOCALAPPDATA%\MineSweeper2\sounds\*.wav</c>：
/// <list type="bullet">
/// <item>工程里不需要任何音频文件，也不存在第三方素材的版权问题（文档 §7.3 的红线）</item>
/// <item>文件已经存在就<b>不覆盖</b>——所以想换成自己的音效，直接丢同名 wav 进去即可</item>
/// </list>
///
/// 每个音效各持一个 <see cref="MediaPlayer"/>：一个实例只能承载一个源，共用一个会让
/// "踩雷"打断"翻开"。音频子系统初始化失败只记日志，绝不影响游戏本身。
/// </summary>
internal sealed class SoundPlayer : IDisposable
{
    private readonly Dictionary<SoundEffect, MediaPlayer> _players = new();
    private readonly string _directory;

    private bool _enabled = true;
    private double _volume = 0.7;
    private bool _disposed;

    /// <param name="directory">音效目录（通常 <c>%LOCALAPPDATA%\MineSweeper2\sounds</c>）。</param>
    public SoundPlayer(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;

        try
        {
            System.IO.Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log($"音效目录不可用（{ex.GetType().Name}：{ex.Message}），本次运行不播放音效。");
            return;
        }

        foreach (SoundEffect effect in Enum.GetValues<SoundEffect>())
        {
            try
            {
                string path = EnsureSoundFile(effect);
                var source = MediaSource.CreateFromUri(new Uri(path));
                _players[effect] = new MediaPlayer { Source = source, AutoPlay = false };
            }
            catch (Exception ex)
            {
                AppPaths.Log($"音效 {effect} 初始化失败：{ex.GetType().Name}：{ex.Message}");
            }
        }

        if (_players.Count == 0)
        {
            AppPaths.Log("音效子系统不可用，本次运行静默。");
        }
    }

    /// <summary>是否至少有一个音效可用。</summary>
    public bool IsAvailable => _players.Count > 0;

    /// <summary>音效目录（排查/替换音效用）。刻意不叫 Directory，免得遮蔽 System.IO.Directory。</summary>
    public string SoundDirectory => _directory;

    /// <summary>应用"开关 + 音量"。</summary>
    public void Apply(bool enabled, double volume)
    {
        _enabled = enabled;
        _volume = Math.Clamp(volume, 0d, 1d);

        foreach (MediaPlayer player in _players.Values)
        {
            try
            {
                player.Volume = _volume;
            }
            catch (Exception)
            {
                // 音量设置失败不值得打扰玩家
            }
        }
    }

    /// <summary>播放一个音效。</summary>
    public void Play(SoundEffect effect)
    {
        if (_disposed || !_enabled || _volume <= 0d)
        {
            return;
        }

        if (!_players.TryGetValue(effect, out MediaPlayer? player))
        {
            return;
        }

        try
        {
            // 从头重放：同一个音效连续触发（例如连点）时不会因为"已播过"而静音
            player.PlaybackSession.Position = TimeSpan.Zero;
            player.Play();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"音效 {effect} 播放失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (MediaPlayer player in _players.Values)
        {
            try
            {
                player.Dispose();
            }
            catch (Exception)
            {
            }
        }

        _players.Clear();
    }

    /// <summary>返回音效文件路径；文件不存在就合成一份写进去。</summary>
    private string EnsureSoundFile(SoundEffect effect)
    {
        string path = Path.Combine(_directory, FileNameOf(effect));

        if (File.Exists(path))
        {
            // 已存在 → 尊重它（玩家可以放自己的音效进来）
            return path;
        }

        File.WriteAllBytes(path, WavSynth.CreateWav(effect));
        AppPaths.Log($"已合成音效 {FileNameOf(effect)}（{WavSynth.DurationMs(effect)} ms）。");
        return path;
    }

    private static string FileNameOf(SoundEffect effect) => effect switch
    {
        SoundEffect.Reveal => "reveal.wav",
        SoundEffect.Flag => "flag.wav",
        SoundEffect.Chord => "chord.wav",
        SoundEffect.Explosion => "explosion.wav",
        SoundEffect.Startup => "startup.wav",
        _ => "victory.wav",
    };
}
