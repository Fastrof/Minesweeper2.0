namespace MineSweeper.Core.Audio;

/// <summary>
/// 音效合成器：把 <see cref="SoundEffect"/> 直接算成一段 16-bit 单声道 WAV 字节。
///
/// <b>为什么合成而不是找素材</b>：文档 §1.2.1 ④ 说自绘路线的收益是"天然不引入第三方
/// 美术资源的版权问题"，§7.3 又把从 Win7 扫雷提取音效划成"仅限本地学习、不得随发布分发"的红线。
/// 音效用代码合成就是同一思路在听觉上的对应做法：**零素材、零体积、零版权风险**。
///
/// 这里是纯数学 + 字节拼装，不引用任何 UI/播放 API，所以能直接进 Core 单测。
/// </summary>
public static class WavSynth
{
    /// <summary>采样率。</summary>
    public const int SampleRate = 44100;

    /// <summary>位深（16-bit PCM）。</summary>
    public const int BitsPerSample = 16;

    private const int HeaderSize = 44;

    private enum Waveform
    {
        Sine,
        Square,
        Noise,
    }

    /// <summary>生成指定音效的完整 WAV 文件字节（含 44 字节头）。</summary>
    public static byte[] CreateWav(SoundEffect effect)
    {
        float[] samples = RenderSamples(effect);
        return EncodeWav(samples);
    }

    /// <summary>生成指定音效的 PCM 采样（-1..1），不含 WAV 头。测试与波形检查用。</summary>
    public static float[] RenderSamples(SoundEffect effect)
    {
        return effect switch
        {
            SoundEffect.Reveal => RenderReveal(),
            SoundEffect.Flag => RenderFlag(),
            SoundEffect.Chord => RenderChord(),
            SoundEffect.Explosion => RenderExplosion(),
            SoundEffect.Victory => RenderVictory(),
            SoundEffect.Startup => RenderStartup(),
            _ => RenderReveal(),
        };
    }

    /// <summary>音效时长（毫秒），供 UI 预算播放资源。</summary>
    public static int DurationMs(SoundEffect effect) => (int)Math.Round(RenderSamples(effect).Length * 1000.0 / SampleRate);

    // ---------------------------------------------------------------- 各个音效

    /// <summary>翻开：一个短促的"哒"。</summary>
    private static float[] RenderReveal()
        => Mix(new[] { Tone(880, 45, 0.35f, Waveform.Sine) });

    /// <summary>插旗：比翻开略高一点，形成手感区分。</summary>
    private static float[] RenderFlag()
        => Mix(new[] { Tone(1320, 35, 0.26f, Waveform.Square) });

    /// <summary>连开：两个上行音。</summary>
    private static float[] RenderChord()
        => Mix(new[]
        {
            Tone(660, 40, 0.30f, Waveform.Sine),
            Tone(990, 55, 0.30f, Waveform.Sine, startMs: 38),
        });

    /// <summary>踩雷：噪声 + 快速下滑的低频。</summary>
    private static float[] RenderExplosion()
    {
        float[] noise = Tone(0, 300, 0.45f, Waveform.Noise);
        float[] sweep = Sweep(220, 55, 300, 0.40f);
        return Mix(new[] { noise, sweep });
    }

    /// <summary>胜利：四音上行琶音（C-E-G-C）。</summary>
    private static float[] RenderVictory()
        => Mix(new[]
        {
            Tone(523, 110, 0.28f, Waveform.Sine),
            Tone(659, 110, 0.28f, Waveform.Sine, startMs: 95),
            Tone(784, 110, 0.28f, Waveform.Sine, startMs: 190),
            Tone(1047, 200, 0.30f, Waveform.Sine, startMs: 285),
        });

    /// <summary>
    /// 启动提示音（v0.8）：程序启动 / 切换难度时播放。
    ///
    /// 刻意做成**一段短旋律**（G-C-E-G 上行 + 尾音收束），对应原版扫雷"开局那一声"的听感：
    /// 比胜利音略低、略短，避免和"通关"混淆；又比单个"哒"更成句，才像"启动"。
    /// </summary>
    private static float[] RenderStartup()
        => Mix(new[]
        {
            Tone(392, 120, 0.26f, Waveform.Sine),                 // G4
            Tone(523, 120, 0.26f, Waveform.Sine, startMs: 110),   // C5
            Tone(659, 120, 0.26f, Waveform.Sine, startMs: 220),   // E5
            Tone(784, 240, 0.28f, Waveform.Sine, startMs: 330),   // G5（尾音收束）
        });

    // ---------------------------------------------------------------- 波形基元

    private static int MsToSamples(double ms) => (int)Math.Round(ms * SampleRate / 1000.0);

    /// <summary>
    /// 一个带"快速起音 + 指数衰减"包络的音；<paramref name="freq"/> 为 0 时按噪声处理。
    /// 每个音的时长按包络截断，不会拖尾。
    /// </summary>
    private static float[] Tone(double freq, double durationMs, float gain, Waveform waveform, double startMs = 0)
    {
        int start = MsToSamples(startMs);
        int length = MsToSamples(durationMs);
        var buffer = new float[start + length];
        var rng = new Random(1234 + (int)freq + (int)durationMs);

        for (int i = 0; i < length; i++)
        {
            float envelope = Envelope(i, length);
            float value;

            switch (waveform)
            {
                case Waveform.Square:
                    value = Math.Sin(2 * Math.PI * freq * i / SampleRate) >= 0 ? 1f : -1f;
                    break;

                case Waveform.Noise:
                    value = (float)((rng.NextDouble() * 2) - 1);
                    break;

                default:
                    value = (float)Math.Sin(2 * Math.PI * freq * i / SampleRate);
                    break;
            }

            buffer[start + i] = value * envelope * gain;
        }

        return buffer;
    }

    /// <summary>频率从 <paramref name="fromHz"/> 线性滑到 <paramref name="toHz"/> 的音（爆炸用）。</summary>
    private static float[] Sweep(double fromHz, double toHz, double durationMs, float gain)
    {
        int length = MsToSamples(durationMs);
        var buffer = new float[length];
        double phase = 0;

        for (int i = 0; i < length; i++)
        {
            double t = length <= 1 ? 0 : (double)i / (length - 1);
            double freq = fromHz + ((toHz - fromHz) * t);
            phase += 2 * Math.PI * freq / SampleRate;
            buffer[i] = (float)Math.Sin(phase) * Envelope(i, length) * gain;
        }

        return buffer;
    }

    /// <summary>快速起音（约 3ms）+ 指数衰减到静音，避免爆音。</summary>
    private static float Envelope(int index, int length)
    {
        int attack = Math.Max(1, MsToSamples(3));
        float attackGain = index < attack ? (float)index / attack : 1f;

        double t = length <= 1 ? 1 : (double)index / (length - 1);
        float decay = (float)Math.Exp(-4.5 * t);

        return attackGain * decay;
    }

    /// <summary>把多条波形按索引相加（长度取最长），并夹到 -1..1。</summary>
    private static float[] Mix(IReadOnlyList<float[]> parts)
    {
        int length = 0;
        foreach (float[] part in parts)
        {
            length = Math.Max(length, part.Length);
        }

        var mixed = new float[length];
        foreach (float[] part in parts)
        {
            for (int i = 0; i < part.Length; i++)
            {
                mixed[i] += part[i];
            }
        }

        for (int i = 0; i < mixed.Length; i++)
        {
            mixed[i] = Math.Clamp(mixed[i], -1f, 1f);
        }

        return mixed;
    }

    // ---------------------------------------------------------------- WAV 封装

    /// <summary>按 16-bit 单声道 PCM 封装成 WAV 字节。</summary>
    private static byte[] EncodeWav(float[] samples)
    {
        int dataSize = samples.Length * (BitsPerSample / 8);
        var wav = new byte[HeaderSize + dataSize];

        int p = 0;
        WriteAscii(wav, ref p, "RIFF");
        WriteInt32(wav, ref p, 36 + dataSize);
        WriteAscii(wav, ref p, "WAVE");

        WriteAscii(wav, ref p, "fmt ");
        WriteInt32(wav, ref p, 16);                                          // fmt 块长度
        WriteInt16(wav, ref p, 1);                                           // PCM
        WriteInt16(wav, ref p, 1);                                           // 单声道
        WriteInt32(wav, ref p, SampleRate);
        WriteInt32(wav, ref p, SampleRate * 1 * (BitsPerSample / 8));        // byte rate
        WriteInt16(wav, ref p, (short)(1 * (BitsPerSample / 8)));            // block align
        WriteInt16(wav, ref p, BitsPerSample);

        WriteAscii(wav, ref p, "data");
        WriteInt32(wav, ref p, dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)Math.Clamp(Math.Round(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
            WriteInt16(wav, ref p, value);
        }

        return wav;
    }

    private static void WriteAscii(byte[] target, ref int offset, string text)
    {
        foreach (char c in text)
        {
            target[offset++] = (byte)c;
        }
    }

    private static void WriteInt16(byte[] target, ref int offset, short value)
    {
        target[offset++] = (byte)(value & 0xFF);
        target[offset++] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32(byte[] target, ref int offset, int value)
    {
        target[offset++] = (byte)(value & 0xFF);
        target[offset++] = (byte)((value >> 8) & 0xFF);
        target[offset++] = (byte)((value >> 16) & 0xFF);
        target[offset++] = (byte)((value >> 24) & 0xFF);
    }
}
