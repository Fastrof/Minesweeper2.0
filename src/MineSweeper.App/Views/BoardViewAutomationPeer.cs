using Microsoft.UI.Xaml.Automation.Peers;

namespace MineSweeper.App.Views;

/// <summary>
/// 自绘棋盘的 UI Automation peer（§1.4 / §3.2 / M4）。
///
/// 方案 A（单一 Win2D 路径）的既定代价是"失去控件自带的无障碍语义树"，文档要求补上它：
/// 屏幕阅读器应当能读出"第 3 行第 5 列，已翻开，数字 2"这类信息。
///
/// 做法：
/// <list type="bullet">
/// <item>棋盘本身是一个 <see cref="AutomationControlType.Pane"/>，Name 里带盘面摘要
/// （尺寸 + 剩余雷数 + 胜负状态）。键盘操作已按项目所有者要求整个移除，
/// 所以这里<b>没有</b>焦点格、也没有 LiveRegion 播报。</item>
/// <item>子节点是该自绘棋盘的"虚拟格"（<see cref="BoardCellAutomationPeer"/>），
/// 只为<b>可见区域</b>生成并设上限，避免 100 万格棋盘把无障碍树撑爆。</item>
/// </list>
/// </summary>
internal sealed class BoardViewAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly BoardView _view;

    internal BoardViewAutomationPeer(BoardView view)
        : base(view)
        => _view = view;

    protected override string GetClassNameCore() => "MineSweeperBoard";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

    protected override string GetNameCore() => _view.BuildBoardAutomationName();

    protected override string GetHelpTextCore() => _view.BuildBoardAutomationHelpText();

    protected override bool IsEnabledCore() => true;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override List<AutomationPeer>? GetChildrenCore() => _view.BuildCellAutomationPeers();
}
