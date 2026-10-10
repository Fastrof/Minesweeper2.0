using Microsoft.UI.Xaml.Automation.Peers;
using Windows.Foundation;

namespace MineSweeper.App.Views;

/// <summary>
/// 棋盘的"虚拟格"无障碍节点（§3.2）。
///
/// 棋盘是 Win2D 自绘的，没有真实控件，所以这里用 <see cref="FrameworkElementAutomationPeer"/>
/// （Owner 传棋盘本身）来省掉 <see cref="AutomationPeer"/> 的一堆抽象成员，
/// 只覆写"这一格念什么、边界在哪"——**刻意不提供 Invoke / 聚焦模式**：棋盘的唯一操作方式是鼠标（见 README §6.1）。
/// </summary>
internal sealed class BoardCellAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly BoardView _view;
    private readonly int _index;

    internal BoardCellAutomationPeer(BoardView view, int index)
        : base(view)
    {
        _view = view;
        _index = index;
    }

    /// <summary>这一格在棋盘里的索引（调试与测试用）。</summary>
    internal int CellIndex => _index;

    protected override string GetClassNameCore() => "MineSweeperCell";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string GetLocalizedControlTypeCore() => "格子";

    /// <summary>屏幕阅读器念的就是这句："第3行 第5列，已翻开，数字 2"。</summary>
    protected override string GetNameCore() => _view.DescribeCellForAutomation(_index);

    protected override string GetHelpTextCore() => "鼠标左键翻开该格，右键插旗。";

    protected override string GetItemTypeCore() => "扫雷格子";

    protected override string GetAutomationIdCore() => $"cell_{_index}";

    protected override bool IsEnabledCore() => _view.IsGameActiveForAutomation;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override Rect GetBoundingRectangleCore() => _view.GetCellAutomationBounds(_index);

    protected override Point GetClickablePointCore()
    {
        Rect bounds = GetBoundingRectangleCore();
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }


}
