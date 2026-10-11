namespace MineSweeper.App;

/// <summary>
/// "关于游戏"页面里的文案。
/// 拆出来只是为了让 MainWindow 不再继续变胖；要改文案改这里就行。
/// </summary>
internal static class AboutText
{
    /// <summary>一句话简介。</summary>
    public const string Tagline = "一个从零手写的 Windows 扫雷：先把经典玩法 100% 还原，再往上加新的东西。";

    /// <summary>还原了什么 / 新加了什么。</summary>
    public const string Body =
        "还原的部分：左键翻开 · 右键三态（旗 → 问号 → 空）· 双击 / 中键 / 左右键同按 chord · " +
        "首点保证安全 · 计时从第一次翻开开始 · 剩余雷数 · 失败时展示全部雷位。\n" +
        "新加的部分：可调主题、代码合成音效、放大镜道具、关卡模式（3 章 18 关）、" +
        "缩放平移与远景降级、屏幕阅读器支持。";

    /// <summary>技术栈（对应文档 §1.2.1 的"零素材"路线）。</summary>
    public const string Tech =
        "技术栈：C# · .NET 10 · WinUI 3（Windows App SDK）· Win2D。\n" +
        "棋盘、数字、旗子、雷全部矢量自绘，音效由代码实时合成——" +
        "所以这个工程里没有一张第三方图片、也没有一个第三方音频文件。";

    /// <summary>规模数据。</summary>
    public const string Stats =
        "规模：单局最大 4096×4096；100 万格同屏渲染实测约 70 FPS；" +
        "核心逻辑有单元测试全量覆盖（零 UI 依赖）。";

    /// <summary>版权红线（文档 §7.3）。</summary>
    public const string RedLine = "本工程不含任何来自 Windows 7 扫雷的素材。";
}
