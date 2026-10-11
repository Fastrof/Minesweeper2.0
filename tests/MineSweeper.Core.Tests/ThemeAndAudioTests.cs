using MineSweeper.Core.Audio;
using MineSweeper.Core.Persistence;
using MineSweeper.Core.Settings;
using MineSweeper.Core.Themes;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>主题（调色板）、玩家设置与音效合成的测试。</summary>
public sealed class ThemeAndAudioTests
{
    // ------------------------------------------------------------ 颜色文本

    [Fact]
    public void 颜色支持三种写法()
    {
        Assert.True(PaletteColor.TryParse("#FF8800", out PaletteColor six));
        Assert.Equal(((byte)0xFF, (byte)0xFF, (byte)0x88, (byte)0x00), (six.A, six.R, six.G, six.B));

        // #RGB 简写，每位重复一次
        Assert.True(PaletteColor.TryParse("#0AF", out PaletteColor three));
        Assert.Equal(((byte)0xFF, (byte)0x00, (byte)0xAA, (byte)0xFF), (three.A, three.R, three.G, three.B));

        // #AARRGGBB
        Assert.True(PaletteColor.TryParse("#401E63D8", out PaletteColor eight));
        Assert.Equal(((byte)0x40, (byte)0x1E, (byte)0x63, (byte)0xD8), (eight.A, eight.R, eight.G, eight.B));

        // 井号可省略
        Assert.True(PaletteColor.TryParse("FF8800", out PaletteColor bare));
        Assert.Equal(six, bare);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("红色")]
    public void 非法颜色被拒绝(string? text)
    {
        Assert.False(PaletteColor.TryParse(text, out _));
    }

    [Fact]
    public void 颜色往返一致()
    {
        foreach (PaletteColor original in new[]
                 {
                     new PaletteColor(0x00, 0x11, 0x22),
                     new PaletteColor(0x40, 0x1E, 0x63),
                 })
        {
            Assert.True(PaletteColor.TryParse(original.ToHex(), out PaletteColor parsed));
            Assert.Equal(original, parsed);
        }
    }

    // ------------------------------------------------------------ 调色板

    [Fact]
    public void 内置主题齐全且Id唯一()
    {
        Assert.Equal(4, BoardPalette.BuiltIn.Count);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BoardPalette palette in BoardPalette.BuiltIn)
        {
            Assert.True(ids.Add(palette.Id), $"主题 Id 重复：{palette.Id}");
            Assert.False(string.IsNullOrWhiteSpace(palette.DisplayName));

            // 每套主题（含数字八色）都必须是合法颜色
            Assert.True(PaletteColor.TryParse(palette.CoveredFill, out _));
            Assert.True(PaletteColor.TryParse(palette.AimingFill, out _));
            Assert.Equal(8, palette.NumberColors.Length);
            for (int n = 1; n <= 8; n++)
            {
                _ = palette.NumberColor(n);
            }
        }

        Assert.NotNull(BoardPalette.FindById("dark"));
        Assert.NotNull(BoardPalette.FindById("DARK"));
        Assert.Null(BoardPalette.FindById("不存在的主题"));
    }

    [Fact]
    public void 调色板规范化把坏颜色回落默认()
    {
        var broken = new BoardPalette
        {
            CoveredFill = "这不是颜色",
            RevealedFill = string.Empty,
            NumberColors = new[] { "#123456" }, // 长度不足 8
        };

        broken.Normalize();

        Assert.Equal(BoardPalette.Classic.CoveredFill, broken.CoveredFill);
        Assert.Equal(BoardPalette.Classic.RevealedFill, broken.RevealedFill);
        Assert.Equal(8, broken.NumberColors.Length);
        Assert.Equal("#123456", broken.NumberColors[0]);
        Assert.Equal(BoardPalette.Classic.NumberColors[7], broken.NumberColors[7]);
    }

    [Fact]
    public void 主题覆盖只改指定颜色()
    {
        BoardPalette dark = BoardPalette.Dark;

        BoardPalette resolved = BoardPalette.Resolve("dark", new Dictionary<string, string>
        {
            ["coveredFill"] = "#123456",
            ["gridLine"] = "不合法",
            ["不认识的键"] = "#FFFFFF",
        });

        Assert.Equal("#123456", resolved.CoveredFill);
        Assert.Equal(dark.RevealedFill, resolved.RevealedFill);

        // 非法值和未知键被丢掉，其余照旧生效
        Assert.Equal(dark.GridLine, resolved.GridLine);
    }

    [Fact]
    public void 未知主题回落经典()
    {
        BoardPalette resolved = BoardPalette.Resolve("根本没这个主题", null);
        Assert.Equal(BoardPalette.Classic.CoveredFill, resolved.CoveredFill);
    }

    /// <summary>
    /// 文档 §4.1 要求"问号：黄色问号"。经典主题的问号必须是偏黄的暖色，
    /// 而不是早期实现里的深蓝——这条防止它被改回去。
    /// </summary>
    [Fact]
    public void 经典主题的问号是黄色()
    {
        PaletteColor question = BoardPalette.Classic.Color("questionMark");

        Assert.True(question.R > question.B, $"问号应当偏黄（R>B），当前 #{question.ToHex()}。");
        Assert.True(question.R >= question.G, $"问号应当偏黄（R>=G），当前 #{question.ToHex()}。");
        Assert.True(question.R > 0x80, $"问号应当足够亮，当前 #{question.ToHex()}。");
    }

    /// <summary>数字八色可以逐项覆盖（number1..number8），越界或非法项被忽略。</summary>
    [Fact]
    public void 数字颜色可逐项覆盖()
    {
        BoardPalette resolved = BoardPalette.Resolve("classic", new Dictionary<string, string>
        {
            ["number1"] = "#FF00FF",
            ["number8"] = "#00FF00",
            ["number9"] = "#FFFFFF", // 越界，忽略
        });

        PaletteColor one = resolved.NumberColor(1);
        Assert.Equal((0xFF, 0x00, 0xFF), (one.R, one.G, one.B));

        PaletteColor eight = resolved.NumberColor(8);
        Assert.Equal((0x00, 0xFF, 0x00), (eight.R, eight.G, eight.B));

        // 未覆盖的保持经典配色
        PaletteColor two = resolved.NumberColor(2);
        Assert.Equal((0x00, 0x80, 0x00), (two.R, two.G, two.B));
    }

    /// <summary>
    /// App 层是通过字符串键名取色的（<c>palette.Color("coveredFill")</c>）。
    /// 键名写错不会编译失败，只会静默回落到兜底色——那会变成"某个主题看起来是坏的"这种难查的 bug。
    /// 这个用例把 App 用到的 18 个键名全部点一遍，任何拼写不一致都会在这里失败。
    /// </summary>
    [Fact]
    public void 调色板认得App用到的全部颜色键()
    {
        string[] keysUsedByApp =
        {
            "coveredFill", "coveredHighlight", "coveredShadow",
            "revealedFill", "gridLine",
            "itemRevealedFill", "itemRevealedMineOutline",
            "explodedFill", "mineBody",
            "flagPole", "flagCloth", "wrongFlagCross", "questionMark",
            "aimingRing", "aimingFill",
            "highlightRing", "highlightFill",
            "heatLowFill", "heatHighFill",
        };

        var sentinel = new PaletteColor(0x01, 0x02, 0x03);

        foreach (BoardPalette palette in BoardPalette.BuiltIn)
        {
            foreach (string key in keysUsedByApp)
            {
                Assert.NotEqual(sentinel, palette.Color(key, sentinel));
            }

            // 取到的就是属性本身的值
            Assert.Equal(PaletteColor.ParseOr(palette.CoveredFill, default), palette.Color("coveredFill"));
            Assert.Equal(PaletteColor.ParseOr(palette.HighlightFill, default), palette.Color("highlightFill"));
        }

        // 不认识的键才回落到传入的兜底色
        Assert.Equal(sentinel, BoardPalette.Classic.Color("没这个键", sentinel));
    }

    // ------------------------------------------------------------ 玩家设置

    [Fact]
    public void 设置存取往返一致()
    {
        string path = TestPaths.NewFile("settings.json");
        var store = new AppSettingsStore(path);

        store.Save(new AppSettings
        {
            AudioEnabled = false,
            AudioVolume = 0.25,
            ThemeId = "dark",
            ThemeOverrides = new Dictionary<string, string> { ["coveredFill"] = "#101010" },
        });

        // 原子写盘：不留临时文件
        Assert.False(File.Exists(path + ".tmp"));

        SettingsLoadResult loaded = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.False(loaded.Settings.AudioEnabled);
        Assert.Equal(0.25, loaded.Settings.AudioVolume, 3);
        Assert.Equal("dark", loaded.Settings.ThemeId);
        Assert.Equal("#101010", loaded.Settings.ThemeOverrides["coveredFill"]);
    }

    [Fact]
    public void 设置文件不存在时新建默认()
    {
        var store = new AppSettingsStore(TestPaths.NewFile("settings.json"));

        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.CreatedNew, result.Status);
        Assert.True(result.Settings.AudioEnabled);
        Assert.Equal(BoardPalette.ClassicId, result.Settings.ThemeId);
    }

    [Fact]
    public void 损坏的设置降级重建并保留备份()
    {
        string path = TestPaths.NewFile("settings.json");
        File.WriteAllText(path, "{ 这不是合法 JSON ");

        SettingsLoadResult result = new AppSettingsStore(path).Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorrupt, result.Status);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));
    }

    [Fact]
    public void 高版本设置被拒绝且不被覆盖()
    {
        string path = TestPaths.NewFile("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 99, "audioEnabled": false }""");

        SettingsLoadResult result = new AppSettingsStore(path).Load();

        Assert.Equal(SettingsLoadStatus.RejectedVersionTooNew, result.Status);
        Assert.Contains("\"schemaVersion\": 99", File.ReadAllText(path));
    }

    [Fact]
    public void 设置规范化夹住非法取值()
    {
        var settings = new AppSettings
        {
            AudioVolume = 5.0,
            ThemeId = "不存在的主题",
            ThemeOverrides = new Dictionary<string, string>
            {
                ["coveredFill"] = "#123456",
                ["gridLine"] = "坏颜色",
            },
        };

        settings.Normalize();

        Assert.Equal(1.0, settings.AudioVolume, 3);
        Assert.Equal(BoardPalette.ClassicId, settings.ThemeId);
        Assert.True(settings.ThemeOverrides.ContainsKey("coveredFill"));
        Assert.False(settings.ThemeOverrides.ContainsKey("gridLine"));

        // 解析出来的调色板就是"经典 + 覆盖"
        BoardPalette palette = settings.ResolvePalette();
        Assert.Equal("#123456", palette.CoveredFill);
    }

    // ------------------------------------------------------------ 音效合成

    [Fact]
    public void 每个音效都生成合法且非静音的WAV()
    {
        foreach (SoundEffect effect in Enum.GetValues<SoundEffect>())
        {
            byte[] wav = WavSynth.CreateWav(effect);

            Assert.True(wav.Length > 44, $"{effect} 的 wav 太小。");
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
            Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));

            // RIFF 长度 = 文件长度 − 8；data 长度 = 文件长度 − 44
            Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
            Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));

            // 16-bit 单声道 PCM / 44.1kHz
            Assert.Equal(1, BitConverter.ToInt16(wav, 20));
            Assert.Equal(1, BitConverter.ToInt16(wav, 22));
            Assert.Equal(WavSynth.SampleRate, BitConverter.ToInt32(wav, 24));
            Assert.Equal(WavSynth.BitsPerSample, BitConverter.ToInt16(wav, 34));

            // 不能是静音：峰值要够大，且要有实际时长
            float peak = 0f;
            for (int i = 44; i + 1 < wav.Length; i += 2)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(wav, i) / (float)short.MaxValue));
            }

            Assert.True(peak > 0.1f, $"{effect} 几乎是静音（峰值 {peak:F3}）。");

            // 最短的是"翻开"这类提示音，最长的是启动旋律（约 570ms）
            Assert.InRange(WavSynth.DurationMs(effect), 20, 700);
        }
    }

    [Fact]
    public void 六个音效各不相同()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (SoundEffect effect in Enum.GetValues<SoundEffect>())
        {
            byte[] wav = WavSynth.CreateWav(effect);
            Assert.True(hashes.Add(Convert.ToHexString(wav)), $"{effect} 与其它音效完全相同。");
        }

        // v0.8 起多了"启动/换难度"的 Startup，共 6 个
        Assert.Equal(6, hashes.Count);
    }
}
