namespace MineSweeper.Core.Updates;

/// <summary>一个版本的"本版新增"条目。</summary>
public sealed record WhatsNewEntry(string Version, string Title, IReadOnlyList<string> Highlights);

/// <summary>
/// 内置的"本版新增"（v1.2）。
///
/// <b>内容内置、不联网</b>：这样升级后无论有没有网、GitHub 通不通，玩家都看得到这一版改了什么。
/// 发新版本时只需在这张表**最前面加一段**，并把 <c>AboutInfo.Version</c> 同步改掉。
/// </summary>
public static class WhatsNewCatalog
{
    /// <summary>全部条目，新的在最前面。</summary>
        /// <summary>v1.3：当前语言下的版本说明列表。
    /// 每次都按<b>当前</b>语言翻译，所以切换语言后立刻生效，也不受静态初始化时机影响。</summary>
    public static IReadOnlyList<WhatsNewEntry> All => Raw
        .Select(e => e with
        {
            Title = MineSweeper.Core.Localization.Strings.TranslateLiteral(e.Title),
            Highlights = e.Highlights.Select(MineSweeper.Core.Localization.Strings.TranslateLiteral).ToArray(),
        })
        .ToArray();

    private static readonly IReadOnlyList<WhatsNewEntry> Raw =
    [
    new("1.5", "v1.5：无猜模式，告别赌运气", [
        "设置里新增「无猜模式」：每张盘都保证能靠纯逻辑推完，不用猜二选一。",
        "发牌前用推理器验证（单格 / 双格 / 全局雷数规则），最多重抽 2000 张；同种子同首点仍是同一张盘。",
        "开关会被记住（settings.json 的 noGuess），下一局开始生效。",
        "超过 1 万格或雷太密抽不到时退回普通盘，并在开局时提示。",
        "分享串支持 7 节格式（带无猜标志），旧 5 / 6 节仍然兼容。",
        "关卡模式保持固定盘面，不受无猜开关影响。",
        "普通局新增「💡 提示」按钮：高亮一个确定安全的格子；提示局不计最快记录，无猜/关卡局不显示。",
        "官网发布页视觉重做：纸色格线、凸起按钮、商店徽章。",
    ]),
    new("1.4", "v1.4：金币商店，道具自己换", [
        "棋盘右上角新增「🛒 商店」入口，扫雷过程中随时能点开、随时能退出继续扫。",
        "每扫出一颗雷得 1 金币；放大镜 30 金币、声呐扫描 20、时间冻结 20。",
        "金币每局重新开始时清零；闯关模式每到新的一关也清零。",
        "金币买到的道具只在本局有效，不会带进下一局。",
        "放大镜削弱：透视范围从 3×3 改成 2×2（点中的格子是左上角）。",
        "原来的「每 20 颗雷送一次三选一」取消，改由金币商店自己换。",
    ]),
    new("1.3", "v1.3：中英双语与界面颜色主题", [
        "界面与全部文案支持简体中文与 English 实时切换，首次安装默认简体中文。",
        "新增界面颜色主题：跟随系统 / 浅色 / 深色，切换即时生效并会记住。",
        "深色界面下保证棋盘与背景分得开（自动调整棋盘底色，高对比主题同样适用）。",
        "关卡、道具、成就、检查更新等界面的文案全部双语化。",
    ]),
        new("1.2", "v1.2：更新更听你的话", [
            "自动检查更新可设频率：每次启动 / 每天 / 每 3 天 / 每周 / 每 2 周 / 每月。",
            "可以暂停更新，并用日历选到哪天为止；暂停期间一次请求都不发。",
            "暂停期间你手动点「检查更新」= 立即恢复自动检查（说明你想看更新了）。",
            "升级后第一次打开会自动展示「本版新增」，设置里也能随时回看。",
            "移除「锁定盘面」开关：以后点哪一格就在哪一格展开。",
            "移除「远景雷密度热力图」：它会把盘面信息直接暴露出来，与 100% 还原经典冲突。",
        ]),
        new("1.1", "v1.1：成就与检查更新", [
            "新增 31 条成就、共 1000 点，其中 2 条隐藏成就，底部「🏆 成就」可查看。",
            "新增检查更新：启动时静默检查 + 设置里手动检查，并可自动下载安装包。",
            "踩雷弹窗拆成「重打这一盘」（同一张盘，练习不计最快记录）与「换一张新盘」。",
            "修复锁定盘面下第一下点到安全格却开在别处的问题。",
        ]),
        new("1.0", "v1.0：首个公开发布版", [
            "经典模式 100% 还原：首点保证安全、右键三态、双击 chord、经典配色与音效。",
            "6 章 36 关的关卡模式，带星级与最快用时。",
            "三种道具（放大镜 / 声呐 / 时间冻结）与每 20 颗雷一次的补给。",
            "19 个颜色键、4 套内置主题，音效由代码合成，不随工程分发任何素材。",
        ]),
    ];

    /// <summary>按版本号找条目（大小写/前缀不敏感，如 "v1.2" 也能找到）。</summary>
    public static WhatsNewEntry? Find(string? version)
    {
        if (!AppVersion.TryParse(version, out Version target))
        {
            return null;
        }

        foreach (WhatsNewEntry entry in All)
        {
            if (AppVersion.TryParse(entry.Version, out Version candidate) && candidate == target)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// 该不该弹「本版新增」。
    /// <list type="bullet">
    /// <item><b>第一次安装不弹</b>（<paramref name="lastSeenVersion"/> 为空）：新玩家一打开就被糊一脸不合适。</item>
    /// <item>版本号比上次运行的高才弹（降级/重装同版本都不弹）。</item>
    /// <item>版本号认不出来一律不弹（宁可少弹，也不要误弹）。</item>
    /// </list>
    /// </summary>
    public static bool ShouldShow(string? lastSeenVersion, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(lastSeenVersion))
        {
            return false;
        }

        if (!AppVersion.TryParse(lastSeenVersion, out Version last) ||
            !AppVersion.TryParse(currentVersion, out Version current))
        {
            return false;
        }

        return current > last;
    }
}
