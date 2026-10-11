namespace MineSweeper.App;

/// <summary>
/// "关于游戏 / 关于作者"页面上显示的元信息。
///
/// 集中放在这里是为了<b>只改一处</b>：上传到 GitHub 之后，把
/// <see cref="RepositoryUrl"/> 填上地址即可，页面会自动显示出来。
/// </summary>
internal static class AboutInfo
{
    /// <summary>产品名。</summary>
    public const string ProductName = "扫雷 2.0";

    /// <summary>
    /// 版本号。与 README §0.7 的 v1.0 及 <c>MineSweeper.App.csproj</c> 的 <c>&lt;Version&gt;</c>
    /// <b>三处必须一起改</b>；留两位（1.0 而不是 1.0.0），关于页面显示的更干净。
    /// </summary>
    public const string Version = "1.0";

    /// <summary>主开发。</summary>
    public const string PrimaryAuthor = "Fastrof";

    /// <summary>第二开发。</summary>
    public const string SecondaryAuthor = "Shinichi";

    /// <summary>作者身份说明（放在"关于作者"页底部）。</summary>
    public const string AuthorsNote = "Fastrof 与 Shinichi 都是计算机专业在读的大二学生。";

    /// <summary>
    /// 开源地址。仓库已经公开（v1.0.0），页面会直接显示这个链接。
    /// </summary>
    public const string RepositoryUrl = "https://github.com/Fastrof/Minesweeper2.0";

    /// <summary>许可说明（只有一行，避免长文）。</summary>
    public const string License = "仅供学习与交流使用";

    /// <summary>开源地址的展示文案。</summary>
    public static string RepositoryText =>
        string.IsNullOrWhiteSpace(RepositoryUrl) ? "即将公开" : RepositoryUrl;
}
