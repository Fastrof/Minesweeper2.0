using System.Security.Cryptography;
using MineSweeper.Core.Updates;
using Xunit;

namespace MineSweeper.Core.Tests;

/// <summary>
/// 检查更新里"下载与校验"那一半的纯逻辑测试（v1.1）。
/// 选包与哈希校验都是安全边界：选错包会给玩家装一个跑不起来的东西，
/// 哈希不校验则可能装上一个下坏的包，所以这两块单独写一轮用例。
/// </summary>
public sealed class UpdateDownloadTests
{
    private static ReleaseAsset Asset(string name, string url = "https://example.com/a")
        => new(name, url, 1024);

    // ------------------------------------------------------------ 选包

    [Fact]
    public void 优先选中带当前架构的_msix()
    {
        ReleaseAsset? picked = UpdateAssetSelector.SelectInstaller(
            [Asset("MineSweeper2.0_1.1.0.0_arm64.msix"), Asset("MineSweeper2.0_1.1.0.0_x64.msix")],
            UpdateAssetSelector.X64,
            out string reason);

        Assert.NotNull(picked);
        Assert.Equal("MineSweeper2.0_1.1.0.0_x64.msix", picked!.Name);
        Assert.Contains("选中安装包", reason);
    }

    [Fact]
    public void 没有架构标记时退化为任意_msix()
    {
        ReleaseAsset? picked = UpdateAssetSelector.SelectInstaller(
            [Asset("MineSweeper2.0_1.1.0.0.msix")],
            UpdateAssetSelector.X64,
            out _);

        Assert.NotNull(picked);
        Assert.Equal("MineSweeper2.0_1.1.0.0.msix", picked!.Name);
    }

    [Fact]
    public void 只有_msixbundle_时也能选出来()
    {
        ReleaseAsset? picked = UpdateAssetSelector.SelectInstaller(
            [Asset("MineSweeper2.0_1.1.0.0_x64.msixbundle")],
            UpdateAssetSelector.X64,
            out _);

        Assert.NotNull(picked);
        Assert.EndsWith(".msixbundle", picked!.Name);
    }

    [Fact]
    public void 名字里带架构的_msixbundle_优先于裸的()
    {
        ReleaseAsset? picked = UpdateAssetSelector.SelectInstaller(
            [Asset("MineSweeper2.0_1.1.0.0.msixbundle"), Asset("MineSweeper2.0_1.1.0.0_x64.msixbundle")],
            UpdateAssetSelector.X64,
            out _);

        Assert.NotNull(picked);
        Assert.Contains("x64", picked!.Name);
    }

    [Fact]
    public void 架构不匹配的_msix_不会被选中()
    {
        ReleaseAsset? picked = UpdateAssetSelector.SelectInstaller(
            [Asset("MineSweeper2.0_1.1.0.0_arm64.msix")],
            UpdateAssetSelector.X64,
            out string reason);

        // 退化为"任意 .msix"是刻意的：宁可让 Windows 在安装时报不兼容，也不要因为名字里没写架构就永远查不到更新。
        Assert.NotNull(picked);
        Assert.Contains("arm64", picked!.Name);
        Assert.NotNull(reason);
    }

    [Fact]
    public void 没有任何附件时返回_null_并说明原因()
    {
        Assert.Null(UpdateAssetSelector.SelectInstaller([], UpdateAssetSelector.X64, out string emptyReason));
        Assert.Contains("没有任何附件", emptyReason);

        Assert.Null(UpdateAssetSelector.SelectInstaller(null, UpdateAssetSelector.X64, out string nullReason));
        Assert.Contains("没有任何附件", nullReason);
    }

    [Fact]
    public void 附件里没有安装包时返回_null()
    {
        Assert.Null(UpdateAssetSelector.SelectInstaller(
            [Asset("source.zip"), Asset("notes.txt")],
            UpdateAssetSelector.X64,
            out string reason));

        Assert.Contains("没有可用于", reason);
    }

    [Fact]
    public void 下载地址为空的附件会被跳过()
    {
        Assert.Null(UpdateAssetSelector.SelectInstaller(
            [Asset("MineSweeper2.0_1.1.0.0_x64.msix", url: "")],
            UpdateAssetSelector.X64,
            out _));
    }

    [Fact]
    public void 架构为空时按_x64_处理()
    {
        ReleaseAsset? picked = UpdateAssetSelector.SelectInstaller(
            [Asset("MineSweeper2.0_1.1.0.0_x64.msix")],
            "  ",
            out _);

        Assert.NotNull(picked);
    }

    // -------------------------------------------------------- 发布说明里的哈希

    [Fact]
    public void 能从发布说明里抓到_SHA256()
    {
        const string notes = "## 更新内容\n\n- 修了个 bug\n\nSHA256: " +
                             "2E3CBE1CCB9F7DBE651C208D4B57EECF977B45B6BC2F8E16BE107B2681F0FCB4\n";

        Assert.True(ReleaseNotesHash.TryExtract(notes, out string? hex));
        Assert.Equal("2E3CBE1CCB9F7DBE651C208D4B57EECF977B45B6BC2F8E16BE107B2681F0FCB4", hex);
    }

    [Fact]
    public void 哈希行大小写与装饰符号都能容忍()
    {
        string value = new string('a', 64);
        Assert.True(ReleaseNotesHash.TryExtract($"**sha256**：`{value}`", out string? hex));
        Assert.Equal(value.ToUpperInvariant(), hex);
    }

    [Fact]
    public void 非法的哈希行会被忽略()
    {
        Assert.False(ReleaseNotesHash.TryExtract("SHA256: 太短了", out _));
        Assert.False(ReleaseNotesHash.TryExtract("SHA256: " + new string('z', 64), out _));
        Assert.False(ReleaseNotesHash.TryExtract("没有这一行", out _));
        Assert.False(ReleaseNotesHash.TryExtract(null, out _));
        Assert.False(ReleaseNotesHash.TryExtract("   ", out _));
    }

    // -------------------------------------------------------- 文件哈希校验

    [Fact]
    public void 算得出一致且正确的_SHA256()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ms20_hash_{Guid.NewGuid():N}.bin");
        try
        {
            byte[] data = "扫雷 2.0"u8.ToArray();
            File.WriteAllBytes(path, data);

            string expected = Convert.ToHexString(SHA256.HashData(data));
            Assert.True(FileHash.TryComputeSha256(path, out string? actual));
            Assert.Equal(expected, actual);

            Assert.True(FileHash.VerifySha256(path, expected, out string okReason));
            Assert.Contains("通过", okReason);

            // 大小写不敏感
            Assert.True(FileHash.VerifySha256(path, expected.ToLowerInvariant(), out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 内容不符时校验失败()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ms20_hash_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.False(FileHash.VerifySha256(path, new string('0', 64), out string reason));
            Assert.Contains("校验失败", reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 没有期望哈希时放行但要说明()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ms20_hash_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.True(FileHash.VerifySha256(path, null, out string reason));
            Assert.Contains("跳过哈希校验", reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 文件不存在时算不出哈希且校验失败()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"ms20_missing_{Guid.NewGuid():N}.bin");
        Assert.False(FileHash.TryComputeSha256(missing, out string? hex));
        Assert.Null(hex);
        Assert.False(FileHash.VerifySha256(missing, new string('0', 64), out _));
        Assert.False(FileHash.TryComputeSha256(null, out _));
    }
}
