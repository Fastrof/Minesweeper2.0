using Avalonia.Automation;
using Avalonia.Automation.Peers;
using MineSweeper.Core;

namespace MineSweeper.App.Linux.Controls;

/// <summary>
/// 棋盘的 UI Automation peer（Linux 上对应 AT-SPI2）。
///
/// 与 Windows 端的分工一致：**只读**——屏幕阅读器能读出"当前是什么局面"，
/// 但不提供 Invoke 等"不用鼠标就能操作"的路径（项目所有者明确要求纯鼠标操作）。
///
/// 当前实现到"棋盘级"：Name 是整盘状态摘要，ControlType = Pane。
/// 逐格的虚拟子节点（Windows 端最多 400 格）留作后续增强。
/// </summary>
public sealed class BoardAutomationPeer : ControlAutomationPeer
{
    private readonly BoardControl _owner;

    public BoardAutomationPeer(BoardControl owner)
        : base(owner) => _owner = owner;

    protected override string GetClassNameCore() => "MineSweeperBoard";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

    protected override string GetNameCore() => _owner.BuildAccessibleName();

    protected override string GetHelpTextCore()
        => "扫雷棋盘。左键翻开，右键插旗，双击已翻开的数字格批量展开。";

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;
}
