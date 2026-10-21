namespace MineSweeper.Core.Items;

/// <summary>一次"正确标记"或"领奖"带来的发放结果（§5.2）。</summary>
public readonly record struct ItemGrantResult(bool Granted, bool InventoryFull, int CountAfter)
{
    /// <summary>是否什么都没发生。</summary>
    public bool NoChange => !Granted && !InventoryFull;
}

/// <summary>
/// 物品栏（§1.3 / §5.2）：三种道具的持有量、商店购买、上限控制。
/// 刻意只依赖 <see cref="PlayerProgress"/> 与 <see cref="ItemConfig"/>，
/// 因此发放规则可以在 Core 单测里直接验证（§8 第 7、8 条）。
///
/// <b>v1.4 起发放规则</b>：不再有"每 20 颗雷三选一"的自动补给，
/// 道具改为用金币在商店里买（<see cref="GameSession.TryBuyItem"/>）。
/// 正确插旗仍会累计 <see cref="PlayerProgress.CorrectMineFlagsTotal"/>（历史/成就用），
/// 但不会直接发道具；<see cref="RewardThreshold"/> 与 <see cref="CorrectFlagsTowardNext"/>
/// 只为兼容旧调用保持可读，UI 已不再展示。
/// </summary>
public sealed class ItemInventory
{
    public ItemInventory(PlayerProgress progress, ItemConfig config)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(config);

        config.Normalize();
        Progress = progress;
        Config = config;
    }

    /// <summary>底层进度（会被写进 progress.json）。</summary>
    public PlayerProgress Progress { get; }

    /// <summary>道具配置。</summary>
    public ItemConfig Config { get; }

    /// <summary>历史奖励阈值（v1.4 起不再发放，仅保留给统计展示/旧测试）。</summary>
    public int RewardThreshold => Config.MagnifierThreshold;

    /// <summary>奖励阈值（旧名，保留兼容）。</summary>
    public int MagnifierThreshold => RewardThreshold;

    /// <summary>单种道具的持有上限（§5.2 的三格）。</summary>
    public int MaxHold => Config.MagnifierMaxHold;

    /// <summary>
    /// 本局通过**商店**买到的数量（v1.4）。
    ///
    /// 语义是"账号库存之外、只在本局有效的额外持有量"：
    /// <list type="bullet">
    /// <item>显示/可用数量 = 账号库存 + 本局购买；</item>
    /// <item>消耗时**优先扣本局买的**（用掉就没了，不会"退回"账号库存）；</item>
    /// <item>每局 / 每关开始时清零（<see cref="ResetSession"/>），因为需求要求
    ///       "金币每局清零、买到的道具也跟着这一局走"。</item>
    /// </list>
    /// 这样既能满足"商店是每局经济"，又不会把玩家原来攒下的道具弄丢。
    /// </summary>
    public PlayerProgress SessionPurchases { get; private set; } = PlayerProgress.CreateDefault();

    /// <summary>新局 / 新关卡开始：清空本局购买（金币在 <see cref="GameSession.Coins"/> 上另行清零）。</summary>
    public void ResetSession() => SessionPurchases = PlayerProgress.CreateDefault();

    /// <summary>本局通过商店买到的某个道具数量。</summary>
    public int PurchasedCountOf(string itemId) => itemId switch
    {
        ItemIds.Magnifier => SessionPurchases.MagnifierCount,
        ItemIds.Sonar => SessionPurchases.SonarCount,
        ItemIds.Freeze => SessionPurchases.FreezeCount,
        _ => 0,
    };

    /// <summary>当前持有的放大镜数量（账号库存 + 本局购买）。</summary>
    public int MagnifierCount => Progress.MagnifierCount + SessionPurchases.MagnifierCount;

    /// <summary>当前持有的声呐扫描数量（账号库存 + 本局购买）。</summary>
    public int SonarCount => Progress.SonarCount + SessionPurchases.SonarCount;

    /// <summary>当前持有的时间冻结数量（账号库存 + 本局购买）。</summary>
    public int FreezeCount => Progress.FreezeCount + SessionPurchases.FreezeCount;

    /// <summary>距离下一次奖励还差几次正确标记（v1.4 起奖励机制已移除，仅保留给统计展示）。</summary>
    public int CorrectFlagsTowardNext
        => Progress.CorrectMineFlagsTotal % Math.Max(1, RewardThreshold);

    /// <summary>某种道具的持有量。</summary>
    public int CountOf(string itemId) => itemId switch
    {
        ItemIds.Magnifier => MagnifierCount,
        ItemIds.Sonar => SonarCount,
        ItemIds.Freeze => FreezeCount,
        _ => 0,
    };

    /// <summary>某种道具是否已达持有上限。</summary>
    public bool IsFull(string itemId) => CountOf(itemId) >= MaxHold;

    /// <summary>三种道具里是否还有能装的（用来判断"这次奖励有的选"）。</summary>
    public bool HasRoomForAny => ItemIds.All.Any(id => !IsFull(id));

    /// <summary>放大镜是否已满（保留旧 API）。</summary>
    public bool IsMagnifierFull => IsFull(ItemIds.Magnifier);

    /// <summary>
    /// 首次进入游戏赠送：**三种道具各送 2 个**（v0.6 起；之前只送放大镜）。
    /// 只送一次（`initialGiftGranted` 落盘），返回是否真的发了。
    /// </summary>
    public bool EnsureInitialGift()
    {
        if (Progress.InitialGiftGranted)
        {
            return false;
        }

        (string Id, int Amount)[] gifts =
        {
            (ItemIds.Magnifier, Config.MagnifierInitialGift),
            (ItemIds.Sonar, Config.SonarInitialGift),
            (ItemIds.Freeze, Config.FreezeInitialGift),
        };

        if (gifts.All(g => g.Amount <= 0))
        {
            return false;
        }

        Progress.InitialGiftGranted = true;

        foreach ((string id, int amount) in gifts)
        {
            if (amount <= 0)
            {
                continue;
            }

            for (int i = 0; i < amount && !IsFull(id); i++)
            {
                AddOne(id);
            }
        }

        return true;
    }

    /// <summary>
    /// 记录一次"正确标记"（= 扫到一颗雷）。
    ///
    /// <b>v1.4 起只做统计累计，不再发放任何道具</b> —— 道具改由金币商店购买
    /// （见 <see cref="GameSession.TryBuyItem"/>）。保留这个方法是为了让历史统计与成就继续工作。
    /// </summary>
    public void RecordCorrectFlag() => Progress.CorrectMineFlagsTotal++;

    /// <summary>
    /// 商店购买成功后把道具记进"本局购买"（v1.4）。
    /// 只写 <see cref="SessionPurchases"/>，**不动账号库存** —— 所以它必然随局结束/新关卡清零。
    /// </summary>
    public ItemGrantResult ClaimPurchase(string itemId)
    {
        if (!ItemIds.All.Contains(itemId))
        {
            return new ItemGrantResult(false, false, 0);
        }

        switch (itemId)
        {
            case ItemIds.Magnifier:
                SessionPurchases.MagnifierCount++;
                break;
            case ItemIds.Sonar:
                SessionPurchases.SonarCount++;
                break;
            case ItemIds.Freeze:
                SessionPurchases.FreezeCount++;
                break;
            default:
                return new ItemGrantResult(false, false, 0);
        }

        return new ItemGrantResult(true, IsFull(itemId), CountOf(itemId));
    }

    /// <summary>直接尝试发放一个放大镜（首赠等旧路径用；满仓则失败并标记 InventoryFull）。</summary>
    public ItemGrantResult TryGrantMagnifier()
    {
        if (IsFull(ItemIds.Magnifier))
        {
            return new ItemGrantResult(false, true, MagnifierCount);
        }

        AddOne(ItemIds.Magnifier);

        return new ItemGrantResult(true, IsFull(ItemIds.Magnifier), MagnifierCount);
    }

    /// <summary>
    /// 消耗一个指定道具。数量为 0 时返回 false。
    ///
    /// <b>v1.4 起优先扣本局商店买的那份</b>：买来的用掉就走，不会"退回"成账号库存；
    /// 本局没买过（或已经用完）才去扣账号库存。玩家看到的总数 = 两者之和，所以
    /// "优先扣哪一份"对玩家不可见，只影响"这一局结束后还剩多少"。
    /// </summary>
    public bool TryConsume(string itemId)
    {
        switch (itemId)
        {
            case ItemIds.Magnifier when SessionPurchases.MagnifierCount > 0:
                SessionPurchases.MagnifierCount--;
                return true;
            case ItemIds.Magnifier when Progress.MagnifierCount > 0:
                Progress.MagnifierCount--;
                return true;

            case ItemIds.Sonar when SessionPurchases.SonarCount > 0:
                SessionPurchases.SonarCount--;
                return true;
            case ItemIds.Sonar when Progress.SonarCount > 0:
                Progress.SonarCount--;
                return true;

            case ItemIds.Freeze when SessionPurchases.FreezeCount > 0:
                SessionPurchases.FreezeCount--;
                return true;
            case ItemIds.Freeze when Progress.FreezeCount > 0:
                Progress.FreezeCount--;
                return true;

            default:
                return false;
        }
    }

    /// <summary>消耗一个放大镜（保留旧 API，内部转调 <see cref="TryConsume"/>）。</summary>
    public bool TryConsumeMagnifier() => TryConsume(ItemIds.Magnifier);

    private void AddOne(string itemId)
    {
        switch (itemId)
        {
            case ItemIds.Magnifier:
                Progress.MagnifierCount++;
                Progress.MagnifierGranted++;
                break;
            case ItemIds.Sonar:
                Progress.SonarCount++;
                break;
            case ItemIds.Freeze:
                Progress.FreezeCount++;
                break;
        }
    }
}