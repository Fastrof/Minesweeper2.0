namespace MineSweeper.Core.Items;

/// <summary>一次"正确标记"或"领奖"带来的发放结果（§5.2）。</summary>
public readonly record struct ItemGrantResult(bool Granted, bool InventoryFull, int CountAfter)
{
    /// <summary>是否什么都没发生。</summary>
    public bool NoChange => !Granted && !InventoryFull;
}

/// <summary>
/// 物品栏（§1.3 / §5.2）：三种道具的持有量、奖励发放、上限控制。
/// 刻意只依赖 <see cref="PlayerProgress"/> 与 <see cref="ItemConfig"/>，
/// 因此发放规则可以在 Core 单测里直接验证（§8 第 7、8 条）。
///
/// <b>v0.6 起发放规则改了</b>：每累计 <see cref="RewardThreshold"/> 次"正确标记"
/// （= 扫到一颗雷）不再自动发一个放大镜，而是**攒下一次"三选一"奖励**
/// （<see cref="PendingRewards"/>）；由 UI 弹出"从三个道具里挑一个"，
/// 玩家选完才真正进物品栏（<see cref="ClaimReward"/>）。
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

    /// <summary>奖励阈值：每扫到这么多颗雷给一次"三选一"。</summary>
    public int RewardThreshold => Config.MagnifierThreshold;

    /// <summary>奖励阈值（旧名，保留兼容）。</summary>
    public int MagnifierThreshold => RewardThreshold;

    /// <summary>单种道具的持有上限（§5.2 的三格）。</summary>
    public int MaxHold => Config.MagnifierMaxHold;

    /// <summary>当前持有的放大镜数量。</summary>
    public int MagnifierCount => Progress.MagnifierCount;

    /// <summary>当前持有的声呐扫描数量。</summary>
    public int SonarCount => Progress.SonarCount;

    /// <summary>当前持有的时间冻结数量。</summary>
    public int FreezeCount => Progress.FreezeCount;

    /// <summary>还没领的"三选一"奖励次数（UI 据此弹窗）。</summary>
    public int PendingRewards => Progress.PendingRewards;

    /// <summary>距离下一次奖励还差几次正确标记（§5.2 的进度条 13/20）。</summary>
    public int CorrectFlagsTowardNext
        => Progress.CorrectMineFlagsTotal % RewardThreshold;

    /// <summary>某种道具的持有量。</summary>
    public int CountOf(string itemId) => itemId switch
    {
        ItemIds.Magnifier => Progress.MagnifierCount,
        ItemIds.Sonar => Progress.SonarCount,
        ItemIds.Freeze => Progress.FreezeCount,
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
    /// 记录一次"正确标记"（§5.2 的计数事件）：只累计计数；
    /// 跨阈值时攒下一次待领奖励，交给 UI 弹"三选一"。
    /// <paramref name="allowGrant"/> 为 false 时连奖励也不给（纯经典模式，§5.5 ⑤）。
    /// </summary>
    /// <param name="choiceRewards">
    /// true（关卡模式）= 攒一次"三选一"奖励，玩家自己挑；
    /// false（普通模式）= 老行为，跨阈值直接发一个放大镜。
    /// </param>
    /// <returns>这一次是否刚好产生了一次奖励（攒下待领 / 直接发放）。</returns>
    public bool RecordCorrectFlag(bool allowGrant, bool choiceRewards)
    {
        Progress.CorrectMineFlagsTotal++;

        if (!allowGrant || Progress.CorrectMineFlagsTotal % RewardThreshold != 0)
        {
            return false;
        }

        if (choiceRewards)
        {
            Progress.PendingRewards++;
            return true;
        }

        // 普通模式：满仓就不再发（与 v0.5 行为一致）
        return TryGrantMagnifier().Granted;
    }

    /// <summary>
    /// 领取一次"三选一"奖励：把选中的道具放进物品栏，并扣掉一次待领奖励。
    /// 选中的道具已满时不扣奖励（UI 会把满的那格禁用，这里只是兜底）。
    /// </summary>
    public ItemGrantResult ClaimReward(string itemId)
    {
        if (!ItemIds.All.Contains(itemId) || Progress.PendingRewards <= 0)
        {
            return new ItemGrantResult(false, false, CountOf(itemId));
        }

        if (IsFull(itemId))
        {
            return new ItemGrantResult(false, true, CountOf(itemId));
        }

        Progress.PendingRewards--;
        AddOne(itemId);

        return new ItemGrantResult(true, IsFull(itemId), CountOf(itemId));
    }

    /// <summary>直接尝试发放一个放大镜（首赠等旧路径用；满仓则失败并标记 InventoryFull）。</summary>
    public ItemGrantResult TryGrantMagnifier()
    {
        if (IsFull(ItemIds.Magnifier))
        {
            return new ItemGrantResult(false, true, Progress.MagnifierCount);
        }

        AddOne(ItemIds.Magnifier);

        return new ItemGrantResult(true, IsFull(ItemIds.Magnifier), Progress.MagnifierCount);
    }

    /// <summary>消耗一个指定道具。数量为 0 时返回 false。</summary>
    public bool TryConsume(string itemId)
    {
        switch (itemId)
        {
            case ItemIds.Magnifier when Progress.MagnifierCount > 0:
                Progress.MagnifierCount--;
                return true;
            case ItemIds.Sonar when Progress.SonarCount > 0:
                Progress.SonarCount--;
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