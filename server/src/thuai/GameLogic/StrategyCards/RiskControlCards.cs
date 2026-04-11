namespace Thuai.GameLogic.StrategyCards;

/// <summary>
/// 免流协议 — First 100,000 Mora in fees exempt per trading day.
/// Trade-off: normal fee rate becomes 0.2% (10x the default 0.02%).
/// The match engine should call CalculateEffectiveFee() instead of applying the raw fee.
/// </summary>
public class FeeExemption : StrategyCard
{
    public override string Name => "免流协议";
    public override CardCategory Category => CardCategory.RiskControl;
    public override string Description => "每个交易日豁免前100,000摩拉的交易手续费，正常交易手续费率为0.2%";

    public long ExemptedAmount { get; set; }
    public long MaxExemption => 100_000;

    public override void OnAcquire(Player player)
    {
        player.TransactionFeeRate = 0.002; // 0.2%
    }

    /// <summary>
    /// Calculates the actual fee after applying the exemption.
    /// Called by the match engine for each trade.
    /// </summary>
    public long CalculateEffectiveFee(long rawFee)
    {
        if (ExemptedAmount >= MaxExemption)
            return rawFee;

        long canExempt = Math.Min(rawFee, MaxExemption - ExemptedAmount);
        ExemptedAmount += canExempt;
        return rawFee - canExempt;
    }

    public void ResetDaily()
    {
        ExemptedAmount = 0;
    }
}

/// <summary>
/// 冰山订单 — Player's orders show only 10% quantity in the public order book.
/// The Order class already supports IsIceberg / VisibleQuantity.
/// TradingDay should check: player.ActiveCards.Any(c => c is IcebergOrder)
/// and set isIceberg=true when creating orders for this player.
/// </summary>
public class IcebergOrder : StrategyCard
{
    public override string Name => "冰山订单";
    public override CardCategory Category => CardCategory.RiskControl;
    public override string Description => "你的挂单在公开订单簿中只显示10%的数量";
}

/// <summary>
/// 止损名刀 — When NAV first drops below 80% of initial, auto-cancel all pending orders
/// and become immune to NAV loss for 20 ticks.
/// TradingDay should call ShouldTrigger() each tick and invoke Trigger() when true.
/// Resets each trading day.
/// </summary>
public class StopLossBlade : StrategyCard
{
    public override string Name => "止损名刀";
    public override CardCategory Category => CardCategory.RiskControl;
    public override string Description => "总净值首次跌破初始资金的80%时自动触发，撤销所有挂单并免疫20帧内的净值亏损";

    public bool HasTriggered { get; private set; }
    public int ImmuneUntilTick { get; private set; } = -1;

    private const double TriggerThreshold = 0.8;
    private const int ImmuneDuration = 20;

    public bool ShouldTrigger(long currentNAV, long initialNAV)
    {
        return !HasTriggered && currentNAV < (long)(initialNAV * TriggerThreshold);
    }

    /// <summary>
    /// Triggers the stop-loss. The caller (TradingDay) is responsible for
    /// cancelling all of this player's pending orders in the OrderBook.
    /// </summary>
    public void Trigger(Player player, int currentTick)
    {
        HasTriggered = true;
        ImmuneUntilTick = currentTick + ImmuneDuration;
        player.IsImmune = true;
        player.ImmuneUntilTick = ImmuneUntilTick;
    }

    public override void OnTick(Player player, int currentTick)
    {
        base.OnTick(player, currentTick);

        if (HasTriggered && currentTick >= ImmuneUntilTick && player.IsImmune)
        {
            player.IsImmune = false;
        }
    }

    public void ResetDaily()
    {
        HasTriggered = false;
        ImmuneUntilTick = -1;
    }
}

/// <summary>
/// 定向增发 — Active skill. Buy 500 gold at 2% below current buy-1 price,
/// directly from the system (bypasses order book). Gold locked for 300 ticks.
/// One-shot per trading day.
/// The actual purchase logic is handled by TradingDay since it needs OrderBook access.
/// </summary>
public class TargetedPurchase : StrategyCard
{
    public override string Name => "定向增发";
    public override CardCategory Category => CardCategory.RiskControl;
    public override string Description => "以买一价2%折扣直接购买500单位黄金，锁定300帧";
    public override bool IsPassive => false;
    public override int Cooldown => 0;

    private bool _used;

    public bool IsUsed => _used;
    public int LockDuration => 300;
    public int PurchaseQuantity => 500;
    public double DiscountRate => 0.02;

    public override void OnActivate(Player player, int currentTick)
    {
        if (_used) return;

        _used = true;
        CurrentCooldown = int.MaxValue; // Prevent re-activation via CanActivate()
        // Actual gold purchase and locking is handled by TradingDay:
        //   long price = (long)(bestBidPrice * (1.0 - DiscountRate));
        //   long cost = price * PurchaseQuantity;
        //   player.SpendFrozenMora(cost) or player.AddMora(-cost)
        //   player.AddLockedGold(PurchaseQuantity, currentTick + LockDuration);
    }

    public void ResetDaily()
    {
        _used = false;
        CurrentCooldown = 0;
    }
}
